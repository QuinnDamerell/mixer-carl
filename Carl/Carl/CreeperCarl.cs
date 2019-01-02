using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carl
{
    public class CreeperCarl
    {
        class UserTime
        {
            public DateTime Joined;
            public DateTime LastSeen;
        }

        class UserActivtyEntry
        {
            public Dictionary<int, UserTime> ActiveChannels;
        }

        private static CreeperCarl s_instance;
        ConcurrentDictionary<int, UserActivtyEntry> m_userMap = new ConcurrentDictionary<int, UserActivtyEntry>();
        ICarl m_carl;
        DateTime m_roundUpdateStartTime = DateTime.MaxValue;
        DateTime m_previousRoundUpdateStartTime = DateTime.Now;
        int m_currentViewerCount = 0;
        int m_viewCountAccumlator = 0;
        ICarl m_userActivityCallback;

        Thread m_userChecker;

        public CreeperCarl(ICarl carl)
        {
            s_instance = this;
            m_carl = carl;
            m_userChecker = new Thread(ChannelUserCheckerThread);
            m_userChecker.IsBackground = false;
            m_userChecker.Start();
        }

        public static List<int> GetActiveChannelIds(int userId)
        {
            if (s_instance != null)
            {
                return s_instance.InternalGetActiveChannels(userId);
            }
            return null;
        }

        public static int GetViewerCount()
        {
            if (s_instance != null)
            {
                return s_instance.m_currentViewerCount;
            }
            return 0;
        }

        //public async void OnCommand(string command, ChatMessage msg)
        //{
        //    if (command.Equals("userstats"))
        //    {
        //        await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"I'm currently chatting with {m_currentViewerCount.ToString("n0", Carl.Culture)} viewers on {m_channelTracker.Count.ToString("n0", Carl.Culture)} mixer channels.", msg.IsWhisper);
        //    }
        //}

        private List<int> InternalGetActiveChannels(int userId)
        {
            List<int> activeChannels = new List<int>();
            UserActivtyEntry entry = null;
            DateTime now = DateTime.Now;
            if (m_userMap.TryGetValue(userId, out entry))
            {
                // The user exists, update the active list.
                lock (entry.ActiveChannels)
                {
                    foreach (var pair in entry.ActiveChannels)
                    {
                        activeChannels.Add(pair.Key);
                    }
                }
                return activeChannels.Count == 0 ? null : activeChannels;
            }
            return null;
        }

        public void OnUserActivity(UserActivity activity)
        {
            if (activity.IsJoin)
            {
                AddOrUpdateUserToChannel(activity.UserId, activity.ChannelId);
            }
            else
            {
                RemoveUserFromChannel(activity.UserId, activity.ChannelId);
            }
        }

        private void AddOrUpdateUserToChannel(int userId, int channelId)
        {
            // Make sure the user has an entry.
            UserActivtyEntry entry = null;
            while (!m_userMap.TryGetValue(userId, out entry))
            {
                // Create a new entry for this user.
                entry = new UserActivtyEntry()
                {
                    ActiveChannels = new Dictionary<int, UserTime>()
                };

                // Try to add the user to the map.
                m_userMap.TryAdd(userId, entry);                
            }

            // Check if the user is already known to be watching or not.
            bool fireNotification = false;
            DateTime joined = DateTime.MinValue;
            lock (entry.ActiveChannels)
            {
                UserTime time;
                if (entry.ActiveChannels.TryGetValue(channelId, out time))
                {
                    // Update the last time we saw this 
                    entry.ActiveChannels[channelId].LastSeen = DateTime.Now;
                }
                else
                {
                    // Create a new entry.
                    joined = DateTime.Now;
                    fireNotification = true;

                    time = new UserTime()
                    {
                        Joined = joined,
                        LastSeen = DateTime.Now
                    };

                    // Add it to the user's list
                    entry.ActiveChannels.Add(channelId, time);
                }
            }         

            // If we should fire a notification and we found a new user fire the event.
            if (fireNotification)
            {
                FireUserActivity(userId, channelId, true, joined);
            }
        }

        private void RemoveUserFromChannel(int userId, int channelId)
        {
            UserActivtyEntry entry = null;
            DateTime joined = DateTime.MinValue;
            bool fireNotification = false;

            // Try to find the user in our map.
            if (m_userMap.TryGetValue(userId, out entry))
            {
                // The users exists, remove this activity.
                lock (entry.ActiveChannels)
                {
                    // Try to remove the value, if it exists it will succeeded.
                    UserTime time;
                    if (entry.ActiveChannels.Remove(channelId, out time))
                    {
                        // The user was here, clear them out now.
                        fireNotification = true;
                        joined = time.Joined;
                    }

                    // If the maps is now empty, remove the user from the user map
                    if(entry.ActiveChannels.Count == 0)
                    {
                        m_userMap.TryRemove(userId, out entry);
                    }
                }
            }

            if (fireNotification)
            {
                FireUserActivity(userId, channelId, false, joined);
            }
        }

        private void FireUserActivity(int userId, int channelId, bool isJoined, DateTime joined)
        {
            m_carl.OnAdvanceUserActivity(new AdvanceUserActivity()
            {
                ChannelId = channelId,
                UserId = userId,
                IsJoin = isJoined,
                Joined = joined
            });
        }

        #region User Finder

        const int c_channelUserPageLimit = 100;
        const int c_sleepyTimeBetweenChannelChecksMs = 100;
        const int c_minSleepyTimeBetweenRoundsSeconds = 30;

        ConcurrentDictionary<int, int> m_channelTracker = new ConcurrentDictionary<int, int>();

        public void OnChatConnectionChanged(int channelId, ChatConnectionState state)
        {
            if (state == ChatConnectionState.Connected)
            {
                m_channelTracker.TryAdd(channelId, 0);
            }
            else
            {
                int temp;
                m_channelTracker.TryRemove(channelId, out temp);

                // We will try to remove this channel for all users, this will only do something for users that are watching.
                foreach (var pair in m_userMap)
                {
                    RemoveUserFromChannel(pair.Key, channelId);
                }
            }
        }

        private async void ChannelUserCheckerThread()
        {
            // Setup
            m_previousRoundUpdateStartTime = DateTime.Now;
            m_roundUpdateStartTime = DateTime.Now;

            int updateRound = 1;
            while (true)
            {
                DateTime now = DateTime.Now;
                try
                {
                    // Try to find one channel that isn't updated yet.
                    bool udpatedChannel = false;
                    foreach (var pair in m_channelTracker)
                    {
                        if (pair.Value < updateRound)
                        {
                            // Try to update the channel viewers.
                            if (await UpdateChannelViewerList(pair.Key))
                            {
                                // if successful, update the round value.
                                m_channelTracker.TryUpdate(pair.Key, updateRound, pair.Value);
                            }

                            // Break after we process one channel to respect the sleepy time.
                            udpatedChannel = true;
                            break;
                        }
                    }

                    // Update in real time if we know it's higher.
                    if (m_viewCountAccumlator > m_currentViewerCount)
                    {
                        m_currentViewerCount = m_viewCountAccumlator;
                    }

                    // If we didn't update a channel go to the next round.
                    if (!udpatedChannel && m_channelTracker.Count != 0)
                    {
                        // Dump the current viewer count
                        m_currentViewerCount = m_viewCountAccumlator;
                        m_viewCountAccumlator = 0;

                        // Make sure we sleep for the min amount of time.
                        double elaspedSeconds = (DateTime.Now - m_roundUpdateStartTime).TotalSeconds;
                        double sleepTime = c_minSleepyTimeBetweenRoundsSeconds - elaspedSeconds;
                        if (sleepTime > 0)
                        {
                            Logger.Info($"Viewer tracker round update {updateRound} done in {elaspedSeconds} seconds.");
                            Thread.Sleep((int)(sleepTime * 1000));
                        }
                        else
                        {
                            Logger.Info($"Viewer tracker round update {updateRound} done but it took too long to sleep. {elaspedSeconds} seconds.");
                        }
                        m_previousRoundUpdateStartTime = m_roundUpdateStartTime;
                        m_roundUpdateStartTime = DateTime.Now;
                        updateRound++;

                        // Run a quick loop to remove any entires that aren't actually wathing anymore.
                        RemoveOldEntries();

                        continue;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("Channel checker hit an exception while running.", e);
                }

                Thread.Sleep(c_sleepyTimeBetweenChannelChecksMs);
            }
        }

        private void RemoveOldEntries()
        {
            List<Tuple<int, int>> toRemove = new List<Tuple<int, int>>();
            // For all users...
            foreach(var pair in m_userMap)
            {
                lock(pair.Value.ActiveChannels)
                {
                    // For all of the channels they are watching.
                    foreach(var innerPair in pair.Value.ActiveChannels)
                    {
                        // If the time we saw this entry was before the last round update, assume we missed the notification
                        // and try to remove the channel from the user.
                        if(innerPair.Value.LastSeen < m_previousRoundUpdateStartTime)
                        {
                            toRemove.Add(new Tuple<int, int>(pair.Key, innerPair.Key));
                        }
                    }
                }
            }

            // Remove everything we found
            foreach(var pair in toRemove)
            {
                RemoveUserFromChannel(pair.Item1, pair.Item2);
            }            
        }

        private async Task<bool> UpdateChannelViewerList(int channelId)
        {
            List<int> viewers = await GetUserIdsWatchingChannel(channelId);
            m_viewCountAccumlator += viewers.Count;
            foreach (int viewer in viewers)
            {
                AddOrUpdateUserToChannel(viewer, channelId);
            }
            return true;
        }

        private class ChatUser
        {
            public int userId;
        }

        private async Task<List<int>> GetUserIdsWatchingChannel(int channelId)
        {
            const int c_limit = 100;
            List<int> knownUsers = new List<int>();
            int pageCount = 0;
            while (pageCount < c_channelUserPageLimit)
            {
                // Setup the call
                try
                {
                    // Get the response
                    string res = await MixerUtils.MakeMixerHttpRequest($"api/v1/chats/{channelId}/users?limit={c_limit}&page={pageCount}&order=userName:asc&fields=userId");

                    // Parse it.
                    List<ChatUser> users = JsonConvert.DeserializeObject<List<ChatUser>>(res);

                    // Add it to our list.
                    foreach (var user in users)
                    {
                        knownUsers.Add(user.userId);
                    }

                    // If we hit the end, get out.
                    if (users.Count < c_limit)
                    {
                        break;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Failed to query chat users API.", e);
                    break;
                }

                pageCount++;
            }
            return knownUsers;
        }

        #endregion
    }
}

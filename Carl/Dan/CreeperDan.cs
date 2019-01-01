using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Carl.Dan
{
    public class CreeperDan : Dan, IFirehoseUserActivityListener, IFirehoseChatConnectionChanged, IFirehoseCommandListener
    {
        class UserActivtyEntry
        {
            public Dictionary<int, DateTime> ActiveChannels;
        }

        ConcurrentDictionary<int, UserActivtyEntry> m_userMap = new ConcurrentDictionary<int, UserActivtyEntry>();
        private static CreeperDan s_instance;
        DateTime m_roundUpdateStartTime = DateTime.MaxValue;
        DateTime m_previousRoundUpdateStartTime = DateTime.Now;
        int m_currentViewerCount = 0;
        int m_viewCountAccumlator = 0;
        ICarl m_userActivityCallback;

        Thread m_userChecker;

        public CreeperDan(IFirehose firehose, ICarl userActivityCallback)
            : base(firehose)
        {
            m_userActivityCallback = userActivityCallback;
            m_firehose.SubChatConnectionChanged(this);
            m_firehose.SubUserActivity(this);
            m_firehose.SubCommandListener(this);
            s_instance = this;

            m_userChecker = new Thread(ChannelUserCheckerThread);
            m_userChecker.IsBackground = false;
            m_userChecker.Start();
        }

        public static List<int> GetActiveChannelIds(int userId)
        {
            if(s_instance != null)
            {
                return s_instance.InternalGetActiveChannels(userId);
            }
            return null;
        }

        public static int GetViewerCount()
        {
            if(s_instance != null)
            {
                return s_instance.m_currentViewerCount;
            }
            return 0;
        }

        public async void OnCommand(string command, ChatMessage msg)
        {
            if(command.Equals("userstats"))
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"I'm currently chatting with {m_currentViewerCount.ToString("n0", Carl.Culture)} viewers on {m_channelTracker.Count.ToString("n0", Carl.Culture)} mixer channels.", msg.IsWhisper);
            }
        }

        private List<int> InternalGetActiveChannels(int userId)
        {
            List<int> activeChannels = new List<int>();
            UserActivtyEntry entry = null;
            DateTime now = DateTime.Now;
            if (m_userMap.TryGetValue(userId, out entry))
            {
                // The user exists, update the active list.
                List<int> toRemove = new List<int>();
                lock (entry.ActiveChannels)
                {
                    foreach(var pair in entry.ActiveChannels)
                    {
                        // Check if the users is older than the start of the previous round.
                        // If that's true, the user isn't in the channel anymore.
                        if ((pair.Value - m_previousRoundUpdateStartTime).TotalMinutes < 0)
                        {
                            toRemove.Add(pair.Key);
                        }
                        else
                        {
                            activeChannels.Add(pair.Key);
                        }
                    }             
                }

                // Clean up old channels.
                foreach (int chan in toRemove)
                {
                    RemoveUserFromChannel(userId, chan, true);
                }
                return activeChannels.Count == 0 ? null : activeChannels;
            }
            return null;
        }

        public void OnUserActivity(UserActivity activity)
        {
            if(activity.IsFromCreeperDan)
            {
                return;
            }

            if(activity.IsJoin)
            {
                // Make sure we know the user is in here.
                AddOrUpdateUserToChannel(activity.UserId, activity.ChannelId, false);
            }
            else
            {
                RemoveUserFromChannel(activity.UserId, activity.ChannelId, false);
            }
        }

        private void AddOrUpdateUserToChannel(int userId, int channelId, bool fireNotificaiton)
        {
            UserActivtyEntry entry = null;
            while (true)
            {                
                if (m_userMap.TryGetValue(userId, out entry))
                {
                    // The user exists, update the active list.
                    lock (entry.ActiveChannels)
                    {
                        // Set or update the join time.
                        entry.ActiveChannels[channelId] = DateTime.Now;
                    }
                    break;
                }
                else
                {
                    // The user doesn't exist, add them into the map.
                    entry = new UserActivtyEntry()
                    {
                        ActiveChannels = new Dictionary<int, DateTime>()
                    };

                    // Set the current time as the join time.
                    entry.ActiveChannels[channelId] = DateTime.Now;

                    if (!m_userMap.TryAdd(userId, entry))
                    {
                        // Someone else already added it, try again.
                        continue;
                    }
                    break;
                }
            }

            if (fireNotificaiton)
            {
                FireUserActivity(userId, channelId, true);
            }
        }

        private void RemoveUserFromChannel(int userId, int channelId, bool fireNotificaiton)
        {
            UserActivtyEntry entry = null;
            if (m_userMap.TryGetValue(userId, out entry))
            {
                // The users exists, remove this activity.
                lock (entry.ActiveChannels)
                {
                    entry.ActiveChannels.Remove(channelId);
                }                
            }

            if(fireNotificaiton)
            {
                FireUserActivity(userId, channelId, false);
            }
        }

        private void FireUserActivity(int userId, int channelId, bool isJoined)
        {
            m_userActivityCallback.OnUserActivity(new UserActivity()
            {
                ChannelId  = channelId,
                UserId = userId,
                IsFromCreeperDan = true,
                IsJoin = isJoined
            });
        }

        #region User Finder

        const int c_channelUserPageLimit = 100;
        const int c_sleepyTimeBetweenChannelChecksMs = 100;
        const int c_minSleepyTimeBetweenRoundsSeconds = 30;

        ConcurrentDictionary<int, int> m_channelTracker = new ConcurrentDictionary<int, int>();

        public void OnChatConnectionChanged(int channelId, ChatConnectionState state)
        {
            if(state == ChatConnectionState.Connected)
            {
                m_channelTracker.TryAdd(channelId, 0);
            }
            else
            {
                int temp;
                m_channelTracker.TryRemove(channelId, out temp);
            }
        }

        private async void ChannelUserCheckerThread()
        {
            // Setup
            m_previousRoundUpdateStartTime = DateTime.Now;
            m_roundUpdateStartTime = DateTime.Now;

            int updateRound = 1;
            while(true)
            {
                DateTime now = DateTime.Now;
                try
                {
                    // Try to find one channel that isn't updated yet.
                    bool udpatedChannel = false;
                    foreach(var pair in m_channelTracker)
                    {
                        if(pair.Value < updateRound)
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
                    if(m_viewCountAccumlator > m_currentViewerCount)
                    {
                        m_currentViewerCount = m_viewCountAccumlator;
                    }

                    // If we didn't update a channel go to the next round.
                    if(!udpatedChannel && m_channelTracker.Count != 0)
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
                        continue;
                    }
                }
                catch(Exception e)
                {
                    Logger.Error("Channel checker hit an exception while running.", e);
                }

                Thread.Sleep(c_sleepyTimeBetweenChannelChecksMs);
            }
        }

        private async Task<bool> UpdateChannelViewerList(int channelId)
        {
            List<int> viewers = await GetUserIdsWatchingChannel(channelId);
            m_viewCountAccumlator += viewers.Count;
            foreach (int viewer in viewers)
            {
                AddOrUpdateUserToChannel(viewer, channelId, true);
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

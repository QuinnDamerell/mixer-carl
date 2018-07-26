using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Carl.Dan
{
    public class CreeperDan : Dan, IFirehoseUserActivityListener
    {
        const int c_userChannelTimeoutMinutes = 1440;

        class UserActivtyEntry
        {
            public Dictionary<int, DateTime> ActiveChannels;
        }

        ConcurrentDictionary<string, UserActivtyEntry> m_userMap = new ConcurrentDictionary<string, UserActivtyEntry>();
        private static CreeperDan s_instance;

        public CreeperDan(IFirehose firehose)
            : base(firehose)
        {
            m_firehose.SubUserActivity(this);
            s_instance = this;
        }

        public static List<int> GetActiveChannelIds(string userName)
        {
            if(s_instance != null)
            {
                return s_instance.InternalGetActiveChannels(userName);
            }
            return null;
        }

        private List<int> InternalGetActiveChannels(string userName)
        {
            List<int> activeChannels = new List<int>();
            UserActivtyEntry entry = null;
            DateTime now = DateTime.Now;
            if (m_userMap.TryGetValue(userName.ToLower(), out entry))
            {
                // The user exists, update the active list.
                lock (entry.ActiveChannels)
                {
                    List<int> remove = new List<int>();
                    foreach(var pair in entry.ActiveChannels)
                    {
                        // Check if the user has been in this channel for a long time. 
                        // If so we will assume we missed the leave message and remove them.
                        if ((now - pair.Value).TotalMinutes > c_userChannelTimeoutMinutes)
                        {
                            remove.Add(pair.Key);
                        }
                        else
                        {
                            activeChannels.Add(pair.Key);
                        }
                    }

                    // Clean up.
                    foreach(int i in remove)
                    {
                        entry.ActiveChannels.Remove(i);
                    }
                }
                return activeChannels.Count == 0 ? null : activeChannels;
            }
            return null;
        }

        public void OnUserActivity(UserActivity activity)
        {
            if(activity.IsJoin)
            {
                while (true)
                {
                    UserActivtyEntry entry = null;
                    if (m_userMap.TryGetValue(activity.UserName.ToLower(), out entry))
                    {
                        // The user exists, update the active list.
                        lock(entry.ActiveChannels)
                        {
                            // Set or update the join time.
                            entry.ActiveChannels[activity.ChannelId] = DateTime.Now;
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
                        entry.ActiveChannels[activity.ChannelId] = DateTime.Now;

                        if (!m_userMap.TryAdd(activity.UserName.ToLower(), entry))
                        {
                            // Someone else already added it, try again.
                            continue;
                        }
                        break;
                    }
                }
            }
            else
            {
                UserActivtyEntry entry = null;
                if (m_userMap.TryGetValue(activity.UserName.ToLower(), out entry))
                {
                    // The users exists, remove this activity.
                    lock (entry.ActiveChannels)
                    {
                        entry.ActiveChannels.Remove(activity.ChannelId);
                    }
                }
            }
        }
    }
}

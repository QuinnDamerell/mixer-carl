using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Carl.Dan
{
    public class CreeperDan : Dan, IFirehoseUserActivityListener
    {
        class UserActivtyEntry
        {
            public List<int> ActiveChannels;
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
            if (m_userMap.TryGetValue(userName.ToLower(), out entry))
            {
                // The user exists, update the active list.
                lock (entry.ActiveChannels)
                {
                    foreach(int i in entry.ActiveChannels)
                    {
                        activeChannels.Add(i);
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
                            entry.ActiveChannels.Add(activity.ChannelId);
                        }
                        break;
                    }
                    else
                    {
                        // The user doesn't exist, add them into the map.
                        entry = new UserActivtyEntry()
                        {
                            ActiveChannels = new List<int>()
                            {
                                activity.ChannelId
                            }
                        };
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

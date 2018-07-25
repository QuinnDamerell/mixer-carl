﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Carl
{
    public class Firehose : IFirehose
    {
        private ICarl m_callback;
        private int m_uniqueId;

        public Firehose(ICarl callback)
        {
            Random rand = new Random(DateTime.Now.Millisecond);
            m_uniqueId = rand.Next();
            m_callback = callback;
        }

        public int GetUniqueId()
        {
            return m_uniqueId;
        }

        #region Chat Messages

        IFirehoseChatMessageListener m_listener = null;
        int m_chatMessageChannelFilter = -1;

        public void SubChatMessages(IFirehoseChatMessageListener listener)
        {
            m_listener = listener;
        }

        public void UnSubChatMessages()
        {
            m_listener = null;
        }

        public void UpdateChatMessagesFilter(int channelId)
        {
            m_chatMessageChannelFilter = channelId;
        }

        public void PubChatMessage(ChatMessage msg)
        {
            IFirehoseChatMessageListener l = m_listener;
            if (l != null)
            {
                if (m_chatMessageChannelFilter == -1 || m_chatMessageChannelFilter == msg.ChannelId)
                {
                    l.OnChatMessage(msg);
                }
            }
        }

        public async Task<bool> SendMessage(int channelId, string message)
        {
            return await m_callback.SendChatMessage(channelId, message);
        }

        public async Task<bool> SendWhisper(int channelId, string targetUserName, string message)
        {
            return await m_callback.SendWhisper(channelId, targetUserName, message);
        }

        #endregion

        #region User Activity

        IFirehoseUserActivityListener m_activityListener = null;
        int m_userActivityChannelFilter = -1;

        public void PubUserActivity(UserActivity activity)
        {
            IFirehoseUserActivityListener l = m_activityListener;
            if (l != null)
            {
                if (m_userActivityChannelFilter == -1 || m_userActivityChannelFilter == activity.ChannelId)
                {
                    l.OnUserActivity(activity);
                }
            }
        }

        public void SubUserActivity(IFirehoseUserActivityListener listener)
        {
            m_activityListener = listener;
        }

        public void UnSubUserActivity()
        {
            m_activityListener = null;
        }

        public void UpdateUserActivityFilter(int channelId)
        {
            m_userActivityChannelFilter = channelId;
        }

        #endregion

    }
}

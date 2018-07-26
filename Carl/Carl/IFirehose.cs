using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Carl
{
    public class ChatMessage
    {
        public string UserName;
        public int UserId;
        public int ChannelId; // For co-streams this tells you which they joined.
        public string Text;
        public bool IsWhisper;
    }

    public class UserActivity
    {
        public string UserName;
        public bool IsJoin;
        public int ChannelId; // For co-streams this tells you which they joined.
    }

    public enum ChatConnectionState
    {
        Connected,
        Disconnected
    }

    public interface IFirehoseChatMessageListener
    {
        void OnChatMessage(ChatMessage msg);
    }

    public interface IFirehoseUserActivityListener
    {
        void OnUserActivity(UserActivity activity);
    }

    public interface IFirehoseChatConnectionChanged
    {
        void OnChatConnectionChanged(int channelId, ChatConnectionState state);
    }

    public interface IFirehose
    {
        // 
        // Chat message APIs

        void SubChatMessages(IFirehoseChatMessageListener listener);
        void UnSubChatMessages();
        void UpdateChatMessagesFilter(int channelId);
        Task<bool> SendMessage(int channelId, string message);
        Task<bool> SendWhisper(int channelId, string targetUserName, string message);

        //
        // User Activity APIs

        void SubUserActivity(IFirehoseUserActivityListener listener);
        void UnSubUserActivity();
        void UpdateUserActivityFilter(int channelId);

        // 
        // Chat Connection APIs

        void SubChatConnectionChanged(IFirehoseChatConnectionChanged listner);
        void UnSubChatConnectionChanged();
    }
}

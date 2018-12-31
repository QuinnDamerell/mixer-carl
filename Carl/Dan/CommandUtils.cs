using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Carl.Dan
{
    public class CommandUtils
    {
        static public async Task<int> GlobalWhisper(IFirehose firehose, string userName, string message)
        {
            int? userId = await MixerUtils.GetUserId(userName);
            if (!userId.HasValue)
            {
                return 0;
            }
            return await GlobalWhisper(firehose, userId.Value, userName, message);
        }

        static public async Task<int> GlobalWhisper(IFirehose firehose, int userId, string message)
        {
            string userName = await MixerUtils.GetUserName(userId);
            if (String.IsNullOrWhiteSpace(userName))
            {
                return 0;
            }
            return await GlobalWhisper(firehose, userId, userName, message);
        }

        static public async Task<int> GlobalWhisper(IFirehose firehose, int userId, string userName, string message)
        {
            List<int> channelIds = CreeperDan.GetActiveChannelIds(userId);
            if (channelIds == null)
            {
                return 0;
            }
            else
            {
                // Whisper them the message in all of the channels.
                int successCount = 0;
                foreach (int channelId in channelIds)
                {
                    if (await firehose.SendWhisper(channelId, userName, message))
                    {
                        successCount++;
                    }
                }
                return successCount;
            }
        }

        // Remove the command and returns the entire string after.
        static public string GetCommandBody(string fullCommand)
        {
            int firstSpace = fullCommand.IndexOf(' ');
            if(firstSpace == -1)
            {
                return null;
            }
            string body = fullCommand.Substring(firstSpace).Trim();
            return String.IsNullOrWhiteSpace(body) ? null : body;
        }

        static public string GetSingleWordArgument(string fullCommand)
        {
            string body = GetCommandBody(fullCommand);
            if(body == null)
            {
                return null;
            }

            string firstArg = body;
            int firstSpace = firstArg.IndexOf(' ');
            if (firstSpace != -1)
            {
                firstArg = firstArg.Substring(0, firstSpace);
            }

            // Remove any @ tags
            if(firstArg.StartsWith("@"))
            {
                firstArg = firstArg.Substring(1);
            }
            return firstArg;
        }

        static public string GetStringAfterFirstTwoWords(string fullCommand)
        {
            string body = GetCommandBody(fullCommand);
            if(body == null)
            {
                return null;
            }
            return GetCommandBody(body);
        }

        static public string GetSecondSingleWordArgument(string fullCommand)
        {
            string body = GetCommandBody(fullCommand);
            if (body == null)
            {
                return null;
            }
            return GetSingleWordArgument(body);
        }

        public static async Task<string> FormatChannelIds(List<int> channelIds, int lengthLimit = int.MaxValue)
        {
            List<string> names = new List<string>();
            foreach(int i in channelIds)
            {
                names.Add("@" + await MixerUtils.GetChannelName(i));
            }
            return FormatWordList(names, lengthLimit);
        }

        public static async Task<string> FormatUserIds(List<int> userIds, int lengthLimit = int.MaxValue)
        {
            List<string> names = new List<string>();
            foreach (int i in userIds)
            {
                names.Add("@" + await MixerUtils.GetUserName(i));
            }
            return FormatWordList(names, lengthLimit);
        }

        public static string FormatWordList(List<string> words, int lengthLimit = int.MaxValue)
        {
            string output = "";
            for(int i = 0; i < words.Count; i++)
            {
                if(i != 0)
                {
                    output += ", ";
                }
                if(words.Count > 1 && i + 1 == words.Count)
                {
                    output += "and ";
                }
                output += words[i];

                if(output.Length > lengthLimit - 20)
                {
                    output += $" and {words.Count - i} more";
                    break;
                }
            }
            return output;
        }

        static public async Task<bool> SendAccessDenied(IFirehose firehose, ChatMessage msg)
        {
            return await SendAccessDenied(firehose, msg.ChannelId, msg.UserName);
        }

        static public async Task<bool> SendAccessDenied(IFirehose firehose, int channelId, string userName)
        {
            return await SendResponse(firehose, channelId, userName, "You don't have permissions to do that 🤨", true);
        }

        public static async Task<bool> SendCantFindUser(IFirehose m_firehose, ChatMessage msg, string failedToFindUserName)
        {
            return await SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"It doesn't look like {failedToFindUserName} is active on Mixer right now. (Maybe they're lurking?)", msg.IsWhisper);
        }

        public static async Task<bool> SendMixerUserNotFound(IFirehose m_firehose, ChatMessage msg, string failedToFindUserName)
        {
            return await SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"I can't find a user named {failedToFindUserName} on Mixer. Is that spelled correctly? 😕", msg.IsWhisper);
        }

        public static async Task<bool> CheckForMutualFriendsAndMessageIfNot(IFirehose m_firehose, ChatMessage msg, int actionReceiverId, string action)
        {
            if (HasAdvancePermissions(msg.UserId) || FriendlyDan.AreMutualFriends(msg.UserId, actionReceiverId))
            {
                return true;
            }
            else
            {
                await SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"You need to be mutual friends with {await MixerUtils.GetUserName(actionReceiverId)} before you can {action} them. You both need to friend each other with the \"^friends add\" command.", msg.IsWhisper);
                return false;
            }
        }

        static public async Task<bool> SendResponse(IFirehose firehose, ChatMessage msg, string message, bool forceWhisper = true)
        {
            return await SendResponse(firehose, msg.ChannelId, msg.UserName, message, msg.IsWhisper || forceWhisper);
        }

        static public async Task<bool> SendResponse(IFirehose firehose, int channelId, string userName, string message, bool whisper)
        {
            Logger.Info($"Sent {(whisper ? "whisper" : "message")} to {userName}: {message}");
            bool success = false;
            if (whisper)
            {
                success = await firehose.SendWhisper(channelId, userName, message);
            }
            else
            {
                success = await firehose.SendMessage(channelId, message);
            }

            if(!success)
            {
                Logger.Error($"Failed to send message '{message}' to {userName} in channel {channelId}");
            }
            return success;
        }

        public static bool HasAdvancePermissions(int userId)
        {
            return userId == 213923 || userId == 354879;
        }

        public static bool ShouldForceIsWhisper(ChatMessage msg)
        {
            if(HasAdvancePermissions(msg.UserId))
            {
                return msg.IsWhisper;
            }
            return true;
        }
    }
}

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
            // Find the userId.
            int? userId = await MixerUtils.GetUserId(userName);
            if(!userId.HasValue)
            {
                return 0;
            }

            List<int> channelIds = CreeperDan.GetActiveChannelIds(userId.Value);
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

        static public async Task<bool> SendAccessDenied(IFirehose firehose, ChatMessage msg, bool forceWhisper = false)
        {
            return await SendAccessDenied(firehose, msg.ChannelId, msg.UserName, msg.IsWhisper || forceWhisper);
        }

        static public async Task<bool> SendAccessDenied(IFirehose firehose, int channelId, string userName, bool whisper)
        {
            return await SendResponse(firehose, channelId, userName, "You don't have permissions to do that 🤨", whisper);
        }

        public static async Task<bool> SendCantFindUser(IFirehose m_firehose, ChatMessage msg, string failedToFindUserName)
        {
            return await SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"It doesn't look like {failedToFindUserName} is on Mixer right now. (Maybe they're lurking?)", msg.IsWhisper);
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
    }
}

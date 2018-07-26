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
            List<int> channelIds = CreeperDan.GetActiveChannelIds(userName);
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

        static public async Task<bool> SendAccessDenied(IFirehose firehose, int channelId, string userName, bool whisper)
        {
            return await SendResponse(firehose, channelId, userName, "You don't have permissions to do that 🤨", whisper);
        }

        static public async Task<bool> SendResponse(IFirehose firehose, int channelId, string userName, string message, bool whisper)
        {
            Logger.Info($"Sent {(whisper ? "whisper" : "message")} to {userName}: {message}");
            if (whisper)
            {
                return await firehose.SendWhisper(channelId, userName, message);
            }
            else
            {
                return await firehose.SendMessage(channelId, message);
            }
        }
    }
}

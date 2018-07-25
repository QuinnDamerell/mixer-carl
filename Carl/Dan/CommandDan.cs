using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Carl;

namespace Carl.Dan
{
    public class CommandDan :  Dan, IFirehoseChatMessageListener
    {
        ConcurrentDictionary<int, bool> m_mockDict = new ConcurrentDictionary<int, bool>();

        public CommandDan(IFirehose firehose) 
            : base(firehose)
        {
            m_firehose.SubChatMessages(this);
        }

        public async void OnChatMessage(ChatMessage msg)
        {
            if (msg.Text.StartsWith("^") || msg.IsWhisper)
            {
                if (IsCommand(msg, "echo"))
                {
                    await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, msg.Text.Substring(5));
                    Logger.Info($"Sending echo! {msg.Text}");
                }
                else if (IsCommand(msg, "hello"))
                {
                    await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, "Oh Hi!");
                    Logger.Info($"Sending Hi!");
                }
                else if (IsCommand(msg, "locate"))
                {
                    await HandleLocateCommand(msg);
                }
                else if (IsCommand(msg, "help"))
                {
                    await HandleHelp(msg);
                }
                else if (IsCommand(msg, "whisper"))
                {
                    await HandleWhisperCommand(msg);
                }
                else if (IsCommand(msg, "mock"))
                {
                    await HandleMockToggle(msg, true);
                }
                else if (IsCommand(msg, "publicmock"))
                {
                    await HandleMockToggle(msg, false);
                }
            }
            else
            {
                // Check if we need to mock.
                CheckForMock(msg);
            }
        }

        private async Task HandleHelp(ChatMessage msg)
        {
            if(!HasPermissions(msg.UserId))
            {
                return;
            }
            await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, "Commands: echo, hello, help, locate, whisper.");
        }

        private async Task HandleLocateCommand(ChatMessage msg)
        {
            string userName = msg.Text.Substring(7).Trim();
            List<int> channelIds = CreeperDan.GetActiveChannelIds(userName);
            if (channelIds == null)
            {
                await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, $"User '{userName}' not found in any channels");
                Logger.Info($"locate failed to find user {userName}");
            }
            else
            {
                // Go async to get the names.
                ThreadPool.QueueUserWorkItem(async (object _) =>
                {
                    string output = $"User {userName} found in: ";
                    foreach (int i in channelIds)
                    {
                        output += $"{await MixerUtils.GetChannelName(i)}, ";

                        // Check for the max message length.
                        if (output.Length > 300)
                        {
                            break;
                        }
                    }
                    await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, output);
                    Logger.Info($"locate found user {userName} in {channelIds.Count} channels");
                });
            }
        }

        private async Task HandleWhisperCommand(ChatMessage msg)
        {
            int secondSpace = msg.Text.IndexOf(' ', 9);
            string userName = msg.Text.Substring(8, secondSpace - 8).Trim();
            string text = msg.Text.Substring(secondSpace).Trim();

            List<int> channelIds = CreeperDan.GetActiveChannelIds(userName);
            if (channelIds == null)
            {
                await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, $"User '{userName}' not found in any channels");
                Logger.Info($"global whisper failed to find user {userName}");
            }
            else
            {
                // Whisper them the message in all of the channels.
                int successCount = 0;
                foreach (int channelId in channelIds)
                {
                    if (await m_firehose.SendWhisper(channelId, userName, $"{msg.UserName} says: {text}"))
                    {
                        successCount++;
                    }
                }
                // Respond to the user.            
                await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, $"Sent whisper to {userName} in {successCount} channels");
                Logger.Info($"Sent whisper to {userName} in {successCount} channels");
            }
        }

        private async Task HandleMockToggle(ChatMessage msg, bool isPrivate)
        {
            if (!HasPermissions(msg.UserId))
            {
                return;
            }

            // Get the user Id.
            int commandSpace = msg.Text.IndexOf(' ');
            if (commandSpace == -1)
            {
                await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, $"Invalid");
                Logger.Info($"Invalid");
            }
            string userName = msg.Text.Substring(commandSpace).Trim();
            int? userId = await MixerUtils.GetUserId(userName);
            if(!userId.HasValue || userName.Length == 0)
            {
                await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, $"User '{userName}' not found on mixer.");
                Logger.Info($"Mock can't find user {userName}");
                return;
            }

            // Add it to our map.
            bool removed = false;
            if(!m_mockDict.TryAdd(userId.Value, isPrivate))
            {
                bool test;
                m_mockDict.TryRemove(userId.Value, out test);
                removed = true;
            }

            string output = $"{(isPrivate ? "Private" : "Public")} mocking {(removed ? "removed" : "setup")} for {userName}.";
            await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, output);
            Logger.Info(output);
        }

        private bool IsCommand(ChatMessage msg, string commandName)
        {
            return msg.Text.StartsWith($"^{commandName}") || (msg.IsWhisper && msg.Text.StartsWith(commandName));
        }

        private bool HasPermissions(int userId)
        {
            return userId == 213923;
        }

        private async void CheckForMock(ChatMessage msg)
        {
            if(m_mockDict.ContainsKey(msg.UserId))
            {
                bool isPrivate = false;
                if(m_mockDict.TryGetValue(msg.UserId, out isPrivate))
                {
                    if(isPrivate)
                    {
                        await m_firehose.SendWhisper(msg.ChannelId, msg.UserName, msg.Text);
                    }
                    else
                    {
                        await m_firehose.SendMessage(msg.ChannelId, msg.Text);
                    }
                }
            }
        }
    }
}

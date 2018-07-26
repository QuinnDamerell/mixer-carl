using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Carl;
using Newtonsoft.Json;

namespace Carl.Dan
{
    public class CommandDan :  Dan, IFirehoseChatMessageListener
    {
        const int c_extensionActiveTimeoutSeconds = 600;

        ConcurrentDictionary<int, bool> m_mockDict = new ConcurrentDictionary<int, bool>();
        HttpClient m_client = new HttpClient();

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
                else if (IsCommand(msg, "summon"))
                {
                    await HandleSummon(msg);
                }
            }
            else
            {
                // Check if we need to mock.
                CheckForMock(msg);
            }
        }

        private async Task<int> GlobalWhisper(string userName, string message)
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
                    if (await m_firehose.SendWhisper(channelId, userName, message))
                    {
                        successCount++;
                    }
                }
                return successCount;
            }
        }

        private async Task<bool> SendResponse(int channelId, string userName, string message, bool whisper)
        {
            Logger.Info($"Sent {(whisper ? "whisper" : "message")} to {userName}: {message}");
            if(whisper)
            {
                return await m_firehose.SendWhisper(channelId, userName, message);
            }
            else
            {
                return await m_firehose.SendMessage(channelId, message);
            }
        }

        private async Task HandleHelp(ChatMessage msg)
        {
            if(!HasPermissions(msg.UserId))
            {
                return;
            }
            await SendResponse(msg.ChannelId, msg.UserName, "Commands: echo, hello, help, locate, whisper, mock, publicmock, summon", true);
        }

        private async Task HandleLocateCommand(ChatMessage msg)
        {
            string userName = msg.Text.Substring(7).Trim();
            List<int> channelIds = CreeperDan.GetActiveChannelIds(userName);
            if (channelIds == null)
            {
                await SendResponse(msg.ChannelId, msg.UserName, $"User '{userName}' not found in any channels", msg.IsWhisper);
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
                    await SendResponse(msg.ChannelId, msg.UserName, output, msg.IsWhisper);
                });
            }
        }

        private async Task HandleWhisperCommand(ChatMessage msg)
        {
            if (!HasPermissions(msg.UserId))
            {
                return;
            }

            int secondSpace = msg.Text.IndexOf(' ', 9);
            string userName = msg.Text.Substring(8, secondSpace - 8).Trim();
            string text = msg.Text.Substring(secondSpace).Trim();

            int whispers = await GlobalWhisper(userName, $"{msg.UserName} says: {text}");
            if (whispers == 0)
            {
                await SendResponse(msg.ChannelId, msg.UserName, $"User '{userName}' not found in any channels", msg.IsWhisper);
            }
            else
            {
                await SendResponse(msg.ChannelId, msg.UserName, $"Sent whisper to {userName} in {whispers} channels", msg.IsWhisper);
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

        #region Summon

        private async Task HandleSummon(ChatMessage msg)
        {
            // Get the args
            string[] args = msg.Text.Split(' ');
            if (args.Length < 2)
            {
                return;
            }
            string summonUserName = args[1];
            string channelName = await MixerUtils.GetChannelName(msg.ChannelId);
            

            if (await CheckIfUserHasAnActiveExtension(summonUserName))
            {
                // The user has an active extension
                if(await PostSummonToExtension(summonUserName, msg.UserName, channelName))
                {
                    await SendResponse(msg.ChannelId, msg.UserName, $"Sent extension summon to {summonUserName} for channel {channelName}", msg.IsWhisper);
                }
                else
                {
                    await SendResponse(msg.ChannelId, msg.UserName, $"FAILED to send extension summon to {summonUserName} for channel {channelName}", msg.IsWhisper);
                }
            }
            else
            {
                // The user doesn't have the extension! Whisper them.
                int whispers = await GlobalWhisper(summonUserName, $"{msg.UserName} summons you to @{channelName}'s channel! https://mixer.com/{channelName}");
                await SendResponse(msg.ChannelId, msg.UserName, $"Sent whisper summon to {summonUserName} for channel @{channelName} in {whispers} channels", msg.IsWhisper);
            }
        }

        private async Task<bool> CheckIfUserHasAnActiveExtension(string userName)
        {
            HttpRequestMessage request = new HttpRequestMessage();
            request.RequestUri = new Uri($"https://relay.quinndamerell.com/Blob.php?key=mixer-carl-{userName}-active");
            try
            {
                HttpResponseMessage response = await m_client.SendAsync(request);
                string res = await response.Content.ReadAsStringAsync();
                DateTime parsedTime;
                if(DateTime.TryParse(res, out parsedTime))
                {
                    return (DateTime.Now - parsedTime).TotalSeconds < c_extensionActiveTimeoutSeconds;
                }
                return false;               
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to query relay.quinndamerell.com active for user {userName}", e);
            }
            return false;
        }

        public class Command
        {
            public string Name;
            public string SubmitTime;
            public string Summoner;
            public string Channel;
        }

        public class Root
        {
            public List<Command> Commands;
        }

        private async Task<bool> PostSummonToExtension(string userToSummon, string userWhoSummoned, string channelName)
        {
            // Build the data
            Root r = new Root()
            {
                Commands = new List<Command>()
                {
                    new Command()
                    {
                        Name="Summon",
                        SubmitTime=DateTime.UtcNow.ToString("o"),
                        Summoner=userWhoSummoned,
                        Channel=channelName,
                    }
                }
            };
            string data = WebUtility.UrlEncode(JsonConvert.SerializeObject(r));

            // Make the request
            HttpRequestMessage request = new HttpRequestMessage();
            request.Method = HttpMethod.Get;
            request.RequestUri = new Uri($"https://relay.quinndamerell.com/Blob.php?key=mixer-carl-{userToSummon}-commands&data={data}");
            try
            {
                HttpResponseMessage response = await m_client.SendAsync(request);
                if(response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to submit summon command to relay.quinndamerell.com active for user {userToSummon}", e);
            }
            return false;
        }

        #endregion
    }
}

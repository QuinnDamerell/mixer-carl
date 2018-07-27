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
        ICarl m_commandCallback;

        public CommandDan(ICarl commandCallback, IFirehose firehose) 
            : base(firehose)
        {
            m_client.DefaultRequestHeaders.Add("Client-ID", "Karl");
            m_commandCallback = commandCallback;
            m_firehose.SubChatMessages(this);
        }

        public async void OnChatMessage(ChatMessage msg)
        {
            if (msg.Text.StartsWith("^") || msg.IsWhisper)
            {
                // Get the raw command.
                string command = msg.Text;
                int firstSpace = msg.Text.IndexOf(' ');
                if(firstSpace != -1)
                {
                    command = msg.Text.Substring(0, firstSpace);
                }
                if(command.StartsWith("^"))
                {
                    command = command.Substring(1);
                }
                command = command.ToLower();

                // See if we can handle it internally.
                if (command.Equals("help") || command.Equals("commands"))
                {
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"Hello @{msg.UserName}! Here's what I can do for you! Commands: hello, whisper, summon, find, echo, mock, pmock, cmock, userstats, about", true);
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, "You can access my commands globally typing '^<command>' or by whispering me a command.", true);
                }
                if (command.Equals("about"))
                {
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, "Hey there! I'm Karl! 🤗 I'm an experimental global chat observer created by @Quinninator and @BoringNameHere. To see what I can do for you, try ^commands.", true);
                }
                else if (command.Equals("hello") | command.Equals("hi"))
                {
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"👋 @{msg.UserName}", msg.IsWhisper);
                }
                else if (command.Equals("ping"))
                {
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, "Pong!", true);
                }
                else if (command.Equals("echo"))
                {
                    await HandleEcho(msg);
                }                
                else if (command.Equals("find"))
                {
                    await HandleFindCommand(msg);
                }               
                else if (command.Equals("whisper"))
                {
                    await HandleWhisperCommand(msg);
                }
                else if (command.Equals("summon"))
                {
                    await HandleSummon(msg);
                }
                else if (command.Equals("mock"))
                {
                    await HandleMockToggle(msg, true);
                }
                else if (command.Equals("pmock"))
                {
                    await HandleMockToggle(msg, false);
                }
                else if (command.Equals("cmock"))
                {
                    await HandleClearMock(msg);
                }
                else
                {
                    // If we can't handle it internally, fire it to the others.
                    m_commandCallback.OnCommand(command, msg);
                }
            }
            else
            {
                // Check if we need to mock.
                CheckForMock(msg);
            }
        }

        private async Task HandleEcho(ChatMessage msg)
        {
            if (!CommandUtils.HasAdvancePermissions(msg.UserId))
            {
                await CommandUtils.SendAccessDenied(m_firehose, msg.ChannelId, msg.UserName, true);
                return;
            }
            string body = CommandUtils.GetCommandBody(msg.Text);
            if(!String.IsNullOrWhiteSpace(body))
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, body, msg.IsWhisper);
            }
        }

        private async Task HandleFindCommand(ChatMessage msg)
        {
            string userName = CommandUtils.GetSingleWordArgument(msg.Text);
            if(userName == null)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"Find who? 🔍 You must specify a user name to find!", msg.IsWhisper);
                return;
            }

            int? userId = await MixerUtils.GetUserId(userName);
            if(!userId.HasValue)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"That's not right, I had trouble getting the user id. Try again later.", msg.IsWhisper);
                return;
            }

            // Find the user.
            List<int> channelIds = CreeperDan.GetActiveChannelIds(userId.Value);
            if (channelIds == null)
            {
                await CommandUtils.SendCantFindUser(m_firehose, msg, userName);
            }
            else
            {
                // Go async to get the names.
                ThreadPool.QueueUserWorkItem(async (object _) =>
                {
                    // Build the string.
                    bool first = true;
                    string output = $"I found {userName} in the following channels, ";
                    int count = 0;
                    foreach (int i in channelIds)
                    {
                        if(!first)
                        {
                            output += ", ";
                        }
                        first = false;

                        output += $"@{await MixerUtils.GetChannelName(i)}";
                        count++;

                        // Check for the max message length.
                        if (output.Length > 330)
                        {
                            output += $", and {channelIds.Count - count} more.";
                            break;
                        }
                    }
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, output, msg.IsWhisper);
                });
            }
        }

        private async Task HandleWhisperCommand(ChatMessage msg)
        {
            string userName = CommandUtils.GetSingleWordArgument(msg.Text);
            if(userName == null)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, "Who do you want to whisper? Give me a user name and the message you want to send.", true);
                return;
            }
            string message = CommandUtils.GetStringAfterFirstTwoWords(msg.Text);
            if(message == null)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, "What do you want to say? Give me a user name and the message you want to send.", true);
                return;
            }

            int whispers = await CommandUtils.GlobalWhisper(m_firehose, userName, $"{msg.UserName} says: {message}");
            if (whispers == 0)
            {
                await CommandUtils.SendCantFindUser(m_firehose, msg, userName);
            }
            else
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"I sent your message to {userName} in {whispers} channels", msg.IsWhisper);
            }
        }

        private async Task HandleClearMock(ChatMessage msg)
        {
            if (!CommandUtils.HasAdvancePermissions(msg.UserId))
            {
                await CommandUtils.SendAccessDenied(m_firehose, msg.ChannelId, msg.UserName, true);
                return;
            }

            // Clear all.
            m_mockDict.Clear();

            await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, "I'm not mocking anyone anymore. 😢", msg.IsWhisper);
        }

        private async Task HandleMockToggle(ChatMessage msg, bool isPrivate)
        {
            if (!CommandUtils.HasAdvancePermissions(msg.UserId))
            {
                await CommandUtils.SendAccessDenied(m_firehose, msg.ChannelId, msg.UserName, true);
                return;
            }

            // Find the user name.
            string userName = CommandUtils.GetSingleWordArgument(msg.Text);
            if (userName == null)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, "I need a user name.", true);
                return;
            }

            // Get the user id.
            int? userId = await MixerUtils.GetUserId(userName);
            if (!userId.HasValue || userName.Length == 0)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"Mock '{userName}' not found on mixer.", msg.IsWhisper);
                return;
            }

            // Update the map.
            bool removed = false;
            bool currentValue;
            if (m_mockDict.TryGetValue(userId.Value, out currentValue))
            {
                // Remove if it's the same toggle.
                if (currentValue == isPrivate)
                {
                    removed = true;
                    m_mockDict.TryRemove(userId.Value, out currentValue);
                }
                // Otherwise, toggle it
                else
                {
                    m_mockDict.TryUpdate(userId.Value, isPrivate, currentValue);
                    currentValue = isPrivate;
                }
            }
            else
            {
                // If they are not in the map, add them.
                m_mockDict.TryAdd(userId.Value, isPrivate);
                currentValue = isPrivate;
            }

            string output;
            if (removed)
            {
                output = $"I'm no longer mocking {userName}. Lucky them.";
            }
            else
            {
                output = $"I'm now {(currentValue ? "privately" : "publically")} mocking {userName} 😮";
            }
            await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, output, msg.IsWhisper);
            return;
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
            string summonUserName = CommandUtils.GetSingleWordArgument(msg.Text);
            if(summonUserName == null)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"Let me know who you want so summon. Give me a user name after the command.", msg.IsWhisper);
                return;
            }
            string channelName = await MixerUtils.GetChannelName(msg.ChannelId);
            if(channelName == null)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"Well that's not right, I had trouble finding the channel. Try again later.", msg.IsWhisper);
                return;
            }

            // Check to see if the user is running the extension.
            if (await CheckIfUserHasAnActiveExtension(summonUserName))
            {
                // The user has an active extension
                if(await PostSummonToExtension(summonUserName, msg.UserName, channelName))
                {
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"I send an extension summon to {summonUserName}", msg.IsWhisper);
                }
                else
                {
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"That's not right... I failed to send extension summon to {summonUserName}.", msg.IsWhisper);
                }
            }
            else
            {
                // The user doesn't have the extension! Whisper them.
                int whispers = await CommandUtils.GlobalWhisper(m_firehose, summonUserName, $"{msg.UserName} summons you to @{channelName}'s channel! https://mixer.com/{channelName}");
                if (whispers == 0)
                {
                    await CommandUtils.SendCantFindUser(m_firehose, msg, summonUserName);                
                }
                else
                {
                    await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"I whisper summoned {summonUserName} in {whispers} channels", msg.IsWhisper);
                }
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

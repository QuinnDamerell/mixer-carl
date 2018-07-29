using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Carl
{
    public enum ChatState
    {
        None,
        Connecting,
        Connected,
        Disconnected
    }

    class ChatConnector : ISimpleWebySocketCallbacks
    {
        public delegate void ChatWsStateChanged(ChatConnector sender, ChatState newState, bool wasError);
        public event ChatWsStateChanged OnChatWsStateChanged;

        // Keep 500 ms between sends to avoid rate limiting.
        TimeSpan c_minTimeBetweenSends = new TimeSpan(0, 0, 0, 0, 600); 

        const int c_mixerMaxMessageLength = 360;

        private int m_channelId;
        private int m_userId;
        SimpleWebySocket m_ws;
        ChatState m_state = ChatState.None;
        ICarl m_callback;
        Random m_rand;
        int? m_authCallMessageId = null;

        class ChatServerDetails
        {
            public string authkey;
            public List<string> endpoints = new List<string>();
        }

        class WebSocketMessage
        {
            public int id;
            public string type;
            public string method;
            public List<string> arguments = new List<string>();
        }

        public ChatConnector(int userId, int channelId, ICarl callback)
        {
            m_channelId = channelId;
            m_userId = userId;
            m_callback = callback;
            m_rand = new Random(DateTime.Now.Millisecond);
        }

        public int GetChannelId()
        {
            return m_channelId;
        }

        public bool IsDisconnected()
        {
            return m_state == ChatState.Disconnected;
        }

        public async Task<bool> Connect()
        {
            if(m_state != ChatState.None)
            {
                return false;
            }
            UpdateStatus(ChatState.Connecting);

            // Get the details.
            ChatServerDetails details = await GetChatServerDetails();
            if (details == null)
            {
                return false;
            }

            if (details.endpoints == null || details.endpoints.Count == 0)
            {
                Logger.Error($"No chat endpoints returned for channel: {m_channelId}");
                return false;
            }

            Random rand = new Random();
            string endpoint = details.endpoints[rand.Next(0, details.endpoints.Count)];
            m_ws = new SimpleWebySocket(this, endpoint);
            m_ws.MinTimeBetweenSends = c_minTimeBetweenSends;

            Logger.Info($"Connecting to channel {m_channelId} on server {endpoint}...");
            if (!await m_ws.Connect())
            {
                Logger.Error($"Failed to connect to chat server for channel: {m_channelId}");
                return false;
            }

            // Authorize!
            WebSocketMessage msg = new WebSocketMessage()
            {
                type = "method",
                method = "auth",
                arguments = new List<string>()
                {
                    $"{m_channelId}",
                    $"{m_userId}",
                    details.authkey
                }
            };

            if(!await SendMessage(msg, true))
            {
                Logger.Error($"Failed to send auth message to channel: {m_channelId}");
                return false;
            }

            // Note the message id
            m_authCallMessageId = msg.id;

            UpdateStatus(ChatState.Connected);
            return true;
        }

        private async Task<bool> SendMessage(WebSocketMessage msg, bool overrideState = false)
        {
            if (!overrideState && m_state != ChatState.Connected)
            {
                return false;
            }

            SimpleWebySocket ws = m_ws;
            if (ws != null)
            {
                msg.id = m_rand.Next();
                return await ws.Send(JsonConvert.SerializeObject(msg));
            }
            return false;
        }

        public async Task<bool> SendChatMessage(string message)
        {
            if(message.Length >= c_mixerMaxMessageLength)
            {
                return false;
            }
            return await SendMessage(new WebSocketMessage()
            {
                type = "method",
                method = "msg",
                arguments = new List<string>()
                {   message   }
            });
        }

        public async Task<bool> SendWhisper(string targetUserName, string message)
        {
            if (message.Length >= c_mixerMaxMessageLength)
            {
                return false;
            }
            return await SendMessage(new WebSocketMessage()
            {
                type = "method",
                method = "whisper",
                arguments = new List<string>()
                    {   targetUserName, message   }
            });
        }

        public void OnStateChanged(SimpleWebySocketState newState, bool wasUserInvoked)
        {
            switch (newState)
            {
                case SimpleWebySocketState.Connected:
                    UpdateStatus(ChatState.Connected);
                    break;
                case SimpleWebySocketState.Disconnected:
                    UpdateStatus(ChatState.Disconnected, true);
                    break;
                case SimpleWebySocketState.Connecting:
                    UpdateStatus(ChatState.Connecting);
                    break;
            }
        }

        public void UpdateStatus(ChatState newState, bool wasError = false)
        {
            switch(newState)
            {
                case ChatState.None:
                    return;
                case ChatState.Connecting:
                    if(m_state != ChatState.None)
                    {
                        return;
                    }
                    break;
                case ChatState.Connected:
                    if(m_state != ChatState.Connecting)
                    {
                        return;
                    }
                    break;
                case ChatState.Disconnected:
                    if(m_state != ChatState.Connected && m_state != ChatState.Connecting)
                    {
                        return;
                    }
                    break;
            }
            m_state = newState;
            OnChatWsStateChanged?.Invoke(this, newState, wasError);
        }

        public async Task Disconnect()
        {
            UpdateStatus(ChatState.Disconnected, false);
            SimpleWebySocket ws = m_ws;
            if(ws != null)
            {
                await ws.Disconnect();
            }
            m_callback = null;
        }

        public void OnMessage(string message)
        {
            JObject jObject = JObject.Parse(message);

            // If we are waiting on the auth message check for it.
            if (m_authCallMessageId.HasValue)
            {
                var idValue = jObject["id"];
                if (idValue != null)
                {
                    if (idValue.Value<int>() == m_authCallMessageId.Value)
                    {
                        // This is the auth response.
                        HandleAuthResponse(jObject);
                        m_authCallMessageId = null;
                        return;
                    }
                }
            }

            // Check for known messages.
            string eventValue = jObject["event"]?.ToString();
            if (eventValue != null && eventValue.Equals("ChatMessage"))
            {
                HandleChatMessage(jObject);
            }
            else if (eventValue != null && (eventValue.Equals("UserJoin") || eventValue.Equals("UserLeave")))
            {
                HandleUserActivity(jObject);
            }
            else
            {
                string method = jObject["method"]?.ToString();
                if(method != null && method.Equals("ping"))
                {
                    Logger.Info("Received ping");
                }
                string type = jObject["type"]?.ToString();
                if (type != null && type.Equals("reply"))
                {
                    string error = jObject["error"]?.ToString();
                    if(!String.IsNullOrWhiteSpace(error))
                    {
                        Logger.Error("Error received from chat server. " + message);
                    }
                }
            }
        }

        private void HandleChatMessage(JObject jObject)
        {
            // { "type":"event","event":"ChatMessage","data":{ "channel":26685532,"id":"7d98a800-8ec8-11e8-9beb-85ac1e129a1f",
            //   "user_name":"jo1e","user_id":48924287,"user_roles":["Mod","User"],"user_level":21,
            //   "user_avatar":"https://uploads.mixer.com/avatar/46c0tc85-48924287.jpg",
            //   "message":{"message":[{"type":"text","data":"","text":""},
            //      { "text":"@GodsofAeon","type":"tag","username":"GodsofAeon","id":52198890},
            //      { "type":"text","data":" There's a lot of tutorials online to follow on how to get started and setting it up overall. ","text":" There's a lot of tutorials online to follow on how to get started and setting it up overall. "}],"meta":{}}}}

            ChatMessage msg = new ChatMessage();

            JToken dataObj = jObject["data"];
            if(dataObj != null)
            {
                JToken channelValue = dataObj["channel"];
                if (channelValue != null)
                {
                    msg.ChannelId = channelValue.Value<int>();
                }

                // Filter out any messages that aren't from this channel. This will happen when people co stream.
                // The message will be picked up by another socket.
                if(msg.ChannelId != m_channelId)
                {
                    return;
                }

                JToken messageVal = dataObj["message"];
                if(messageVal != null)
                {
                    msg.Text = String.Empty;
                    JToken msgVal = messageVal["message"];
                    foreach (var child in msgVal.Children())
                    {
                        JToken text = child["text"];
                        if(text != null)
                        {
                            msg.Text += text.Value<string>();
                        }
                    }
                }
                JToken metaVal = messageVal["meta"];
                if(metaVal != null)
                {
                    msg.IsWhisper = (metaVal["whisper"] != null);
                }
                JToken userNameValue = dataObj["user_name"];
                if(userNameValue != null)
                {
                    msg.UserName = userNameValue.Value<string>();
                }
                JToken userIdValue = dataObj["user_id"];
                if (userIdValue != null)
                {
                    msg.UserId = userIdValue.Value<int>();
                } 

                // Validate we got everything.
                if (msg.UserId == 0 || msg.UserName == null || msg.Text == null || msg.ChannelId == 0)
                {
                    return;
                }

                // Fire off the message
                m_callback.OnChatMessage(msg);                
            }         
        }

        private void HandleAuthResponse(JObject jObject)
        {
            // {"type":"reply","error":null,"id":2116218693,"data":{"authenticated":true,"roles":["Mod","VerifiedPartner","Pro","User"]}}
            JToken dataObj = jObject["data"];
            if (dataObj != null)
            {
                JToken authValue = dataObj["authenticated"];
                if (authValue != null)
                {
                    if(!authValue.Value<bool>())
                    {
                        // The auth failed, disconnect the session.
                        var _ignored = Task.Run(() => UpdateStatus(ChatState.Disconnected, true)).ConfigureAwait(false);                       
                    }
                }
            }
        }

        private void HandleUserActivity(JObject jObject)
        {
            //            {
            //                {
            //                    "type": "event",
            //  "event": "UserJoin",
            //  "data": {
            //                        "originatingChannel": 160788,
            //    "username": "QuinnBot",
            //    "roles": [
            //      "VerifiedPartner",
            //      "Pro",
            //      "User"
            //    ],
            //    "id": 756604
            //  }
            //}}

            bool isJoin = jObject["event"].Value<string>().Equals("UserJoin");

            JToken dataObj = jObject["data"];
            if (dataObj != null)
            {
                JToken userIdValue = dataObj["id"];
                if (userIdValue == null)
                {
                    return;
                }
                JToken channelValue = dataObj["originatingChannel"];
                if (channelValue == null)
                {
                    return;
                }

                // Filter out any messages that aren't from this channel. This will happen when people co stream.
                // The message will be picked up by another socket.
                int channelId = channelValue.Value<int>();
                if (channelId != m_channelId)
                {
                    return;
                }

                m_callback.OnUserActivity(new UserActivity()
                {
                    ChannelId = channelValue.Value<int>(),
                    IsJoin = isJoin,
                    UserId = userIdValue.Value<int>(),
                });
            }
        }

        async Task<ChatServerDetails> GetChatServerDetails()
        {
            try
            {               
                string res = await MixerUtils.MakeMixerHttpRequest($"api/v1/chats/{m_channelId}");
                return JsonConvert.DeserializeObject<ChatServerDetails>(res);
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to get channel server info for channel: {m_channelId}", e);
            }
            return null;
        }
    }
}

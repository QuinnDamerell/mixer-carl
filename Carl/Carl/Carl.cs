using Carl.Dan;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Carl
{
    class ChannelEntry
    {
        public MixerChannel Channel;
        public ChatConnector Chat;
        public DateTime LastFoundTime;
    }

    public interface ICarl
    {
        // Called by the chat connectors when a message has been received.
        void OnChatMessage(ChatMessage msg);

        // Called by the chat connectors when a user has joined a channel.        
        void OnUserActivity(UserActivity activity);

        // Called by creeper carl when there's user activity.
        void OnAdvanceUserActivity(AdvanceUserActivity activity);

        // Called by the command Dan when there's a command to handle.
        void OnCommand(string command, ChatMessage msg);

        // Called by the firehose and chat connectors when they want to send a message.
        Task<bool> SendChatMessage(int channelId, string message);

        // Called by the firehose and chat connectors when they want to send a message.
        Task<bool> SendWhisper(int channelId, string targetUserName, string message);
    }

    class Carl : ICarl
    {
        public static CultureInfo Culture = new CultureInfo("en-US");

        int m_viewerCountLimit = 3;   // The number of viewers a channel must have (inclusive) to be picked up.
        int m_workerLimit = 5;
        int m_workMasterTimeMs = 2000;

        CreeperCarl m_creeperCarl;

        List<int> m_channelOverrides = null;//= new List<int>(){ 153416 };
        DateTime? m_lastChannelUpdateTime;

        int m_chatBotUserId;
        string m_chatBotoAuthToken;

        Thread m_masterWorker;

        public bool Run(string[] args)
        {
            CarlConfig config = CarlConfig.Get();
            if(config != null)
            {
                m_chatBotUserId = config.ChatBotUserId;
                m_chatBotoAuthToken = config.ChatBotOAuthToken;
                m_viewerCountLimit = config.ViewerCountLimit;
            }
            else
            {
                Logger.Info("Checking Args...");
                if(args.Length < 2)
                {
                    Logger.Info("Usage: carl <chat bot user Id> <chat bot oauth> (channel viewer count limit - default is 5)");
                    Logger.Info("User info can be found here: https://dev.mixer.com/tutorials/chatbot.html");
                    return false;
                }

                if(!int.TryParse(args[0], out m_chatBotUserId))
                {
                    Logger.Error("Failed to parse chat bot user id.");
                    return false;
                }
                m_chatBotoAuthToken = args[1];
                if(args.Length > 2)
                {
                    if (!int.TryParse(args[2], out m_viewerCountLimit))
                    {
                        Logger.Error("Failed to parse viewer count limit.");
                        return false;
                    }
                }
            }

            Logger.Info($"Starting! Viewer Count Limit:{m_viewerCountLimit}.");

            // Set the oauth token
            MixerUtils.SetMixerCreds(m_chatBotoAuthToken);

            Logger.Info("Setting up discovery");
            // Start the discovery process.
            ChannelDiscover dis = new ChannelDiscover(m_viewerCountLimit, m_channelOverrides);
            dis.OnChannelOnlineUpdate += OnChannelOnlineUpdate;
            dis.Run();

            Logger.Info("Setting up work master");
            // Setup the work master
            m_masterWorker = new Thread(WorkMasterThread);
            m_masterWorker.IsBackground = true;
            m_masterWorker.Start();

            Logger.Info("Setting up worker threads");
            // Setup the worker threads.
            int i = 0;
            while (i < m_workerLimit)
            {
                i++;
                Thread t = new Thread(WorkerThread);
                t.IsBackground = true;
                t.Start();
                m_workers.Add(t);
            }

            // Create our creeper.
            m_creeperCarl = new CreeperCarl(this);

            Logger.Info("Running!");
            return true;
        }

        #region Connection Management

        Dictionary<int, ChannelEntry> m_channelMap = new Dictionary<int, ChannelEntry>();
        Dictionary<int, bool> m_channelProcessQueue = new Dictionary<int, bool>();
        List<Thread> m_workers = new List<Thread>();

        private async void WorkerThread()
        {
            while (true)
            {
                // Get a channel id to work on.
                int channelId = -1;
                lock (m_channelProcessQueue)
                {
                    foreach (KeyValuePair<int, bool> entry in m_channelProcessQueue)
                    {
                        // Skip already processing channel ids.
                        if (entry.Value)
                        {
                            continue;
                        }

                        // Claim this channel id.
                        channelId = entry.Key;
                        m_channelProcessQueue[channelId] = true;
                        break;
                    }
                }

                if (channelId == -1)
                {
                    // No work to do, sleep.
                    Thread.Sleep(100);
                    continue;
                }

                // We have a channel id we should connect, try to do it.
                ChatConnector conn = new ChatConnector(m_chatBotUserId, channelId, this);

                bool added = false;
                if ((await conn.Connect()))
                {
                    // We succeeded, add the connection to the channel.
                    lock (m_channelMap)
                    {
                        if (m_channelMap.ContainsKey(channelId))
                        {
                            m_channelMap[channelId].Chat = conn;
                            conn.OnChatWsStateChanged += Chat_OnChatWsStateChanged;
                            added = true;
                        }
                    }
                }

                // If we failed to connect or didn't add the chat, disconnect.
                if (!added)
                {
                    await conn.Disconnect();
                }
                else
                {
                    OnChatConnectionChanged(channelId, ChatConnectionState.Connected);
                }

                // Remove the channel from the processing map.
                lock (m_channelProcessQueue)
                {
                    m_channelProcessQueue.Remove(channelId);
                }
            }
        }

        private void Chat_OnChatWsStateChanged(ChatConnector sender, ChatState newState, bool wasError)
        {
            // If the socket closed removed the channel.
            if (newState == ChatState.Disconnected)
            {
                int channelId = sender.GetChannelId();
                var _ignored = Task.Run(async () => 
                {
                    Logger.Error($"Chat ws disconnected due to ws error for channel {channelId}");
                    await RemoveChannel(channelId, "websocket error.");
                }).ConfigureAwait(false);
            }
        }

        private void WorkMasterThread()
        {
            while (true)
            {
                DateTime workStart = DateTime.Now;
                int connectedChannels = 0;
                int eligibleChannels = 0;
                List<int> toRemove = new List<int>();
                lock (m_channelMap)
                {
                    foreach (KeyValuePair<int, ChannelEntry> ent in m_channelMap)
                    {
                        if (m_lastChannelUpdateTime.HasValue && (m_lastChannelUpdateTime.Value - ent.Value.LastFoundTime).TotalSeconds > 180)
                        {
                            toRemove.Add(ent.Value.Channel.Id);
                        }
                        else
                        {
                            // If there isn't a chat setup, add it to the queue to get spun up.
                            eligibleChannels++;
                            if (ent.Value.Chat == null)
                            {
                                lock (m_channelProcessQueue)
                                {
                                    if (!m_channelProcessQueue.ContainsKey(ent.Value.Channel.Id))
                                    {
                                        m_channelProcessQueue.Add(ent.Value.Channel.Id, false);
                                    }
                                }
                            }
                            else
                            {
                                // If the chat gets stuck in a disconnected state, clear the chat object.
                                if (ent.Value.Chat.IsDisconnected())
                                {
                                    ent.Value.Chat = null;
                                }
                                else
                                {
                                    connectedChannels++;
                                }
                            }
                        }
                    }
                }

                Logger.Info($"{connectedChannels}/{eligibleChannels} ({(eligibleChannels == 0 ? 0 : Math.Round(((double)connectedChannels / (double)eligibleChannels)*100, 2))}%) connected channels; tracking {CreeperCarl.GetViewerCount().ToString("n0", Culture)} viewers. Work time was: {(DateTime.Now - workStart).TotalMilliseconds}ms");

                foreach (int id in toRemove)
                {
                    RemoveChannel(id, "the channel is offline or under the viewer limit.").ConfigureAwait(false);
                }

                DateTime sleepStart = DateTime.Now;
                Thread.Sleep(m_workMasterTimeMs);
                double time = (DateTime.Now - sleepStart).TotalMilliseconds;
                if (time > 2100)
                {
                    Logger.Info($"Master work thread slept for {time}ms");
                }
            }
        }

        private async Task RemoveChannel(int channelId, string reason)
        {
            ChannelEntry entry;
            lock (m_channelMap)
            {
                if (!m_channelMap.ContainsKey(channelId))
                {
                    return;
                }

                entry = m_channelMap[channelId];
                m_channelMap.Remove(channelId);
            }

            // Fire the remove event
            OnChatConnectionChanged(entry.Channel.Id, ChatConnectionState.Disconnected);

            if (entry.Chat != null)
            {
                Logger.Info($"Disconnecting channel {channelId} because {reason}");
                entry.Chat.OnChatWsStateChanged -= Chat_OnChatWsStateChanged;
                await entry.Chat.Disconnect();
            }
        }

        private void OnChannelOnlineUpdate(object sender, List<MixerChannel> e)
        {
            int channelsAdded = 0;
            DateTime now = DateTime.Now;
            lock (m_channelMap)
            {
                foreach (MixerChannel chan in e)
                {
                    // Filter by the viewer count limit.
                    if (chan.ViewersCurrent < m_viewerCountLimit || !chan.Online)
                    {
                        continue;
                    }
                    
                    if (m_channelMap.ContainsKey(chan.Id))
                    {
                        m_channelMap[chan.Id].LastFoundTime = now;
                        m_channelMap[chan.Id].Channel.ViewersCurrent = chan.ViewersCurrent;
                    }
                    else
                    {
                        channelsAdded++;
                        m_channelMap.Add(chan.Id, new ChannelEntry()
                        {
                            Channel = chan,
                            Chat = null,
                            LastFoundTime = now
                        });
                    }
                }
            }
            Logger.Info($"{channelsAdded} channels added to the map!");
            m_lastChannelUpdateTime = DateTime.Now;
        }

        private ChatConnector GetChatConnector(int channelId)
        {
            lock (m_channelMap)
            {
                if (m_channelMap.ContainsKey(channelId))
                {
                    return m_channelMap[channelId].Chat;
                }
            }
            return null;            
        }

        public async Task<bool> SendChatMessage(int channelId, string message)
        {
            ChatConnector connector = GetChatConnector(channelId);
            if(connector == null)
            {
                return false;
            }
            return await connector.SendChatMessage(message);
        }

        public async Task<bool> SendWhisper(int channelId, string targetUserName, string message)
        {
            ChatConnector connector = GetChatConnector(channelId);
            if (connector == null)
            {
                return false;
            }
            return await connector.SendWhisper(targetUserName, message);
        }

        #endregion

        #region Firehose Management

        List<Firehose> m_firehoses = new List<Firehose>();

        public bool SubFirehose(Firehose firehose)
        {
            lock(m_firehoses)
            {
                foreach(Firehose h in m_firehoses)
                {
                    if(h.GetUniqueId() == firehose.GetUniqueId())
                    {
                        return false;
                    }
                }
                m_firehoses.Add(firehose);
                return true;
            }
        }

        public bool UnSubFirehost(Firehose firehose)
        {
            lock (m_firehoses)
            {
                for(int i = 0; i < m_firehoses.Count; i++)
                {
                    if (m_firehoses[i].GetUniqueId() == firehose.GetUniqueId())
                    {
                        m_firehoses.RemoveAt(i);
                        return true;
                    }
                }
                return false;
            }
        }

        void ICarl.OnChatMessage(ChatMessage msg)
        {
            foreach(Firehose h in m_firehoses)
            {
                h.PubChatMessage(msg);
            }
        }

        void ICarl.OnUserActivity(UserActivity activity)
        {
            // Send user activity to creeper carl.
            // Carl will correctly fire the notifications and augment ones that
            // we miss from the websocket.
            m_creeperCarl.OnUserActivity(activity);     
        }

        void ICarl.OnAdvanceUserActivity(AdvanceUserActivity activity)
        {
            foreach (Firehose h in m_firehoses)
            {
                h.PubUserActivity(activity);
            }
        }

        void OnChatConnectionChanged(int channelId, ChatConnectionState state)
        {
            // Inform creeper carl when chat connections change.
            m_creeperCarl.OnChatConnectionChanged(channelId, state);

            // Now inform the firehoses.
            foreach (Firehose h in m_firehoses)
            {
                h.PublishChatConnectionChanged(channelId, state);
            }
        }

        public void OnCommand(string command, ChatMessage msg)
        {
            foreach (Firehose h in m_firehoses)
            {
                h.PubCommand(command, msg);
            }
        }

        #endregion
    }
}

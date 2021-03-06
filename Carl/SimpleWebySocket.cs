﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Carl
{
    public enum SimpleWebySocketState
    {
        NotConnected,
        Connecting,
        Connected,
        Disconnected
    }

    public interface ISimpleWebySocketCallbacks
    {
        void OnStateChanged(SimpleWebySocketState newState, bool consumerInvoked);

        void OnMessage(string message);
    }

    class SendQueueItem
    {
        public string Message;
        public SemaphoreSlim Mutex;
        public bool Sent = false;
    }

    public class SimpleWebySocket
    {
        // Allows you to limit how many message are sent per time period.
        public TimeSpan MinTimeBetweenSends = new TimeSpan(0,0,0,0,0);

        const int c_sendWaitTimeoutMs = 30000;       

        string m_url;
        ClientWebSocket m_ws;
        ISimpleWebySocketCallbacks m_callback;
        BlockingCollection<SendQueueItem> m_sendQueue = new BlockingCollection<SendQueueItem>();
        private CancellationTokenSource m_cancelToken = new CancellationTokenSource();

        Thread m_sendThread;
        Thread m_recvThread;

        public SimpleWebySocketState m_state;
        public SimpleWebySocketState State
        {
            get { return m_state; }
            private set { m_state = value; }
        }

        public SimpleWebySocket(ISimpleWebySocketCallbacks callback, string url)
        {
            m_callback = callback;
            m_url = url;
            m_ws = new ClientWebSocket();
        }

        private void UpdateState(SimpleWebySocketState newState, bool consumerInovked = false)
        {
            State = newState;
            m_callback.OnStateChanged(newState, false);
        }

        public async Task<bool> Connect()
        {
            // Check state
            if(State != SimpleWebySocketState.NotConnected)
            {
                return false;
            }
            UpdateState(SimpleWebySocketState.Connecting);

            // Setup
            m_ws.Options.KeepAliveInterval = new TimeSpan(0, 2, 0);

            // Connect
            try
            {
                await m_ws.ConnectAsync(new Uri(m_url), m_cancelToken.Token);

                if (m_ws.State != WebSocketState.Open)
                {
                    return false;
                }

                // Update the state.
                UpdateState(SimpleWebySocketState.Connected);

                // Start the worker threads
                m_sendThread = new Thread(SendThread);
                m_sendThread.IsBackground = true;
                m_sendThread.Start();
                m_recvThread = new Thread(ReceiveThread);
                m_recvThread.IsBackground = true;
                m_recvThread.Start();                
                return true;
            }
            catch (Exception e)
            {
                Logger.Error("Failed to connect to websocket.", e);
                await InternalDisconnect(WebSocketCloseStatus.NormalClosure);
                return false;
            }
        }

        public async Task Disconnect()
        {
            await InternalDisconnect(WebSocketCloseStatus.NormalClosure, "UserClosed", true);
        }

        public async Task<bool> Send(string message, bool blockUntilSent = false)
        {
            // Queue the request.
            SendQueueItem item = new SendQueueItem()
            {
                Message = message
            };

            if(blockUntilSent)
            {
                item.Mutex = new SemaphoreSlim(0);
            }

            m_sendQueue.Add(item);

            if (blockUntilSent)
            {
                // Wait on the item to be sent.
                await item.Mutex.WaitAsync(c_sendWaitTimeoutMs);
                return item.Sent;
            }

            return true;            
        }

        private async void SendThread()
        {
            while (State == SimpleWebySocketState.Connected)
            {
                // Wait on a message to send.
                SendQueueItem item = null;
                try
                {
                    item = m_sendQueue.Take(m_cancelToken.Token);
                }
                catch(Exception)
                {
                    // This throws when the cancel token if fired.
                    break;
                }

                // Grab the web socket locally for thread safety.
                ClientWebSocket ws = m_ws;
                if (ws == null)
                {
                    item.Mutex?.Release();
                    break;
                }

                if(m_sendQueue.Count > 50)
                {
                    Logger.Error($"Web socket send queue is looonnnggg {m_sendQueue.Count}.");
                }

                DateTime sendStart = DateTime.Now;

                // Send the message.
                try
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(item.Message);
                    await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, m_cancelToken.Token);
                }
                catch (Exception)
                {
                    // If we failed to send, kill the connection.
                    await InternalDisconnect(WebSocketCloseStatus.NormalClosure);
                    item.Mutex?.Release();
                    break;
                }

                // Indicate the message was sent.
                item.Sent = true;
                item.Mutex?.Release();

                double sleepyTimeMs = MinTimeBetweenSends.TotalMilliseconds - (DateTime.Now - sendStart).TotalMilliseconds;
                if (sleepyTimeMs > 0)
                {
                    Thread.Sleep((int)sleepyTimeMs);
                }
            }
        }

        private async void ReceiveThread()
        {
            var buffer = new byte[2024];
            string message = String.Empty;

            while (State == SimpleWebySocketState.Connected)
            {
                // Grab the web socket locally for thread safety.
                ClientWebSocket ws = m_ws;
                if(ws == null)
                {
                    break;
                }

                WebSocketReceiveResult result = null;
                // Wait on data from the socket.
                DateTime start = DateTime.Now;
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), m_cancelToken.Token);
                }
                catch(TaskCanceledException)
                {
                    // This happens when the socket is shutting down, so don't log or worry about it.
                    await InternalDisconnect(WebSocketCloseStatus.NormalClosure);
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Websocket received an error while reading the websocket: {(DateTime.Now - start).TotalSeconds}s", ex);
                    await InternalDisconnect(WebSocketCloseStatus.ProtocolError);
                    break;
                }

                // Continue if we didn't get anything
                if(result == null)
                {
                    continue;
                }

                // Handle the message
                if(result.MessageType == WebSocketMessageType.Close)
                {
                    // Disconnect
                    await InternalDisconnect(WebSocketCloseStatus.NormalClosure, "SERVER REQUESTED CLOSE");
                } 
                else if(result.MessageType == WebSocketMessageType.Text)
                {
                    try
                    {
                        // Add the message to the current buffer.
                        message += Encoding.UTF8.GetString(buffer, 0, result.Count);

                        // Keep going if we need more data.
                        if (!result.EndOfMessage)
                        {
                            continue;
                        }

                        // Respond to pings.
                        if (message.Trim() == "ping")
                        {
                            Send("pong");
                        }
                        else
                        {
                            // Send the message to the client
                            m_callback.OnMessage(message);
                        }
                    }
                    catch(Exception e)
                    {
                        Logger.Error("Exception thrown while handing websocket message", e);
                    }

                    // Now that we have a full message, clear the buffer.
                    message = String.Empty;
                }
            }
        }

        private async Task InternalDisconnect(WebSocketCloseStatus status, string reason = "UNKNOWN", bool isUserInvoked = false)
        {
            // Only do this once.
            if(State == SimpleWebySocketState.Disconnected)
            {
                return;
            }

            // First of set the new state.
            UpdateState(SimpleWebySocketState.Disconnected, isUserInvoked);

            // Now try to close the web socket.
            ClientWebSocket ws = m_ws;
            if(ws != null)
            {
                try
                {
                    await ws.CloseAsync(status, reason, m_cancelToken.Token);                    
                }
                catch (Exception) { }

                try
                {
                    ws.Abort();
                }
                catch (Exception) { }
            }

            // Cancel anything that's not dead.
            m_cancelToken.Cancel();
        }
    }
}

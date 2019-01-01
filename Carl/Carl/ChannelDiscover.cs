using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace Carl
{
    public class MixerChannel
    {
        public int Id;
        public int ViewersCurrent;
        public string Token;
        public bool Online;
    }

    class ChannelDiscover
    {
        public delegate void ChannelOnlineUpdate(object sender, List<MixerChannel> e);
        public event ChannelOnlineUpdate OnChannelOnlineUpdate;

        const int m_threadSleepTimeMs = 15000;
        int m_viwersInclusiveLimit;
        List<int> m_channelOverrides;
        Thread m_updater;

        public ChannelDiscover(int viewerInclusiveLimit, List<int> channelOverrides = null)
        {
            m_channelOverrides = channelOverrides;
            m_viwersInclusiveLimit = viewerInclusiveLimit;
        }

        public void Run()
        {
            m_updater = new Thread(UpdateThread);
            m_updater.IsBackground = true;
            m_updater.Start();
        }

        private async void UpdateThread()
        {
            while(true)
            {
                try
                {
                    DateTime start = DateTime.Now;
                    List<MixerChannel> channels = await GetOnlineChannels();
                    if (channels.Count != 0 && OnChannelOnlineUpdate != null)
                    {
                        OnChannelOnlineUpdate(this, channels);
                        Logger.Info($"Channel Update found {channels.Count} online channels with > 1 viewers in {(DateTime.Now - start).TotalSeconds}s");
                    }
                }
                catch(Exception e)
                {
                    Logger.Error("Error during channel discovery update.", e);
                }

                Thread.Sleep(m_threadSleepTimeMs);
            }
        }

        private async Task<List<MixerChannel>> GetOnlineChannels()
        {
            List<MixerChannel> channels = new List<MixerChannel>();

            // Add the debug overrides if needed.
            if(m_channelOverrides != null && m_channelOverrides.Count > 0)
            {
                foreach(int chanId in m_channelOverrides)
                {
                    channels.Add(new MixerChannel()
                    {
                        Id = chanId,
                        Token = await MixerUtils.GetChannelName(chanId),
                        ViewersCurrent = 300
                    });
                }
                return channels;
            }

            // Always add my channel for debugging.
            channels.Add(new MixerChannel()
            {
                Id = 153416,
                Token = await MixerUtils.GetChannelName(153416),
                ViewersCurrent = 300,
                Online = true,
            });

            int i = 0;
            while (i < 1000)
            {
                try
                {
                    string response = await MixerUtils.MakeMixerHttpRequest($"api/v1/channels?limit=100&page={i}&order=online:desc,viewersCurrent:desc&fields=token,id,viewersCurrent,online");
                    List<MixerChannel> chan = JsonConvert.DeserializeObject<List<MixerChannel>>(response);
                    channels.AddRange(chan);

                    // Check if we hit the end.
                    if(chan.Count == 0)
                    {
                        break;
                    }

                    // Check if we are on channels that are under our viewer limit
                    if(chan[0].ViewersCurrent < m_viwersInclusiveLimit)
                    {
                        break;
                    }

                    // Check if we hit the end of online channels.
                    if (!chan[0].Online)
                    {
                        break;
                    }

                    // Sleep a little so we don't hit the API too hard.
                    await Task.Delay(10);
                }
                catch (Exception e)
                {
                    Logger.Error($"Failed to query channel API.", e);
                    break;
                }
                i++;
            }
            return channels;
        }
    }
}

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Carl
{
    public class MixerChannel
    {
        public int Id;
        public int ViewersCurrent;
        public string Token;
    }

    class ChannelDiscover
    {
        public delegate void ChannelOnlineUpdate(object sender, List<MixerChannel> e);
        public event ChannelOnlineUpdate OnChannelOnlineUpdate;

        const int m_threadSleepTimeMs = 15000;
        HttpClient m_client = new HttpClient();
        List<int> m_channelOverrides;

        public ChannelDiscover(List<int> channelOverrides = null)
        {
            m_channelOverrides = channelOverrides;
            m_client.DefaultRequestHeaders.Add("Client-ID", "Karl");
        }

        public void Run()
        {
            UpdateThread();
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

                await Task.Delay(m_threadSleepTimeMs);
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
                ViewersCurrent = 300
            });            

            int rateLimitBackoff = 0;
            int i = 0;
            while (i < 1000)
            {
                HttpRequestMessage request = new HttpRequestMessage();
                request.RequestUri = new Uri($"https://mixer.com/api/v1/channels?limit=100&page={i}&order=online:desc,viewersCurrent:desc&fields=token,id,viewersCurrent");
                string res;
                try
                {
                    HttpResponseMessage response = await m_client.SendAsync(request);
                    if (response.StatusCode == (HttpStatusCode)429)
                    {
                        // If we get rate limited wait for a while.
                        rateLimitBackoff++;
                        await Task.Delay(200 * rateLimitBackoff);

                        // And try again.
                        continue;
                    }
                    rateLimitBackoff = 0;

                    res = await response.Content.ReadAsStringAsync();
                    List<MixerChannel> chan = JsonConvert.DeserializeObject<List<MixerChannel>>(res);
                    channels.AddRange(chan);

                    // If we hit the end of the list of channels with viewers, return.
                    if(chan.Count != 0 && chan[0].ViewersCurrent == 0)
                    {
                        break;
                    }
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

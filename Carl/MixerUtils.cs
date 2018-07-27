using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Carl
{
    public class MixerUtils
    {
        static HttpClient s_client = new HttpClient();
        static ConcurrentDictionary<int, string> s_channelNames = new ConcurrentDictionary<int, string>();

        class MixerChannelApi
        {
            public string token;
            public int userId;
        }

        class MixerUserApi
        {
            public string token;
        }

        public static void Init()
        {
            s_client.DefaultRequestHeaders.Add("Client-ID", "Karl");
        }

        public static void SetMixerCreds(string oauthToken)
        {
            s_client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken);
        }

        public async static Task<string> GetChannelName(int channelId)
        {
            // Look for it in the cache
            string channelName = null;
            if(s_channelNames.TryGetValue(channelId, out channelName))
            {
                return channelName;
            }

            // Try to get it
            try
            {
                string res = await MakeMixerHttpRequest($"api/v1/channels/{channelId}");
                channelName = JsonConvert.DeserializeObject<MixerChannelApi>(res).token;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to get channel name from API: {channelId}", e);
                return null;
            }

            // Try to set it in the cache
            s_channelNames.TryAdd(channelId, channelName);
            return channelName;
        }

        public static void AddChannelMap(int channelId, string channelName)
        {
            s_channelNames.TryAdd(channelId, channelName);
        }

        public async static Task<int?> GetUserId(string userName)
        {
            try
            {
                string res = await MakeMixerHttpRequest($"api/v1/channels/{userName}");
                return JsonConvert.DeserializeObject<MixerChannelApi>(res).userId;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to get user name from API: {userName}", e);
                return null;
            }
        }

        public async static Task<string> MakeMixerHttpRequest(string url)
        {
            int rateLimitBackoff = 0;
            int i = 0;
            while (i < 1000)
            {
                HttpRequestMessage request = new HttpRequestMessage();
                request.RequestUri = new Uri($"https://mixer.com/{url}");

                HttpResponseMessage response = await s_client.SendAsync(request);
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    // If we get rate limited wait for a while.
                    rateLimitBackoff++;
                    await Task.Delay(50 * rateLimitBackoff);

                    if(rateLimitBackoff > 2)
                    {
                        Logger.Error($"Mixer rate limit is at {rateLimitBackoff}");
                    }

                    // And try again.
                    continue;
                }
                else if(response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"Mixer backend returned status code {response.StatusCode}");
                }
                return await response.Content.ReadAsStringAsync();              
            }
            return String.Empty;
        }
    }
}

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

        public async static Task<string> GetChannelName(int channelId)
        {
            // Look for it in the cache
            string channelName = null;
            if(s_channelNames.TryGetValue(channelId, out channelName))
            {
                return channelName;
            }

            // Try ot get it   
            HttpRequestMessage request = new HttpRequestMessage();
            request.RequestUri = new Uri($"https://mixer.com/api/v1/channels/{channelId}");
            try
            {
                HttpResponseMessage response = await s_client.SendAsync(request);
                string res = await response.Content.ReadAsStringAsync();
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
            // Try ot get it   
            HttpRequestMessage request = new HttpRequestMessage();
            request.RequestUri = new Uri($"https://mixer.com/api/v1/channels/{userName}");
            try
            {
                HttpResponseMessage response = await s_client.SendAsync(request);
                string res = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<MixerChannelApi>(res).userId;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to get user name from API: {userName}", e);
                return null;
            }
        }
    }
}

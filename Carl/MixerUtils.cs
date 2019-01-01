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
        static string m_userOAuthToken;
        static HttpClient s_client = new HttpClient();
        static ConcurrentDictionary<int, UserInfo> s_channelIds = new ConcurrentDictionary<int, UserInfo>();
        static ConcurrentDictionary<int, UserInfo> s_userIds = new ConcurrentDictionary<int, UserInfo>();
        static ConcurrentDictionary<string, UserInfo> s_userNames = new ConcurrentDictionary<string, UserInfo>();

        class UserInfo
        {
            public string Name;
            public int UserId = 0;
            public int ChannelId = 0;
        }

        class MixerChannelApi
        {
            public string token;
            public int userId;
            public int id;
        }

        class MixerUserApi
        {
            public MixerChannelApi channel;
        }

        public static void Init()
        {
            s_client.DefaultRequestHeaders.Add("Client-ID", "Karl");
        }

        public static void SetMixerCreds(string oauthToken)
        {
            m_userOAuthToken = oauthToken;
        }

        public async static Task<string> GetChannelName(int channelId)
        {
            // Look for it in the cache
            UserInfo userObj = null;
            if(s_channelIds.TryGetValue(channelId, out userObj))
            { 
                return userObj.Name;                
            }

            // Try to get it
            userObj = new UserInfo();
            try
            {
                string res = await MakeMixerHttpRequest($"api/v1/channels/{channelId}");
                var api = JsonConvert.DeserializeObject<MixerChannelApi>(res);
                userObj.ChannelId = api.id;
                userObj.Name = api.token;
                userObj.UserId = api.userId;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to get channel name from API: {channelId}", e);
                return null;
            }

            if(userObj.ChannelId == 0 || userObj.UserId == 0 || String.IsNullOrWhiteSpace(userObj.Name))
            {
                return null;
            }

            AddToMaps(userObj);

            return userObj.Name;
        }

        public async static Task<string> GetProperUserName(string userName)
        {
            // Look for it in the cache
            UserInfo userObj = null;
            if (s_userNames.TryGetValue(userName.ToLower(), out userObj))
            {
                return userObj.Name;
            }

            // Try to get it
            userObj = new UserInfo();
            try
            {
                string res = await MakeMixerHttpRequest($"api/v1/channels/{userName}");
                var api = JsonConvert.DeserializeObject<MixerChannelApi>(res);
                userObj.ChannelId = api.id;
                userObj.Name = api.token;
                userObj.UserId = api.userId;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to get channel name from API: {userName}", e);
                return null;
            }

            if (userObj.ChannelId == 0 || userObj.UserId == 0 || String.IsNullOrWhiteSpace(userObj.Name))
            {
                return null;
            }

            AddToMaps(userObj);

            return userObj.Name;
        }

        public async static Task<int?> GetUserId(string userName)
        {
            // Look for it in the cache
            UserInfo userObj = null;
            if (s_userNames.TryGetValue(userName.ToLower(), out userObj))
            {
                return userObj.UserId;
            }

            // Try to get it
            userObj = new UserInfo();
            try
            {
                string res = await MakeMixerHttpRequest($"api/v1/channels/{userName}");
                var api = JsonConvert.DeserializeObject<MixerChannelApi>(res);
                userObj.ChannelId = api.id;
                userObj.Name = api.token;
                userObj.UserId = api.userId;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to get user id name from API: {userName}", e);
                return null;
            }

            if (userObj.ChannelId == 0 || userObj.UserId == 0 || String.IsNullOrWhiteSpace(userObj.Name))
            {
                return null;
            }

            AddToMaps(userObj);

            return userObj.UserId;
        }

        public async static Task<string> GetUserName(int userId)
        {
            // Look for it in the cache
            UserInfo userObj = null;
            if (s_userIds.TryGetValue(userId, out userObj))
            {
                return userObj.Name;
            }

            // Try to get it
            userObj = new UserInfo();
            try
            {
                string res = await MakeMixerHttpRequest($"api/v1/users/{userId}");
                var api = JsonConvert.DeserializeObject<MixerUserApi>(res);
                userObj.ChannelId = api.channel.id;
                userObj.Name = api.channel.token;
                userObj.UserId = api.channel.userId;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to get user name from API: {userId}", e);
                return null;
            }

            if (userObj.ChannelId == 0 || userObj.UserId == 0 || String.IsNullOrWhiteSpace(userObj.Name))
            {
                return null;
            }

            AddToMaps(userObj);

            return userObj.Name;
        }

        private static void AddToMaps(UserInfo info)
        {
            if (info == null || info.ChannelId == 0 || info.UserId == 0 || String.IsNullOrWhiteSpace(info.Name))
            {
                return;
            }
            s_channelIds.TryAdd(info.ChannelId, info);
            s_userIds.TryAdd(info.UserId, info);
            s_userNames.TryAdd(info.Name.ToLower(), info);
        }

        public async static Task<string> MakeMixerHttpRequest(string url, bool useCreds = true)
        {
            int rateLimitBackoff = 0;
            int i = 0;
            while (i < 1000)
            {
                HttpRequestMessage request = new HttpRequestMessage();
                request.RequestUri = new Uri($"https://mixer.com/{url}");

                if(useCreds)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", m_userOAuthToken);
                }

                HttpResponseMessage response = await s_client.SendAsync(request);
                if (response.StatusCode == (HttpStatusCode)429)
                {
                    // If we get rate limited wait for a while.
                    Logger.Info("backoff "+url);
                    rateLimitBackoff++;
                    await Task.Delay(100 * rateLimitBackoff);

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

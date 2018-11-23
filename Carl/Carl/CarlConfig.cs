using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Carl
{
    class CarlConfig
    {
        public int ChatBotUserId;
        public string ChatBotOAuthToken;
        public int ViewerCountLimit;

        public static CarlConfig Get()
        {
            try
            {
                return JsonConvert.DeserializeObject<CarlConfig>(File.ReadAllText(@"CarlConfig.json"));
            }
            catch(Exception e)
            {
                Console.WriteLine($"Failed to read config. {e.Message}");
                return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Carl;
using Carl.Dan;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Carl
{
    public class Program
    {
        public static void Main(string[] args)
        {
            MixerUtils.Init();

            Carl c = new Carl();
            if(!c.Run(args))
            {
                return;
            }

            // Setup the commander dan.
            Firehose hose = new Firehose(c);
            c.SubFirehose(hose);
            CommandDan dan = new CommandDan(c, hose);

            // Setup message Dan
            hose = new Firehose(c);
            c.SubFirehose(hose);
            MessagesDan messagesDan = new MessagesDan(hose);

            // Notification Dan
            hose = new Firehose(c);
            c.SubFirehose(hose);
            FriendlyDan friendlyDan = new FriendlyDan(hose);

            while (true)
            {
                Thread.Sleep(500000);
            }

            //var host = new WebHostBuilder()
            //    .UseKestrel()
            //    .UseContentRoot(Directory.GetCurrentDirectory())
            //    .UseIISIntegration()
            //    .UseStartup<Startup>()
            //    .Build();
            //host.Run();
        }
    }
}

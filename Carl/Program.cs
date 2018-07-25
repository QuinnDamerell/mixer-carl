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
            Carl c = new Carl();
            if(!c.Run(args))
            {
                return;
            }

            // Setup the Dan Summoner
            Firehose hose = new Firehose(c);
            c.SubFirehose(hose);
            CommandDan dan = new CommandDan(hose);

            // Set up the Creeper Dan
            hose = new Firehose(c);
            c.SubFirehose(hose);
            CreeperDan creeperDan = new CreeperDan(hose);

            while(true)
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

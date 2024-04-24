global using System;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Coflnet.Sky.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            using var sigin = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
            {
                Console.WriteLine("SIGINT received!");
                Environment.Exit(0);
            });
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                Console.WriteLine("SIGTERM received!");
                Environment.Exit(0);
            });
            using var sigquit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, context =>
            {
                Console.WriteLine("SIGQUIT received!");
                Environment.Exit(0);
            });
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}

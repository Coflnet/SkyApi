global using System;
using System.Runtime.InteropServices;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coflnet.Sky.Api
{
    public class Program
    {
        public static async System.Threading.Tasks.Task Main(string[] args)
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
            
            var host = CreateHostBuilder(args).Build();
            
            // Seed rate limit policies from configuration
            using (var scope = host.Services.CreateScope())
            {
                // Seed IP rate limit policies
                var ipPolicyStore = scope.ServiceProvider.GetRequiredService<IIpPolicyStore>();
                await ipPolicyStore.SeedAsync();
                
                // Seed Client rate limit policies (for premium clients)
                var clientPolicyStore = scope.ServiceProvider.GetRequiredService<IClientPolicyStore>();
                await clientPolicyStore.SeedAsync();
            }
            
            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}

using System;
using System.IO;
using System.Reflection;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Prometheus;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using StackExchange.Redis;
using AspNetCoreRateLimit;
using AspNetCoreRateLimit.Redis;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Filter;
using AutoMapper;
using System.Net.Http;
using Coflnet.Sky.Bazaar.Client.Api;
using System.Linq;
using Coflnet.Sky.ModCommands.Client.Api;
using Coflnet.Sky.ModCommands.Client.Extensions;

namespace Coflnet.Sky.Api
{
    /// <summary>
    /// The asp.net core startup code
    /// </summary>
    public class Startup
    {
        /// <summary>
        /// Creates a new instance of <see cref="Startup"/>
        /// </summary>
        /// <param name="configuration"></param>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        private IConfiguration Configuration { get; }
        private static string CORS_PLICY_NAME = "defaultCorsPolicy";


        /// <summary>
        /// This method gets called by the runtime. Use this method to add services to the container.
        /// </summary>
        /// <param name="services"></param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers().AddNewtonsoftJson();
            services.AddSwaggerGenNewtonsoftSupport();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "SkyApi",
                    Version = "v1",
                    Description = "Notes: PET, RUNE and POTION item tags (somtimes called ids) are expanded to include the type, eg PET_LION.<br>"
                                + " All other Tags match with from hypixel and can be found via the search endpoint."
                });
                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

                c.SchemaFilter<RequireNonNullablePropertiesSchemaFilter>();
                c.IncludeXmlComments(xmlPath, true);
                // c.CustomSchemaIds(t => t.FullName[12..].Replace("Models.","").Replace("Model.","").Replace("Client.",""));
            });
            services.AddAutoMapper(typeof(OrganizationProfile));
            services.AddCors(o =>
            {
                o.AddPolicy(CORS_PLICY_NAME, p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
            });

            services.AddJaeger(Configuration, 0.001, 60);
            services.AddScoped<PricesService>();
            services.AddSingleton<GoogletokenService>();
            services.AddSingleton<AuctionService>();
            services.AddDbContext<HypixelContext>();
            services.AddTransient<KatService>();
            services.AddSingleton<PremiumTierService>();
            services.AddSingleton<Core.Services.HypixelItemService>();
            services.AddSingleton<Core.Services.IHypixelItemStore>(di => di.GetRequiredService<Core.Services.HypixelItemService>());
            services.AddSingleton<Core.Services.ExoticColorService>();
            services.AddSingleton<HttpClient>();
            services.AddSingleton<NetworthService>();

            services.AddSingleton<ItemSkinHandler>();
            services.AddHostedService<ItemSkinHandler>(di => di.GetService<ItemSkinHandler>());
            services.AddResponseCaching();
            services.AddResponseCompression();
            var redisOptions = ConfigurationOptions.Parse(Configuration["REDIS_HOST"]);
            if (redisOptions.SyncTimeout < 2000)
                redisOptions.SyncTimeout = 2000;
            redisOptions.AbortOnConnectFail = false;

            services.AddStackExchangeRedisCache(options =>
            {
                options.ConfigurationOptions = redisOptions;
                options.InstanceName = "skyapi";
            });
            services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect(redisOptions));

            // Rate limiting 
            services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));
            services.Configure<IpRateLimitPolicies>(Configuration.GetSection("IpRateLimitPolicies"));
            services.AddRedisRateLimiting();
            services.AddSingleton<IIpPolicyStore, DistributedCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, DistributedCacheRateLimitCounterStore>();
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
            services.AddSingleton<DiscordBot.Client.Api.IMessageApi>(new DiscordBot.Client.Api.MessageApi(Configuration["DISCORD_BOT_BASE_URL"]));
            services.AddCoflService();
            services.AddSingleton<Mayor.Client.Api.IElectionPeriodsApiApi>(a =>
            {
                return new Mayor.Client.Api.ElectionPeriodsApiApi(Configuration["MAYOR_BASE_URL"]);
            });
            services.AddSingleton<Proxy.Client.Api.IBaseApi>(a =>
            {
                return new Proxy.Client.Api.BaseApi(Configuration["PROXY_BASE_URL"]);
            });
            services.AddSingleton<Subscriptions.Client.Api.ISubscriptionApi>(a =>
            {
                return new Subscriptions.Client.Api.SubscriptionApi(Configuration["SUBSCRIPTION_BASE_URL"]);
            });
            services.AddApi(options => options.AddApiHttpClients(c =>
                {
                    c.BaseAddress = new Uri(Configuration["MOD_BASE_URL"]);
                }));

            services.AddSingleton<Trade.Client.Api.ITradeApi>(p =>
            {
                return new Trade.Client.Api.TradeApi(Configuration["TRADE_BASE_URL"]);
            });
            services.AddSingleton<AhListChecker>();

            services.AddSingleton<TfmService>();
            services.AddSingleton<ModDescriptionService>();
            services.AddSingleton<AuctionConverter>();
            services.AddSingleton<Auctions.Client.Api.IExportApi>(p => new Auctions.Client.Api.ExportApi(Configuration["AUCTIONS_BASE_URL"]));
            services.AddSingleton<MappingCenter>(di =>
            {

                return new MappingCenter(di.GetRequiredService<Core.Services.HypixelItemService>(), async (id) =>
                {
                    if (id == "SKYBLOCK_COIN")
                        return new Dictionary<DateTime, long>() { { DateTime.UtcNow.Date.AddDays(-400), 1 } };
                    var bazaarItems = di.GetRequiredService<ModDescriptionService>().DeserializedCache.BazaarItems;
                    if (bazaarItems.ContainsKey(id))
                    {
                        var history = await di.GetRequiredService<IBazaarApi>().GetHistoryGraphAsync(id); ;
                        return history.GroupBy(a => a.Timestamp.Date).Select(s => s.First()).ToDictionary(h => h.Timestamp.Date, h => (long)h.Sell);
                    }
                    using var dbcontext = new HypixelContext();
                    var itemDetails = di.GetRequiredService<ItemDetails>();
                    var dbId = itemDetails.GetItemIdForTag(id, false);
                    if (dbId == 0)
                    {
                        Console.WriteLine("Item not found in db " + id);
                        return new Dictionary<DateTime, long>();
                    }
                    var all = dbcontext.Prices.Where(p => p.ItemId == dbId).ToList();
                    return all.GroupBy(a => a.Date.Date).Select(s => s.First()).ToDictionary(a => a.Date.Date, a => (long)a.Avg);
                });
            });
        }

        /// <summary>
        /// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        /// </summary>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger, IMapper mapper)
        {
            app.UseExceptionHandler(errorApp =>
            {
                ErrorHandler.Add(logger, errorApp, "api");
            });
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                mapper.ConfigurationProvider.AssertConfigurationIsValid();
            }
            app.UseSwagger(a =>
            {
                a.RouteTemplate = "api/swagger/{documentName}/swagger.json";
            });
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/api/swagger/v1/swagger.json", "SkyApi v1");
                c.RoutePrefix = "api";
            });

            app.UseRouting();

            app.UseCors(CORS_PLICY_NAME);

            app.UseAuthorization();

            app.UseResponseCaching();
            app.UseResponseCompression();
            app.UseIpRateLimiting();
          /*  app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post)
                {
                    context.Request.EnableBuffering();
                    context.Request.Body.Position = 0;
                    using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
                    {
                        var body = await reader.ReadToEndAsync();
                        context.Request.Body.Position = 0;
                        logger.LogInformation($"POST Body: {body}");
                    }
                }
                await next();
            });*/

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}

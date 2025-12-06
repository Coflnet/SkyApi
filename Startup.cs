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
using Microsoft.Extensions.Options;
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
using Mscc.GenerativeAI;
using Coflnet.Sky.Api.Helper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.Tasks;

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
            
            // Add Custom Authentication that integrates with existing Google token system
            services.AddAuthentication("CustomScheme")
                .AddScheme<CustomAuthenticationSchemeOptions, CustomAuthenticationHandler>("CustomScheme", options => { });

            services.AddAuthorization();
            
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "SkyApi",
                    Version = "v1",
                    Description = "Notes: PET, RUNE and POTION item tags (somtimes called ids) are expanded to include the type, eg PET_LION.<br>"
                                + " All other Tags match with hypixel and can be found via the search endpoint, also see our <a href=\"https://sky.coflnet.com/wiki/api\">api docs</a>.<br>"
                                + "Most of these endpoints are used for our <a href=\"https://sky.coflnet.com/flips\">Hypixel skyblock ah and bazaar flipping service</a> and may need you to have a premium account token<br>"
                                + "Also if you use the api for a public project you are responsible for complying with our <a href=\"https://sky.coflnet.com/wiki/api#attribution\">api attribution</a>."
                });
                
                // Add JWT Authorization to Swagger
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });
                
                // Add Google Token Authorization to Swagger (for existing endpoints)
                c.AddSecurityDefinition("GoogleToken", new OpenApiSecurityScheme
                {
                    Description = "Google Token Authorization header. Example: \"GoogleToken: {token}\"",
                    Name = "GoogleToken",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey
                });
                
                // Apply authorization only to endpoints with [Authorize] attribute
                c.OperationFilter<AuthorizeCheckOperationFilter>();
                
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
                o.AddPolicy(CORS_PLICY_NAME, p => p
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .SetIsOriginAllowed(_ => true)
                    .AllowCredentials());
            });

            services.AddJaeger(Configuration, 0.001, 60);
            services.AddScoped<PricesService>();
            services.AddSingleton<GoogletokenService>();
            services.AddSingleton<ApiKeyService>();
            services.AddSingleton<AuctionService>();
            services.AddDbContext<HypixelContext>();
            services.AddTransient<KatService>();
            services.AddSingleton<PremiumTierService>();
            services.AddSingleton<Core.Services.HypixelItemService>();
            services.AddSingleton<Core.Services.IHypixelItemStore>(di => di.GetRequiredService<Core.Services.HypixelItemService>());
            services.AddSingleton<GoogleAI>(di=> new GoogleAI(Configuration["GOOGLE_GEMINI_API_KEY"]));
            services.AddSingleton<Core.Services.ExoticColorService>();
            services.AddSingleton<HttpClient>();
            services.AddSingleton<FilterPobularityService>();
            services.AddSingleton<NetworthService>();
            services.AddScoped<AiRateLimitFilter>();

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
            // IP Rate Limiting (for public/anonymous requests)
            services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));
            services.Configure<IpRateLimitPolicies>(Configuration.GetSection("IpRateLimitPolicies"));
            
            // Client Rate Limiting (for authenticated/premium clients with higher quotas)
            // Premium client IDs can be configured via PREMIUM_CLIENT_IDS environment variable (comma-separated)
            // Example: PREMIUM_CLIENT_IDS=client-id-1,client-id-2,client-id-3
            services.Configure<ClientRateLimitOptions>(options =>
            {
                Configuration.GetSection("ClientRateLimiting").Bind(options);
                // Ensure ClientWhitelist is initialized
                options.ClientWhitelist ??= new System.Collections.Generic.List<string>();
                // Add premium client IDs from environment variable to whitelist
                var premiumClientIds = Configuration["PREMIUM_CLIENT_IDS"];
                if (!string.IsNullOrEmpty(premiumClientIds))
                {
                    var clientIds = premiumClientIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var clientId in clientIds)
                    {
                        if (!options.ClientWhitelist.Contains(clientId))
                        {
                            options.ClientWhitelist.Add(clientId);
                        }
                    }
                }
                // Ensure IP whitelist bypass token is present so resolver can return it.
                // Read token from env/config if provided, otherwise use default.
                var bypassToken = Configuration["IP_WHITELIST_BYPASS_TOKEN"];
                if (string.IsNullOrEmpty(bypassToken)) bypassToken = "IP_WHITELIST_BYPASS";
                if (!options.ClientWhitelist.Contains(bypassToken))
                {
                    options.ClientWhitelist.Add(bypassToken);
                }
            });
            services.Configure<ClientRateLimitPolicies>(options =>
            {
                Configuration.GetSection("ClientRateLimitPolicies").Bind(options);
                // Ensure ClientRules is initialized
                options.ClientRules ??= new System.Collections.Generic.List<ClientRateLimitPolicy>();
                // Configure premium client rules from environment variable
                // PREMIUM_CLIENT_RULES format: clientId1:period1=limit1,period2=limit2;clientId2:period1=limit1
                // Example: PREMIUM_CLIENT_RULES=premium-client:1s=20,1m=500;vip-client:1s=50,1m=1000
                var premiumRulesConfig = Configuration["PREMIUM_CLIENT_RULES"];
                if (!string.IsNullOrEmpty(premiumRulesConfig))
                {
                    var clientConfigs = premiumRulesConfig.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var clientConfig in clientConfigs)
                    {
                        var parts = clientConfig.Split(':', 2);
                        if (parts.Length != 2) continue;
                        
                        var clientId = parts[0].Trim();
                        var rulesStr = parts[1].Trim();
                        var rules = new System.Collections.Generic.List<RateLimitRule>();
                        
                        foreach (var ruleStr in rulesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            var ruleParts = ruleStr.Split('=');
                            if (ruleParts.Length == 2 && int.TryParse(ruleParts[1], out var limit))
                            {
                                rules.Add(new RateLimitRule
                                {
                                    Endpoint = "*",
                                    Period = ruleParts[0].Trim(),
                                    Limit = limit
                                });
                            }
                        }
                        
                        if (rules.Count > 0)
                        {
                            options.ClientRules.Add(new ClientRateLimitPolicy
                            {
                                ClientId = clientId,
                                Rules = rules
                            });
                        }
                    }
                }
            });
            
            services.AddRedisRateLimiting();
            services.AddHttpContextAccessor();
            services.AddSingleton<IIpPolicyStore, DistributedCacheIpPolicyStore>();
            services.AddSingleton<IClientPolicyStore, DistributedCacheClientPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, DistributedCacheRateLimitCounterStore>();
            // Use custom rate limit configuration that falls back to IP when no client ID is provided
            // Pass the configured bypass token (from env) into the configuration instance
            var bypassTokenForRegistration = Configuration["IP_WHITELIST_BYPASS_TOKEN"];
            if (string.IsNullOrEmpty(bypassTokenForRegistration)) bypassTokenForRegistration = "IP_WHITELIST_BYPASS";
            services.AddSingleton<IRateLimitConfiguration>(sp => new Helper.CustomRateLimitConfiguration(
                sp.GetRequiredService<IHttpContextAccessor>(),
                sp.GetRequiredService<IOptions<IpRateLimitOptions>>(),
                sp.GetRequiredService<IOptions<ClientRateLimitOptions>>(),
                bypassTokenForRegistration));
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

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseResponseCaching();
            app.UseResponseCompression();
            // Rate limiting: Uses client ID if provided via X-ClientId header, otherwise falls back to IP
            // Premium clients with valid IDs get higher limits as configured in ClientRateLimitPolicies
            // Whitelisted clients (via PREMIUM_CLIENT_IDS env var) bypass rate limiting entirely
            // Note: We only use ClientRateLimiting (not IpRateLimiting) because our CustomRateLimitConfiguration
            // falls back to "ip:{clientIp}" when no X-ClientId header is provided, effectively giving us
            // IP-based limiting for anonymous users while allowing premium clients to use their higher quotas
            app.UseClientRateLimiting();
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

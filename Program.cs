global using System;
using System.Runtime.InteropServices;
using AspNetCoreRateLimit;
using Coflnet.Security.OpenBao;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add OpenBao configuration (must be added before Startup configures services)
builder.Configuration.AddOpenBaoFromEnvironment();

var startup = new Coflnet.Sky.Api.Startup(builder.Configuration);
startup.ConfigureServices(builder.Services);

var app = builder.Build();

HypixelContext.SetConfiguration(app.Services.GetRequiredService<IConfiguration>());

// Seed rate limit policies from configuration
using (var scope = app.Services.CreateScope())
{
    // Seed IP rate limit policies
    var ipPolicyStore = scope.ServiceProvider.GetRequiredService<IIpPolicyStore>();
    await ipPolicyStore.SeedAsync();
    
    // Seed Client rate limit policies (for premium clients)
    var clientPolicyStore = scope.ServiceProvider.GetRequiredService<IClientPolicyStore>();
    await clientPolicyStore.SeedAsync();
}

startup.Configure(app, app.Environment,
    app.Services.GetRequiredService<ILogger<Coflnet.Sky.Api.Startup>>(),
    app.Services.GetRequiredService<AutoMapper.IMapper>());

app.Run();

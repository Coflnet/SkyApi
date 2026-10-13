using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller.Tests;

/// <summary>Tests for <see cref="ModBridgeController"/>.</summary>
[TestFixture]
public class ModBridgeControllerTests
{
    private Mock<IModUserResolver> resolver;
    private Mock<IModUserPublisher> publisher;
    private Mock<IBoosterCookieService> cookies;
    private Mock<IPendingBazaarSearchService> pending;
    private ModBridgeController controller;

    /// <summary>Creates the controller with mocks.</summary>
    [SetUp]
    public void Setup()
    {
        resolver = new Mock<IModUserResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<Microsoft.AspNetCore.Mvc.ControllerBase>())).ReturnsAsync(new ModUser("u1", "uuid", "Steve"));
        publisher = new Mock<IModUserPublisher>();
        publisher.Setup(p => p.ExecuteForUser(It.IsAny<string>(), It.IsAny<string>(), null)).ReturnsAsync(1);
        cookies = new Mock<IBoosterCookieService>();
        pending = new Mock<IPendingBazaarSearchService>();
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi.Setup(s => s.GetSettingWithHttpInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(HttpStatusCode.NoContent, null));
        var settings = new SettingsService(new Mock<IConfiguration>().Object, Mock.Of<ILogger<SettingsService>>(), settingsApi.Object);
        controller = new ModBridgeController(resolver.Object, publisher.Object, cookies.Object, pending.Object, settings,
            new MemoryCache(new MemoryCacheOptions()), null);
    }

    /// <summary>With an active cookie /bz is executed as is.</summary>
    [Test]
    public async Task ActiveCookie_PublishesBz()
    {
        cookies.Setup(c => c.Get("Steve")).ReturnsAsync(CookieState.Active);
        var result = await controller.Execute(new ModExecuteRequest { Command = "/bz Enchanted Diamond" });
        result.Executed.Should().BeTrue();
        result.Fallback.Should().BeNull();
        publisher.Verify(p => p.ExecuteForUser("u1", "/bz Enchanted Diamond", null), Times.Once);
        pending.Verify(p => p.Add(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>()), Times.Never);
    }

    /// <summary>Without a cookie the search is queued and replaced by bazaarsearch.</summary>
    [Test]
    public async Task ExpiredCookie_QueuesPendingSearch()
    {
        cookies.Setup(c => c.Get("Steve")).ReturnsAsync(CookieState.Expired);
        var result = await controller.Execute(new ModExecuteRequest { Command = "/bz Enchanted Diamond" });
        result.Fallback.Should().Be("no_cookie_pending_search");
        result.Command.Should().Be("/cofl bazaarsearch Enchanted Diamond");
        pending.Verify(p => p.Add("u1", "Enchanted Diamond", null, null), Times.Once);
        publisher.Verify(p => p.ExecuteForUser("u1", "/cofl bazaarsearch Enchanted Diamond", null), Times.Once);
    }

    /// <summary>No receiving session is reported.</summary>
    [Test]
    public async Task NoReceivers_ReportsNotConnected()
    {
        publisher.Setup(p => p.ExecuteForUser(It.IsAny<string>(), It.IsAny<string>(), null)).ReturnsAsync(0);
        var result = await controller.Execute(new ModExecuteRequest { Command = "/warp forge" });
        result.Executed.Should().BeFalse();
        result.Fallback.Should().Be("not_connected");
    }

    /// <summary>Commands outside the allowlist throw.</summary>
    [Test]
    public async Task DisallowedCommand_Throws()
    {
        var act = () => controller.Execute(new ModExecuteRequest { Command = "/op me" });
        (await act.Should().ThrowAsync<CoflnetException>()).Which.Slug.Should().Be("command_not_allowed");
        publisher.VerifyNoOtherCalls();
    }

    /// <summary>More than 10 commands in the window are rate limited.</summary>
    [Test]
    public async Task EleventhCommand_IsRateLimited()
    {
        for (int i = 0; i < 10; i++)
            await controller.Execute(new ModExecuteRequest { Command = "/warp forge" });
        var act = () => controller.Execute(new ModExecuteRequest { Command = "/warp forge" });
        (await act.Should().ThrowAsync<CoflnetException>()).Which.Slug.Should().Be("rate_limited");
    }
}

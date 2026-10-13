using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Mod.Tests;

/// <summary>Tests for <see cref="BoosterCookieService"/>.</summary>
[TestFixture]
public class BoosterCookieServiceTests
{
    private Mock<IPlayerStateApi> api;
    private BoosterCookieService service;

    /// <summary>Creates the service.</summary>
    [SetUp]
    public void Setup()
    {
        api = new Mock<IPlayerStateApi>();
        service = new BoosterCookieService(api.Object, new MemoryCache(new MemoryCacheOptions()), Mock.Of<ILogger<BoosterCookieService>>());
    }

    private void Returns(DateTime expires)
        => api.Setup(a => a.PlayerStatePlayerIdExtractedGetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new PlayerState.Client.Model.ExtractedInfo { BoosterCookieExpires = expires });

    /// <summary>Future expiry is active.</summary>
    [Test]
    public async Task Future_IsActive()
    {
        Returns(DateTime.UtcNow.AddDays(1));
        (await service.Get("Steve")).Should().Be(CookieState.Active);
    }

    /// <summary>Past expiry is expired.</summary>
    [Test]
    public async Task Past_IsExpired()
    {
        Returns(DateTime.UtcNow.AddDays(-1));
        (await service.Get("Steve")).Should().Be(CookieState.Expired);
    }

    /// <summary>Default value means never seen.</summary>
    [Test]
    public async Task Default_IsUnknown()
    {
        Returns(default);
        (await service.Get("Steve")).Should().Be(CookieState.Unknown);
    }

    /// <summary>Missing name and api failures are unknown.</summary>
    [Test]
    public async Task EmptyName_AndExceptions_AreUnknown()
    {
        (await service.Get(null)).Should().Be(CookieState.Unknown);
        api.Setup(a => a.PlayerStatePlayerIdExtractedGetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
            .ThrowsAsync(new Exception("down"));
        (await service.Get("Alex")).Should().Be(CookieState.Unknown);
    }

    /// <summary>Result is cached per name.</summary>
    [Test]
    public async Task Result_IsCached()
    {
        Returns(DateTime.UtcNow.AddDays(1));
        await service.Get("Steve");
        await service.Get("steve");
        api.Verify(a => a.PlayerStatePlayerIdExtractedGetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    /// <summary>NoCookie setting overrides an active cookie.</summary>
    [Test]
    public void HasCookie_RespectsNoCookieSetting()
    {
        BoosterCookieService.HasCookie(new DescriptionSetting { NoCookie = true }, CookieState.Active).Should().BeFalse();
        BoosterCookieService.HasCookie(new DescriptionSetting(), CookieState.Active).Should().BeTrue();
        BoosterCookieService.HasCookie(null, CookieState.Expired).Should().BeFalse();
    }
}

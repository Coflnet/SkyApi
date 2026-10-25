using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Proxy.Client.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class ActiveAuctionsControllerTests
{
    private ActiveAuctionSet set;
    private ActiveAuctionReplica replica;
    private Mock<ActiveAuctionQueryService> queryService;
    private Mock<ActiveSearchQueryResolver> resolver;
    private AuctionsController controller;
    private ActiveAuctionQuery fallbackQuery;
    private string[] fallbackTags;

    private static readonly ActiveAuctionQueryResult Stored = new(new(), 3, new(), true, DateTime.UtcNow, Array.Empty<string>());

    [SetUp]
    public void Setup()
    {
        fallbackQuery = null;
        set = new ActiveAuctionSet();
        replica = new ActiveAuctionReplica(new ConfigurationBuilder().Build(), set, null, null, NullLogger<ActiveAuctionReplica>.Instance);
        queryService = new Mock<ActiveAuctionQueryService>(null, null, null, null, null, null);
        queryService.Setup(q => q.QueryAsync(It.IsAny<ActiveAuctionQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(Stored);
        queryService.Setup(q => q.ResolveTagsAsync(It.IsAny<ActiveAuctionQuery>()))
            .ReturnsAsync((ActiveAuctionQuery q) => q.Tags);
        resolver = new Mock<ActiveSearchQueryResolver>(null, null, null);
        controller = (AuctionsController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AuctionsController));
        typeof(AuctionsController).GetField("activeReplica", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(controller, replica);
        typeof(AuctionsController).GetField("activeQueryService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(controller, queryService.Object);
        typeof(AuctionsController).GetField("activeSearchResolver", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(controller, resolver.Object);
        controller.DbFallback = (q, tags, _) =>
        {
            fallbackQuery = q;
            fallbackTags = tags;
            return Task.FromResult(new ActiveAuctionQueryResult(new(), 0, new(), false, DateTime.UtcNow, tags));
        };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
    }

    private Task<ActionResult<ActiveAuctionQueryResult>> Call(params (string, string)[] query)
        => controller.GetActiveFiltered(query.ToDictionary(q => q.Item1, q => q.Item2));

    [Test]
    public async Task NotReadyWithOneTagUsesDatabaseFallback()
    {
        var result = await Call(("tags", "hyperion"), ("Reforge", "Heroic"));
        result.Value.FromStore.Should().BeFalse();
        fallbackTags.Should().Equal("hyperion");
        queryService.Verify(q => q.QueryAsync(It.IsAny<ActiveAuctionQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task NotReadyWithoutTagsReturns503WithRetryAfter()
    {
        var result = await Call(("Reforge", "Heroic"));
        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
        controller.Response.Headers["Retry-After"].ToString().Should().Be("30");
        fallbackQuery.Should().BeNull();
    }

    [Test]
    public async Task NotReadyWithManyTagsReturns503()
    {
        var result = await Call(("tags", "a,b,c,d,e"));
        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
    }

    [Test]
    public async Task ReadyDelegatesToQueryServiceAndParsesParameters()
    {
        set.MarkReady();
        ActiveAuctionQuery seen = null;
        queryService.Setup(q => q.QueryAsync(It.IsAny<ActiveAuctionQuery>(), It.IsAny<CancellationToken>()))
            .Callback((ActiveAuctionQuery q, CancellationToken _) => seen = q).ReturnsAsync(Stored);
        var result = await Call(("PageSize", "500"), ("ORDERBY", "ending_soon"), ("tags", "A,B"), ("itemNameContains", "x"),
            ("perTagLimit", "3"), ("sharpness", "7"));
        result.Value.Should().BeSameAs(Stored);
        seen.PageSize.Should().Be(100);
        seen.OrderBy.Should().Be(ActiveSortOrder.ENDING_SOON);
        seen.Tags.Should().Equal("A", "B");
        seen.ItemNameContains.Should().Be("x");
        seen.PerTagLimit.Should().Be(3);
        seen.Filters.Should().Equal(new Dictionary<string, string> { ["sharpness"] = "7" });
    }

    [Test]
    public async Task FreeTextIsMergedAndExplicitFiltersWin()
    {
        set.MarkReady();
        resolver.Setup(r => r.ResolveAsync("hyperion sharp 7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedActiveQuery(new[] { "HYPERION" }, null, new() { ["sharpness"] = "7", ["Reforge"] = "Heroic" }, null, ""));
        ActiveAuctionQuery seen = null;
        queryService.Setup(q => q.QueryAsync(It.IsAny<ActiveAuctionQuery>(), It.IsAny<CancellationToken>()))
            .Callback((ActiveAuctionQuery q, CancellationToken _) => seen = q).ReturnsAsync(Stored);
        await Call(("q", "hyperion sharp 7"), ("sharpness", "5"));
        seen.Tags.Should().Equal("HYPERION");
        seen.Filters["sharpness"].Should().Be("5");
        seen.Filters["Reforge"].Should().Be("Heroic");
    }

    [Test]
    public void CountReportsReadiness()
    {
        controller.GetActiveCount().ToString().Should().Contain("ready = False");
        set.MarkReady();
        controller.GetActiveCount().ToString().Should().Contain("ready = True");
    }
}

public class ActiveAuctionHintServiceTests
{
    [Test]
    public async Task HintsOncePerMinutePerAuctionAndPerSeller()
    {
        var now = DateTime.UtcNow;
        var calls = new List<string>();
        var proxy = new Mock<IBaseApi>();
        proxy.Setup(p => p.BaseAhPlayerIdPostWithHttpInfoAsync(It.IsAny<string>(), "sky-api-auction-view", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((string id, string _, int _, CancellationToken _) => { lock (calls) calls.Add(id); })
            .ReturnsAsync(new Coflnet.Sky.Proxy.Client.Client.ApiResponse<object>(System.Net.HttpStatusCode.OK, (object)null, ""));
        var service = new ActiveAuctionHintService(proxy.Object, NullLogger<ActiveAuctionHintService>.Instance, () => now);
        var a = new SaveAuction { UId = 1, AuctioneerId = "seller1" };

        service.HintSeller(a).Should().BeTrue();
        service.HintSeller(a).Should().BeFalse("same auction");
        service.HintSeller(new SaveAuction { UId = 2, AuctioneerId = "seller1" }).Should().BeFalse("same seller");
        service.HintSeller(new SaveAuction { UId = 3, AuctioneerId = "seller2" }).Should().BeTrue();
        now = now.AddSeconds(61);
        service.HintSeller(a).Should().BeTrue();
        service.HintSeller(null).Should().BeFalse();

        for (var i = 0; i < 100 && calls.Count < 3; i++)
            await Task.Delay(20);
        calls.Should().BeEquivalentTo(new[] { "seller1", "seller2", "seller1" });
    }

    [Test]
    public void OldEntriesAreCleanedUp()
    {
        var now = DateTime.UtcNow;
        var proxy = new Mock<IBaseApi>();
        var service = new ActiveAuctionHintService(proxy.Object, NullLogger<ActiveAuctionHintService>.Instance, () => now);
        service.HintSeller(new SaveAuction { UId = 1, AuctioneerId = "s" });
        service.TrackedEntries.Should().Be(2);
        now = now.AddMinutes(11);
        service.HintSeller(new SaveAuction { UId = 2, AuctioneerId = "t" });
        service.TrackedEntries.Should().Be(2);
    }
}

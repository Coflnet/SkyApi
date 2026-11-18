using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Api.Client.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class AuctionsControllerLowSupplyTests
{
    private AuctionsController controller;

    [SetUp]
    public void Setup()
    {
        var pricesApi = new Mock<IPricesApi>();
        pricesApi.Setup(p => p.ApiItemPriceItemTagGetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new PriceSumary(median: 5_000_000, volume: 2));
        var services = new ServiceCollection().AddSingleton(pricesApi.Object).BuildServiceProvider();
        controller = (AuctionsController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AuctionsController));
        SetField("factory", services.GetRequiredService<IServiceScopeFactory>());
        SetField("logger", NullLogger<AuctionsController>.Instance);
    }

    private void SetField(string name, object value)
        => typeof(AuctionsController).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(controller, value);

    [Test]
    public async Task ItemWithFailingLowestBinLookupIsSkippedAndOthersAreReturned()
    {
        controller.LowestBinLookup = (_, tag, _) => tag switch
        {
            "BROKEN" => throw new InvalidOperationException("lookup failed"),
            _ => Task.FromResult((100L, "uuid-" + tag, 200L))
        };

        var result = await controller.BuildSupplyElements(new[] { ("A", 3L), ("BROKEN", 4L), ("B", 5L) });

        result.Select(r => r.Tag).Should().BeEquivalentTo(new[] { "A", "B" });
        result.First(r => r.Tag == "A").LbinData.Lowest.Should().Be(100);
        result.First(r => r.Tag == "A").Supply.Should().Be(3);
    }

    [Test]
    public async Task ManyConcurrentItemsAreAllCollected()
    {
        controller.LowestBinLookup = async (_, tag, _) => { await Task.Yield(); return (1L, tag, 2L); };
        var items = Enumerable.Range(0, 500).Select(i => ("ITEM" + i, 3L)).ToList();

        var result = await controller.BuildSupplyElements(items);

        result.Should().HaveCount(500);
    }

    [Test]
    public async Task LookupsRunWithBoundedConcurrency()
    {
        int inFlight = 0, peak = 0;
        controller.LowestBinLookup = async (_, tag, _) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref peak, now);
            await Task.Delay(10);
            Interlocked.Decrement(ref inFlight);
            return (1L, tag, 2L);
        };
        var items = Enumerable.Range(0, 200).Select(i => ("ITEM" + i, 3L)).ToList();

        var result = await controller.BuildSupplyElements(items);

        result.Should().HaveCount(200);
        peak.Should().BeLessThanOrEqualTo(16);
    }

    [Test]
    public async Task MostlyFailingLookupsLogOneWarning()
    {
        var logger = new Mock<ILogger<AuctionsController>>();
        SetField("logger", logger.Object);
        controller.LowestBinLookup = (_, tag, _) => throw new InvalidOperationException("down");

        await controller.BuildSupplyElements(new[] { ("A", 3L), ("B", 3L), ("C", 3L) });

        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
            Interlocked.CompareExchange(ref target, value, current);
    }
}

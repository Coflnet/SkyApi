using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class PricesControllerTests
{
    [TestCase("569774104c85", "569774104c85")]
    [TestCase("3b49ada2-3f4a-4aea-874b-e308b5733897", "e308b5733897")]
    public void TryGetItemUid_ExtractsLastTwelveHexCharacters(string itemUuid, string expectedUid)
    {
        var auction = new SaveAuction { FlatenedNBT = new() { ["uuid"] = itemUuid } };

        var found = PricesController.TryGetItemUid(auction, out var uid);

        Assert.That(found, Is.True);
        Assert.That(uid, Is.EqualTo(NBT.UidToLong(expectedUid)));
    }

    [Test]
    public void TryGetItemUid_UsesParserContextFallback()
    {
        var auction = new SaveAuction { Context = new() { ["itemUuid"] = "3b49ada2-3f4a-4aea-874b-e308b5733897" } };

        var found = PricesController.TryGetItemUid(auction, out var uid);

        Assert.That(found, Is.True);
        Assert.That(uid, Is.EqualTo(NBT.UidToLong("e308b5733897")));
    }

    [TestCase(null)]
    [TestCase("too-short")]
    [TestCase("not-hex-value")]
    public void TryGetItemUid_RejectsInvalidValues(string itemUuid)
    {
        var auction = new SaveAuction { FlatenedNBT = new() { ["uid"] = itemUuid } };

        Assert.That(PricesController.TryGetItemUid(auction, out _), Is.False);
    }

    [TestCase(0, null)]
    [TestCase(-5, null)]
    [TestCase(90, 90)]
    [TestCase(3600, 3600)]
    [TestCase(100000, 3600)]
    public void ClampMinAge_ClampsToAnHour(int seconds, int? expected)
    {
        var result = PricesController.ClampMinAge(seconds);

        Assert.That(result, Is.EqualTo(expected is null ? null : TimeSpan.FromSeconds(expected.Value)));
    }

    private const string Tag = "GARDEN_WARRIOR_FERMENTO";
    private const int ItemId = 15057;
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static async Task<List<AveragePrice>> FullHistory(string database, DateTime[] aggregatedDays, DateTime[] soldAt)
    {
        var options = new DbContextOptionsBuilder<HypixelContext>().UseInMemoryDatabase(database).Options;
        using var context = new HypixelContext(options);
        context.Prices.AddRange(aggregatedDays.Select(d => new AveragePrice { ItemId = ItemId, Date = d, Avg = 40_000_000 }));
        context.Auctions.AddRange(soldAt.Select((end, i) => new SaveAuction
        { Uuid = "a" + i, Tag = Tag, ItemId = ItemId, End = end, HighestBidAmount = 45_000_000, Count = 1 }));
        await context.SaveChangesAsync();
        var itemDetails = new ItemDetails(Mock.Of<IItemsApi>());
        itemDetails.TagLookup[Tag] = ItemId;
        DiHandler.OverrideService<ItemDetails, ItemDetails>(itemDetails);
        var controller = new PricesController(new PricesService(context, null, null, null), context, null,
            NullLogger<PricesController>.Instance, null, null, null, null, null, itemDetails)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (await controller.GetFullHistory(Tag)).ToList();
    }

    [Test]
    public async Task GetFullHistory_ItemWithoutAggregateRowsReturnsAuctionHistory()
    {
        var sold = Today.AddDays(-20).AddHours(12);

        var result = await FullHistory(nameof(GetFullHistory_ItemWithoutAggregateRowsReturnsAuctionHistory), [], [sold]);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Date, Is.EqualTo(sold.Date));
        Assert.That(result[0].Avg, Is.EqualTo(45_000_000));
    }

    [Test]
    public async Task GetFullHistory_StaleAggregateIsExtendedFromTheDayAfterItsLastRow()
    {
        var aggregated = new[] { Today.AddDays(-41), Today.AddDays(-40) };
        var sold = new[] { Today.AddDays(-40).AddHours(12), Today.AddDays(-20).AddHours(12) };

        var result = await FullHistory(nameof(GetFullHistory_StaleAggregateIsExtendedFromTheDayAfterItsLastRow), aggregated, sold);

        Assert.That(result.Select(p => p.Date), Is.EqualTo(new[] { Today.AddDays(-41), Today.AddDays(-40), Today.AddDays(-20) }));
    }

    [Test]
    public async Task GetFullHistory_CurrentAggregateIsReturnedUnchanged()
    {
        var sold = Today.AddDays(-20).AddHours(12);

        var result = await FullHistory(nameof(GetFullHistory_CurrentAggregateIsReturnedUnchanged), [Today.AddDays(-1)], [sold]);

        Assert.That(result.Select(p => p.Date), Is.EqualTo(new[] { Today.AddDays(-1) }));
    }
}
#pragma warning restore CS1591

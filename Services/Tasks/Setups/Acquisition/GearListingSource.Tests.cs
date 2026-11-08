using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace SkyApi.Services.Tasks.Setups.Acquisition;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class GearListingSourceTests
{
    private const string Tag = "THEORETICAL_HOE_WHEAT_3";
    private const int ItemId = 4711;
    private HypixelContext context;
    private ActiveAuctionSet set;
    private ActiveAuctionGearListingSource source;

    [SetUp]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<HypixelContext>().UseInMemoryDatabase(nameof(GearListingSourceTests) + Guid.NewGuid()).Options;
        context = new HypixelContext(options);
        var itemDetails = new ItemDetails(Mock.Of<IItemsApi>());
        itemDetails.TagLookup[Tag] = ItemId;
        DiHandler.OverrideService<ItemDetails, ItemDetails>(itemDetails);
        var services = new ServiceCollection().AddScoped(_ => new PricesService(context, null, null, null)).BuildServiceProvider();
        set = new ActiveAuctionSet();
        source = new ActiveAuctionGearListingSource(set, new FilterEngine(new Mock<INBT>().Object), services.GetRequiredService<IServiceScopeFactory>());
    }

    [TearDown]
    public void TearDown() => context.Dispose();

    private static SaveAuction Listing(long uid, long price, bool bin = true, long highestBid = 0) => new()
    {
        UId = uid,
        Uuid = AuctionFactory.Uuid((int)uid),
        Tag = Tag,
        ItemId = ItemId,
        StartingBid = price,
        Bin = bin,
        HighestBidAmount = highestBid,
        End = DateTime.Now.AddHours(1),
        Enchantments = new(),
        FlatenedNBT = new()
    };

    [Test]
    public void Active_bins_are_the_bins_without_bids_cheapest_first()
    {
        set.Upsert(Listing(1, 300), "test");
        set.Upsert(Listing(2, 100), "test");
        set.Upsert(Listing(3, 50, bin: false), "test");
        set.Upsert(Listing(4, 10, highestBid: 20), "test");
        set.MarkReady();

        source.ActiveBins(Tag).Select(a => a.StartingBid).Should().Equal(100, 300);
        source.ActiveBins("OTHER").Should().BeEmpty();
    }

    [Test]
    public async Task Database_fallback_returns_the_cheapest_bin()
    {
        context.Auctions.AddRange(Listing(1, 300), Listing(2, 100));
        await context.SaveChangesAsync();

        var lowest = await source.LowestFromDatabaseAsync(Tag, new Dictionary<string, string>(), CancellationToken.None);

        lowest.Should().Be((100L, AuctionFactory.Uuid(2)));
    }

    /// <summary>Regression: the token was only applied to the wait, the database query itself kept running</summary>
    [Test]
    public async Task Database_fallback_passes_the_token_into_the_query()
    {
        context.Auctions.Add(Listing(1, 300));
        await context.SaveChangesAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var lookup = () => source.LowestFromDatabaseAsync(Tag, new Dictionary<string, string>(), cancelled.Token);

        await lookup.Should().ThrowAsync<OperationCanceledException>();
    }
}

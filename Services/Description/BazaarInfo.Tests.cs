using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Model;
using NUnit.Framework;
using AwesomeAssertions;

namespace Coflnet.Sky.Api.Services.Description.Tests;

/// <summary>Contains bazaar info tests for the purse-filtered crafts/flips lists and the tier rule.</summary>
[TestFixture]
public class BazaarInfoTests
{
    private static readonly ISet<string> BazaarItems = new HashSet<string> { "A", "B", "C", "D" };

    private static ProfitableCraft Craft(string itemId, double craftCost, double sellPrice)
        => new() { ItemId = itemId, ItemName = itemId, CraftCost = craftCost, SellPrice = sellPrice, Type = "crafting" };

    /// <summary>Crafts above the purse are excluded, the rest are still ranked by profit.</summary>
    [Test]
    public void SelectCrafts_ExcludesCraftsAbovePurse_RankedByProfit()
    {
        // profit ~= sellPrice*0.99 - craftCost - 2
        var crafts = new[]
        {
            Craft("A", craftCost: 100, sellPrice: 200), // profit ~96, affordable
            Craft("B", craftCost: 1_000_000, sellPrice: 2_000_000), // huge profit but unaffordable
            Craft("C", craftCost: 50, sellPrice: 300), // profit ~245, affordable, best
            Craft("D", craftCost: 10, sellPrice: 60), // profit ~47, affordable
        };

        var result = BazaarInfo.SelectCrafts(crafts, BazaarItems, purse: 500, skip: 0);

        result.Select(c => c.Craft.ItemId).Should().Equal("C", "A", "D");
    }

    /// <summary>With skip=3 the free tier still sees ranks 4-6 among the affordable entries.</summary>
    [Test]
    public void SelectCrafts_SkipThree_ReturnsFourthToSixthAffordableEntries()
    {
        var items = new HashSet<string> { "A", "B", "C", "D", "E", "F", "G" };
        var crafts = new[]
        {
            Craft("A", 10, 1000), // rank 1
            Craft("B", 10, 900),  // rank 2
            Craft("C", 10, 800),  // rank 3
            Craft("D", 10, 700),  // rank 4
            Craft("E", 10, 600),  // rank 5
            Craft("F", 10, 500),  // rank 6
            Craft("G", 1_000_000, 2_000_000), // unaffordable, would otherwise be rank 1
        };

        var affordable = BazaarInfo.SelectCrafts(crafts, items, purse: 500, skip: 0);
        var freeTier = BazaarInfo.SelectCrafts(crafts, items, purse: 500, skip: 3);

        affordable.Select(c => c.Craft.ItemId).Should().Equal("A", "B", "C");
        freeTier.Select(c => c.Craft.ItemId).Should().Equal("D", "E", "F");
    }

    /// <summary>A null purse disables the purse filter entirely (current behaviour preserved).</summary>
    [Test]
    public void SelectCrafts_NullPurse_DoesNotFilter()
    {
        var crafts = new[]
        {
            Craft("A", 10, 1000),
            Craft("B", 1_000_000, 2_000_000),
        };

        var result = BazaarInfo.SelectCrafts(crafts, BazaarItems, purse: null, skip: 0);

        result.Select(c => c.Craft.ItemId).Should().Contain("B");
    }

    private static BazaarFlip Flip(string tag, double sellPrice, double profitPerHour)
        => new() { ItemTag = tag, SellPrice = sellPrice, BuyPrice = sellPrice + 10, ProfitPerHour = profitPerHour };

    /// <summary>Flips the player cannot afford at the buy-order price are excluded and ordering/skip is respected.</summary>
    [Test]
    public void SelectFlips_ExcludesUnaffordable_OrderedByProfitPerHour_SkipRespected()
    {
        var flips = new[]
        {
            Flip("A", sellPrice: 100, profitPerHour: 500),
            Flip("B", sellPrice: 100_000, profitPerHour: 10_000), // unaffordable
            Flip("C", sellPrice: 200, profitPerHour: 400),
            Flip("D", sellPrice: 50, profitPerHour: 300),
            Flip("E", sellPrice: 60, profitPerHour: 200),
        };

        var topTier = BazaarInfo.SelectFlips(flips, purse: 1000, skip: 0);
        var freeTier = BazaarInfo.SelectFlips(flips, purse: 1000, skip: 3);

        topTier.Select(f => f.ItemTag).Should().Equal("A", "C", "D");
        freeTier.Select(f => f.ItemTag).Should().Equal("E");
    }

    /// <summary>Determines the tier skip rule for premium and free accounts.</summary>
    [TestCase(AccountTier.STARTER_PREMIUM, 2, 0)]
    [TestCase(AccountTier.NONE, 2, 3)]
    [TestCase(AccountTier.STARTER_PREMIUM, -2, 3)]
    public void TierSkip_MatchesTierAndExpiry(AccountTier tier, int expiresInDays, int expectedSkip)
    {
        var accountInfo = new AccountInfo
        {
            Tier = tier,
            ExpiresAt = DateTime.UtcNow.AddDays(expiresInDays)
        };

        BazaarInfo.TierSkip(accountInfo).Should().Be(expectedSkip);
    }
}

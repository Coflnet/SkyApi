using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests
{
    public class DungeonChestInfoTests
    {
        [Test]
        public void DungeonChestInfo_BreakdownContainsAllItems_AndSumMatches()
        {
            // Arrange
            var data = new DataContainer();

            // Create two mock auctions representing two chest items
            var a1 = new Core.SaveAuction() { Tag = "ITEM_A", ItemName = "§fItem A", Count = 2 };
            var a2 = new Core.SaveAuction() { Tag = "ITEM_B", ItemName = "§fItem B", Count = 1 };

            // auctionRepresent must be list of (SaveAuction, string[])
            var auctionRepresent = new List<(Core.SaveAuction, string[])>();
            // fill with empty slots up to index 31 where cost is located
            for (int i = 0; i < 31; i++) auctionRepresent.Add((null, Array.Empty<string>()));

            // put our items at slots 12 and 13 (somewhere in inventory)
            auctionRepresent[12] = (a1, new[] { "§7Some lore" });
            auctionRepresent[13] = (a2, new[] { "§7Other lore" });

            // cost slot at 31: contains coin line and Dungeon Chest Key marker
            auctionRepresent.Add((null, Array.Empty<string>())); // index 31
            auctionRepresent[31] = (null, new[] { "", "Cost", "250,000 Coins", "Dungeon Chest Key" });

            data.auctionRepresent = auctionRepresent;

            // PriceEst: fill with nulls and then set medians for our items at their indices
            var priceEst = Enumerable.Repeat<Sniper.Client.Model.PriceEstimate>(null, auctionRepresent.Count).ToList();
            priceEst[12] = new Sniper.Client.Model.PriceEstimate { Median = 100_000 };
            priceEst[13] = new Sniper.Client.Model.PriceEstimate { Median = 50_000 };
            data.PriceEst = priceEst;

            // bazaarPrices: provide DUNGEON_CHEST_KEY price (so the key adds to coins)
            var baz = ImmutableDictionary.CreateBuilder<string, Coflnet.Sky.Bazaar.Client.Model.ItemPrice>();
            baz["DUNGEON_CHEST_KEY"] = new Coflnet.Sky.Bazaar.Client.Model.ItemPrice { SellPrice = 10_000 };
            data.bazaarPrices = baz.ToImmutable();

            // Prepare mods list (use fully-qualified type to avoid model ambiguity)
            data.mods = new List<List<Coflnet.Sky.Api.Models.Mod.DescModification>>();

            var service = new DungeonChestInfo();

            // Act
            service.Apply(data);

            // Assert: find the last added mods (should be one list)
            Assert.That(data.mods, Is.Not.Empty);
            var added = data.mods.Last();

            // Ensure breakdown header and both items are present
            var joined = string.Join("\n", added.Select(x => x.Value));
            Assert.That(joined, Does.Contain("Contents breakdown:"));
            Assert.That(joined, Does.Contain("Item A"));
            Assert.That(joined, Does.Contain("Item B"));

            // Check the chest total estimation equals sum of medians (100k*2 + 50k = 250k)
            Assert.That(joined, Does.Contain("This chest contains items worth"));
            // the displayed medValues are formatted by ModDescriptionService.FormatPriceShort
            var expectedTotal = Coflnet.Sky.Api.Services.ModDescriptionService.FormatPriceShort(100_000 * 2 + 50_000);
            Assert.That(joined, Does.Contain(expectedTotal));
        }
    }
}

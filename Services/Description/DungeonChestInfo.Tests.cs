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


            // Use the real-world dungeon chest content provided in the issue
            // We'll map each JSON entry to the auctionRepresent (SaveAuction, string[])
            var auctionRepresent = new List<(Core.SaveAuction, string[])>();

            // helper to add empty slots
            void AddEmpty(int n) { for (int i = 0; i < n; i++) auctionRepresent.Add((null, Array.Empty<string>())); }

            // Based on provided JSON: there are many leading empty slots; replicate structure exactly
            AddEmpty(10);

            // slot 10: Enchanted Book - Legion I
            auctionRepresent.Add((new Core.SaveAuction { Tag = "ENCHANTMENT_ULTIMATE_LEGION_1", ItemName = "§fEnchanted Book", Count = 1 }, new[] {
                "§9§d§lLegion I",
                "§7Increases all §cCombat §7stats and §b✯",
                "§bMagic Find §7by §e0.07% §7per player within",
                "§7§b30 §7blocks of you, up to §c20 §7players.",
                "",
                "§7§cYou can only have 1 Ultimate",
                "§cEnchantment on an item!",
                "",
                "§7Applicable on: §9Armor",
                "§7§7Apply Cost: §350 Exp Levels",
                "",
                "§7Use this on an item in an Anvil to",
                "§7apply it!",
                "",
                "§f§lCOMMON",
                "",
                "§7§eClick the chest below to purchase",
                "§ethese rewards!"
            }));

            // slot 11: Enchanted Book - Last Stand II
            auctionRepresent.Add((new Core.SaveAuction { Tag = "ENCHANTMENT_ULTIMATE_LAST_STAND_2", ItemName = "§fEnchanted Book", Count = 1 }, new[] {
                "§9§d§lLast Stand II",
                "§7Gain §a+10% §a❈ Defense §7when hit while",
                "§7below §c40%❤§7.",
                "",
                "§7§cYou can only have 1 Ultimate",
                "§cEnchantment on an item!",
                "",
                "§7Applicable on: §9Armor",
                "§7§7Apply Cost: §3100 Exp Levels",
                "",
                "§7Use this on an item in an Anvil to",
                "§7apply it!",
                "",
                "§f§lCOMMON",
                "",
                "§7§eClick the chest below to purchase",
                "§ethese rewards!"
            }));

            // slot 12: Enchanted Book - Ultimate Wise II
            auctionRepresent.Add((new Core.SaveAuction { Tag = "ENCHANTMENT_ULTIMATE_WISE_2", ItemName = "§fEnchanted Book", Count = 1 }, new[] {
                "§9§d§lUltimate Wise II",
                "§7Reduces the ability mana cost of this",
                "§7item by §a20%§7.",
                "",
                "§7§cYou can only have 1 Ultimate",
                "§cEnchantment on an item!",
                "",
                "§7Applicable on: §9Held Item§7, §9§6Precursor",
                "§6Eye",
                "§7§7Apply Cost: §3100 Exp Levels",
                "",
                "§7Use this on an item in an Anvil to",
                "§7apply it!",
                "",
                "§f§lCOMMON",
                "",
                "§7§eClick the chest below to purchase",
                "§ethese rewards!"
            }));

            // slot 13: empty
            auctionRepresent.Add((null, Array.Empty<string>()));

            // slot 14: Enchanted Book - Ultimate Jerry III
            auctionRepresent.Add((new Core.SaveAuction { Tag = "ENCHANTMENT_ULTIMATE_JERRY_3", ItemName = "§fEnchanted Book", Count = 1 }, new[] {
                "§9§d§lUltimate Jerry III",
                "§7Increases the base damage of",
                "§7§fAspect of the Jerry§7 by §a3,000%§7.",
                "",
                "§7§cYou can only have 1 Ultimate",
                "§cEnchantment on an item!",
                "",
                "§7Applicable on: §9§aAspect of the Jerry,",
                "§aSignature Edition§7, §9§fAspect of the",
                "§fJerry",
                "§7§7Apply Cost: §3150 Exp Levels",
                "",
                "§7Use this on an item in an Anvil to",
                "§7apply it!",
                "",
                "§f§lCOMMON",
                "",
                "§7§eClick the chest below to purchase",
                "§ethese rewards!"
            }));

            // slot 15: Undead Essence x108
            auctionRepresent.Add((new Core.SaveAuction { Tag = "ESSENCE_UNDEAD", ItemName = null, Count = 108 }, new[] {
                "§7Undead Essence can be used to",
                "§7convert some items into Dungeon",
                "§7items and upgrade them!",
                "",
                "",
                "§7§eClick the chest below to purchase",
                "§ethese rewards!"
            }));

            // slot 16: Wither Essence x82
            auctionRepresent.Add((new Core.SaveAuction { Tag = "ESSENCE_WITHER", ItemName = null, Count = 82 }, new[] {
                "§7Wither Essence can be used to",
                "§7convert some items into Dungeon",
                "§7items and upgrade them!",
                "",
                "",
                "§7§eClick the chest below to purchase",
                "§ethese rewards!"
            }));

            // Fill remaining slots until the chest contents index (31) with empties so GetCostFromDungeonChest can see it
            while (auctionRepresent.Count < 31) auctionRepresent.Add((null, Array.Empty<string>()));

            // slot 31: SKYBLOCK_CLAIM_CHEST with contents and cost lines (real chest summary)
            auctionRepresent.Add((new Core.SaveAuction { Tag = "SKYBLOCK_CLAIM_CHEST", ItemName = null, Count = 0 }, new[] {
                "§7Contents",
                "§fEnchanted Book (§d§lLegion I§f)",
                "§fEnchanted Book (§d§lLast Stand II§f)",
                "§fEnchanted Book (§d§lUltimate Wise II§f)",
                "§fEnchanted Book (§d§lUltimate Jerry III§f)",
                "§dUndead Essence §8x108",
                "§dWither Essence §8x82",
                "",
                "§7Cost",
                "§62,000,000 Coins",
                "",
                "§7§cNOTE: Coins are withdrawn from your",
                "§cbank if you don't have enough in",
                "§cyour purse.",
                "",
                "§eClick to open!"
            }));

            // Fill to a stable size
            while (auctionRepresent.Count < 90) auctionRepresent.Add((null, Array.Empty<string>()));

            data.auctionRepresent = auctionRepresent;

            // Build PriceEst list and set some realistic medians for the items we know
            var priceEst = Enumerable.Repeat<Sniper.Client.Model.PriceEstimate>(null, auctionRepresent.Count).ToList();
            // reasonable medians: enchanted books ~1_000_000? (use test-friendly smaller values to avoid overflow)
            priceEst[10] = new Sniper.Client.Model.PriceEstimate { Median = 1_000_000 };
            priceEst[11] = new Sniper.Client.Model.PriceEstimate { Median = 1_000_000 };
            priceEst[12] = new Sniper.Client.Model.PriceEstimate { Median = 1_000_000 };
            priceEst[14] = new Sniper.Client.Model.PriceEstimate { Median = 1_000_000 };
            // essences: small per-unit medians
            priceEst[15] = new Sniper.Client.Model.PriceEstimate { Median = 500 }; // undead essence
            priceEst[16] = new Sniper.Client.Model.PriceEstimate { Median = 600 }; // wither essence
            data.PriceEst = priceEst;

            // No bazaar key in this chest; set bazaarPrices empty
            data.bazaarPrices = ImmutableDictionary<string, Coflnet.Sky.Bazaar.Client.Model.ItemPrice>.Empty;

            // Prepare mods list (use fully-qualified type to avoid model ambiguity)
            data.mods = new List<List<Coflnet.Sky.Api.Models.Mod.DescModification>>();

            var service = new DungeonChestInfo();

            // Act
            service.Apply(data);

            // Assert: find the last added mods (should be one list)
            Assert.That(data.mods, Is.Not.Empty);
            var added = data.mods.Last();

            // Ensure breakdown header and expected items are present (enchanted books and essences)
            var joined = string.Join("\n", added.Select(x => x.Value));
            Assert.That(joined, Does.Contain("Contents breakdown:"));
            Assert.That(joined, Does.Contain("Enchanted Book"));
            // allow either raw tag or readable name
            Assert.That(joined.Contains("ESSENCE_UNDEAD") || joined.Contains("Undead Essence"));
            Assert.That(joined.Contains("ESSENCE_WITHER") || joined.Contains("Wither Essence"));

            // Check the chest total estimation equals our computed sum:
            // 4 enchanted books @1_000_000 each = 4_000_000
            // 108 * 500 = 54_000
            // 82 * 600 = 49_200
            var expectedSum = 4_000_000 + 54_000 + 49_200; // 4_103_200
            var expectedTotal = Coflnet.Sky.Api.Services.ModDescriptionService.FormatPriceShort(expectedSum);
            Assert.That(joined, Does.Contain("This chest contains items worth"));
            Assert.That(joined, Does.Contain(expectedTotal));

            // The chest summary included a cost line: 2,000,000 coins in the real data
            var expectedCost = Coflnet.Sky.Api.Services.ModDescriptionService.FormatPriceShort(2_000_000);
            Assert.That(joined, Does.Contain("It costs"));
            Assert.That(joined.Contains(expectedCost));
        }
    }
}

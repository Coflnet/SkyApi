using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Bazaar.Client.Model;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests;

public class KuudraChestInfoTests
{
    [Test]
    public void KuudraChestInfo_UsesClaimChestLoreToDetectInfernalKey()
    {
        var auctionRepresent = new List<(Core.SaveAuction auction, string[] desc)>();

        while (auctionRepresent.Count < 11)
            auctionRepresent.Add((null, Array.Empty<string>()));

        auctionRepresent.Add((new Core.SaveAuction { Tag = "TERROR_BOOTS", ItemName = "§6Terror Boots §6✪", Count = 1 }, new[] {
            "§7§eClick the chest below to purchase",
            "§ethese rewards!"
        }));
        auctionRepresent.Add((new Core.SaveAuction { Tag = "ENCHANTED_BOOK", ItemName = "§fEnchanted Book", Count = 1 }, new[] {
            "§7§eClick the chest below to purchase",
            "§ethese rewards!"
        }));
        auctionRepresent.Add((new Core.SaveAuction { Tag = "ESSENCE_CRIMSON", ItemName = "§dCrimson Essence §8x100", Count = 1 }, new[] {
            "§7§eClick the chest below to purchase",
            "§ethese rewards!"
        }));
        auctionRepresent.Add((new Core.SaveAuction { Tag = "KUUDRA_TEETH", ItemName = "§5Kuudra Teeth", Count = 3 }, new[] {
            "§7§eClick the chest below to purchase",
            "§ethese rewards!"
        }));
        auctionRepresent.Add((new Core.SaveAuction { Tag = null, ItemName = "§6Kraken Shard §8x1", Count = 1 }, new[] {
            "§7§eClick the chest below to purchase",
            "§ethese rewards!"
        }));

        while (auctionRepresent.Count < 31)
            auctionRepresent.Add((null, Array.Empty<string>()));

        auctionRepresent.Add((new Core.SaveAuction { Tag = "SKYBLOCK_CLAIM_CHEST", ItemName = "§aOpen Reward Chest" }, new[] {
            "§7Contents",
            "§6Terror Boots §6✪",
            "§fEnchanted Book",
            "§dCrimson Essence §8x100",
            "§5Kuudra Teeth",
            "§6Kraken Shard §8x1",
            "",
            "§7Cost",
            "§5Infernal Kuudra Key",
            "",
            "§eClick to open!"
        }));

        while (auctionRepresent.Count < 40)
            auctionRepresent.Add((null, Array.Empty<string>()));

        var priceEst = Enumerable.Repeat<Sniper.Client.Model.PriceEstimate>(null, auctionRepresent.Count).ToList();
        priceEst[11] = new Sniper.Client.Model.PriceEstimate { Median = 150_000 };
        priceEst[12] = new Sniper.Client.Model.PriceEstimate { Median = 25_000 };
        priceEst[13] = new Sniper.Client.Model.PriceEstimate { Median = 100_000 };
        priceEst[14] = new Sniper.Client.Model.PriceEstimate { Median = 18_000 };
        priceEst[15] = new Sniper.Client.Model.PriceEstimate { Median = 12_000 };

        var data = new DataContainer
        {
            auctionRepresent = auctionRepresent,
            PriceEst = priceEst,
            bazaarPrices = ImmutableDictionary<string, ItemPrice>.Empty
                .Add("CORRUPTED_NETHER_STAR", new ItemPrice { SellPrice = 20_000, BuyPrice = 19_000 })
                .Add("ENCHANTED_RED_SAND", new ItemPrice { SellPrice = 3_000, BuyPrice = 2_900 })
                .Add("ENCHANTED_MYCELIUM", new ItemPrice { SellPrice = 2_000, BuyPrice = 1_900 }),
            mods = new List<List<DescModification>>()
        };

        var service = new KuudraChestInfo();

        service.Apply(data);

        Assert.That(data.mods, Is.Not.Empty);
        var added = data.mods.Last();
        var joined = string.Join("\n", added.Select(mod => mod.Value));

        var expectedInfernalCost = ModDescriptionService.FormatPriceShort(3_000_000 + 120 * 2_000 + 2 * 20_000);
        var unexpectedBasicCost = ModDescriptionService.FormatPriceShort(200_000 + 2 * 2_000 + 2 * 20_000);

        Assert.That(joined, Does.Contain("Detected Key: Infernal Kuudra Key"));
        Assert.That(joined, Does.Contain($"Key Cost (est): {expectedInfernalCost}"));
        Assert.That(joined, Does.Not.Contain($"Key Cost (est): {unexpectedBasicCost}"));

        var costLine = added.Select(mod => mod.Value).First(line => line.Contains("It costs"));
        Assert.That(costLine, Does.Contain(expectedInfernalCost));
    }

    [Test]
    public void KuudraChestInfo_IncludesEssenceWithoutName_AndShardWithCountSuffix()
    {
        Assert.That(ModDescriptionService.TryGetShardTagFromName("§6Kraken Shard", out var shardTag), Is.True);

        var auctionRepresent = new List<(Core.SaveAuction auction, string[] desc)>();

        while (auctionRepresent.Count < 11)
            auctionRepresent.Add((null, Array.Empty<string>()));

        auctionRepresent.Add((new Core.SaveAuction { Tag = "TERROR_BOOTS", ItemName = "§6Terror Boots §6✪", Count = 1 }, new[] {
            "§7§eClick the chest below to purchase",
            "§ethese rewards!"
        }));
        auctionRepresent.Add((new Core.SaveAuction { Tag = "ESSENCE_CRIMSON", ItemName = null, Count = 100 }, new[] {
            "§7Crimson Essence can be used to upgrade items!"
        }));
        auctionRepresent.Add((new Core.SaveAuction { Tag = null, ItemName = "§6Kraken Shard §8x2", Count = 1 }, new[] {
            "§6Vitality §8(Global)",
            "§7Owned: §b0 Shards"
        }));

        while (auctionRepresent.Count < 31)
            auctionRepresent.Add((null, Array.Empty<string>()));

        auctionRepresent.Add((new Core.SaveAuction { Tag = "SKYBLOCK_CLAIM_CHEST", ItemName = "§aOpen Reward Chest" }, new[] {
            "§7Contents",
            "§6Terror Boots §6✪",
            "§dCrimson Essence §8x100",
            "§6Kraken Shard §8x2",
            "",
            "§7Cost",
            "§6Basic Kuudra Key",
            "",
            "§eClick to open!"
        }));

        while (auctionRepresent.Count < 40)
            auctionRepresent.Add((null, Array.Empty<string>()));

        var priceEst = Enumerable.Repeat<Sniper.Client.Model.PriceEstimate>(null, auctionRepresent.Count).ToList();
        priceEst[11] = new Sniper.Client.Model.PriceEstimate { Median = 100_000 };

        var data = new DataContainer
        {
            auctionRepresent = auctionRepresent,
            PriceEst = priceEst,
            bazaarPrices = ImmutableDictionary<string, ItemPrice>.Empty
                .Add("CORRUPTED_NETHER_STAR", new ItemPrice { SellPrice = 20_000, BuyPrice = 19_000 })
                .Add("ENCHANTED_RED_SAND", new ItemPrice { SellPrice = 3_000, BuyPrice = 2_900 })
                .Add("ENCHANTED_MYCELIUM", new ItemPrice { SellPrice = 2_000, BuyPrice = 1_900 })
                .Add("ESSENCE_CRIMSON", new ItemPrice { SellPrice = 500, BuyPrice = 490 })
                .Add(shardTag, new ItemPrice { SellPrice = 20_000, BuyPrice = 19_000 }),
            mods = new List<List<DescModification>>()
        };

        var service = new KuudraChestInfo();

        service.Apply(data);

        Assert.That(data.mods, Is.Not.Empty);
        var added = data.mods.Last();
        var joined = string.Join("\n", added.Select(mod => mod.Value));

        var expectedTotal = ModDescriptionService.FormatPriceShort(100_000 + 100 * 500 + 2 * 20_000);
        Assert.That(joined, Does.Contain(expectedTotal));
        Assert.That(joined, Does.Contain("Crimson Essence x100"));
        Assert.That(joined, Does.Contain("Kraken Shard"));
    }

    [Test]
    public void KuudraChestInfo_IncludesLoreOnlyAttributeShards()
    {
        const string blazingFortuneTag = "ATTRIBUTE_SHARD+blazing_fortune;1";
        Assert.That(ModDescriptionService.TryGetShardTagFromName("§6Kraken Shard", out var krakenTag), Is.True);

        var auctionRepresent = new List<(Core.SaveAuction auction, string[] desc)>();

        while (auctionRepresent.Count < 11)
            auctionRepresent.Add((null, Array.Empty<string>()));

        auctionRepresent.Add((null, new[] {
            "§6Blazing Fortune §8(Fishing)",
            "§7Grants §81§b§b+10 §b✯ Magic Find §7on §c♆",
            "§cMagmatic §7mobs.",
            "",
            "§7Owned: §b0 Shards",
            "",
            "§7§eClick the chest below to purchase",
            "§ethese rewards!"
        }));
        auctionRepresent.Add((new Core.SaveAuction { Tag = "ESSENCE_CRIMSON", ItemName = null, Count = 2000 }, new[] {
            "§7Crimson Essence can be used to",
            "§7convert some items into Dungeon",
            "§7items and upgrade them!"
        }));
        auctionRepresent.Add((new Core.SaveAuction { Tag = "KUUDRA_TEETH", ItemName = "§5Kuudra Teeth", Count = 3 }, new[] {
            "§7§7Also known as mandibles."
        }));
        auctionRepresent.Add((null, new[] {
            "§6Vitality §8(Global)",
            "§7Grants §82§4§4+20 §4♨ Vitality§7.",
            "",
            "§7Owned: §b15 Shards",
            "",
            "§7§eClick the chest below to purchase",
            "§ethese rewards!"
        }));

        while (auctionRepresent.Count < 31)
            auctionRepresent.Add((null, Array.Empty<string>()));

        auctionRepresent.Add((new Core.SaveAuction { Tag = "SKYBLOCK_CLAIM_CHEST", ItemName = "§aOpen Reward Chest" }, new[] {
            "§7Contents",
            "§6Blazing Fortune Attribute Shard",
            "§dCrimson Essence §8x2000",
            "§5Kuudra Teeth",
            "§6Kraken Shard §8x1",
            "",
            "§7Cost",
            "§6Infernal Kuudra Key",
            "",
            "§eClick to open!"
        }));

        while (auctionRepresent.Count < 40)
            auctionRepresent.Add((null, Array.Empty<string>()));

        var priceEst = Enumerable.Repeat<Sniper.Client.Model.PriceEstimate>(null, auctionRepresent.Count).ToList();
        priceEst[12] = new Sniper.Client.Model.PriceEstimate { Median = 2_016_000 };
        priceEst[13] = new Sniper.Client.Model.PriceEstimate { Median = 22_764 };

        var data = new DataContainer
        {
            auctionRepresent = auctionRepresent,
            PriceEst = priceEst,
            bazaarPrices = ImmutableDictionary<string, ItemPrice>.Empty
                .Add("CORRUPTED_NETHER_STAR", new ItemPrice { SellPrice = 20_000, BuyPrice = 19_000 })
                .Add("ENCHANTED_RED_SAND", new ItemPrice { SellPrice = 3_000, BuyPrice = 2_900 })
                .Add("ENCHANTED_MYCELIUM", new ItemPrice { SellPrice = 1_347, BuyPrice = 1_300 }),
            itemPrices = new Dictionary<string, long>
            {
                [blazingFortuneTag] = 310_000,
                [krakenTag] = 450_000
            },
            mods = new List<List<DescModification>>()
        };

        var service = new KuudraChestInfo();

        service.Apply(data);

        Assert.That(data.mods, Is.Not.Empty);
        var joined = string.Join("\n", data.mods.Last().Select(mod => mod.Value));

    Assert.That(joined, Does.Contain("Blazing Fortune Attribute Shard"));
        Assert.That(joined, Does.Contain("Kraken Shard"));

        var expectedTotal = ModDescriptionService.FormatPriceShort(310_000 + 2_016_000 + 22_764 + 450_000);
        Assert.That(joined, Does.Contain(expectedTotal));
    }
}
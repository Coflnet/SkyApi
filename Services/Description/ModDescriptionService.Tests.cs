using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.PlayerName;
using Coflnet.Sky.PlayerName.Client.Api;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using System.IO;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerName.Client.Client;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Core;
using System.Linq;
using FluentAssertions;
using System.Threading;
using System.Collections.Immutable;
using Coflnet.Sky.Bazaar.Client.Model;

namespace SkyApi.Services.Description;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class ModDescriptionServiceTests
{

    Mock<ISynchronousClient> synchronousClient;
    Mock<IAsynchronousClient> asynchronousClient = null!;
    Mock<IReadableConfiguration> readableConfiguration;
    Mock<ISettingsApi> settingsApi;
    Mock<ISniperClient> sniperClient;
    ModDescriptionService service;
    [SetUp]
    public void NewMethod()
    {
        synchronousClient = new Mock<ISynchronousClient>();
        asynchronousClient = new Mock<IAsynchronousClient>();
        readableConfiguration = new Mock<IReadableConfiguration>();
        IConfiguration configuration = new Mock<IConfiguration>().Object;
        settingsApi = new Mock<ISettingsApi>();
        SettingsService settingsService = new(configuration, Mock.Of<ILogger<SettingsService>>(), settingsApi.Object);

        PlayerNameApi playerNameApi = new PlayerNameApi(synchronousClient.Object, asynchronousClient.Object, readableConfiguration.Object);
        PlayerNameService playerNameService = new(playerNameApi, Mock.Of<ILogger<PlayerNameService>>());
        DiHandler.OverrideService<SettingsService, SettingsService>(settingsService);

        sniperClient = new Mock<ISniperClient>();

        IItemsApi itemsApi = new Mock<IItemsApi>().Object;
        ItemSkinHandler itemSkinHandler = new ItemSkinHandler(itemsApi);
        service = new(Mock.Of<ICraftsApi>(), settingsService, Mock.Of<IdConverter>(), Mock.Of<IServiceScopeFactory>(),
                    Mock.Of<BazaarApi>(), playerNameService, Mock.Of<ILogger<ModDescriptionService>>(), Mock.Of<IConfiguration>(), Mock.Of<IStateUpdateService>(), sniperClient.Object,
                    itemSkinHandler, new(null, null, null), null, null, null, null, null);
    }

    [Test]
    public void BingoShop_PerItemCoinsPerPoint_WithPrereq_IsInsertedAndCalculated()
    {
        // Arrange a minimal Bingo Shop data container with 3 items
        var data = new Coflnet.Sky.Api.Services.Description.DataContainer
        {
            inventory = new InventoryDataWithSettings { ChestName = "Bingo Shop" },
            Items = new List<Item>
            {
                new Item { ItemName = "§fBingo Talisman" },
                new Item { ItemName = "§aBingo Ring" },
                new Item { ItemName = "§fBingo Display" }
            },
            auctionRepresent = new List<(SaveAuction auction, string[] desc)>
            {
                (new SaveAuction { ItemName = "§fBingo Talisman" }, new []{ "§7Cost", "§6100 Bingo Points" }),
                // Requires Bingo Talisman as prerequisite on the next line
                (new SaveAuction { ItemName = "§aBingo Ring" }, new []{ "§7Cost", "§6150 Bingo Points", "§fBingo Talisman" }),
                (new SaveAuction { ItemName = "§fBingo Display" }, new []{ "§7Cost", "§650 Bingo Points" })
            },
            PriceEst = new List<Coflnet.Sky.Sniper.Client.Model.PriceEstimate>
            {
                new() { Median = 1_000_000, ItemKey = "talisman", MedianKey = "talisman" },
                new() { Median = 3_000_000, ItemKey = "ring", MedianKey = "ring" },
                new() { Median = 10_000, ItemKey = "display", MedianKey = "display" }
            },
            mods = new List<List<DescModification>> { new(), new(), new() },
            Loaded = new Dictionary<string, Task<string>>()
        };
        // Needed for number formatting
        data.modService = service;

        var modifier = new BingoShopDisplay();

        // Act
        modifier.Apply(data);

        // Assert: ring entry has a replacement line with coins per Bingo Point after subtracting prerequisite value
        // Ring effective value = 3,000,000 - 1,000,000 = 2,000,000; points = 150 -> 13,333 per point
        data.mods[1].Should().NotBeEmpty();
        var replace = data.mods[1].OfType<DescModification>().First();
        replace.Type.Should().Be(DescModification.ModType.REPLACE);
        replace.Value.Should().Contain("Coins per Bingo Point:");
        replace.Value.Should().Contain("13,333");

        // Assert: also created an info panel appended to mods
        data.mods.Count.Should().Be(4); // 3 items + 1 info panel
    }

    [Test]
    public void BingoShop_InfoPanel_ShowsTopOptionsByPerPoint()
    {
        // Arrange
        var data = new Coflnet.Sky.Api.Services.Description.DataContainer
        {
            inventory = new InventoryDataWithSettings { ChestName = "Bingo Shop" },
            Items = new List<Item>
            {
                new Item { ItemName = "§fBingo Talisman" },
                new Item { ItemName = "§aBingo Ring" },
                new Item { ItemName = "§fBingo Display" }
            },
            auctionRepresent = new List<(SaveAuction auction, string[] desc)>
            {
                (new SaveAuction { ItemName = "§fBingo Talisman" }, new []{ "§7Cost", "§6100 Bingo Points" }),
                (new SaveAuction { ItemName = "§aBingo Ring" }, new []{ "§7Cost", "§6150 Bingo Points", "§fBingo Talisman" }),
                (new SaveAuction { ItemName = "§fBingo Display" }, new []{ "§7Cost", "§650 Bingo Points" })
            },
            PriceEst = new List<Coflnet.Sky.Sniper.Client.Model.PriceEstimate>
            {
                new() { Median = 1_000_000, ItemKey = "talisman", MedianKey = "talisman" },
                new() { Median = 3_000_000, ItemKey = "ring", MedianKey = "ring" },
                new() { Median = 10_000, ItemKey = "display", MedianKey = "display" }
            },
            mods = new List<List<DescModification>> { new(), new(), new() },
            Loaded = new Dictionary<string, Task<string>>()
        };
        data.modService = service;

        var modifier = new BingoShopDisplay();

        // Act
        modifier.Apply(data);

        // Assert info panel content
        var info = data.mods.Last();
        info.Should().NotBeNull();
        info.Count.Should().BeGreaterOrEqualTo(5); // header + 3 entries + drag hint
        info[0].Value.Should().Contain("Best Bingo Points options");
        info.Any(l => l.Value.Contains("Bingo Ring")).Should().BeTrue();
        info.Any(l => l.Value.Contains("13,333")).Should().BeTrue();
    }
    [Test]
    async public Task AddCoinsPerBitValue_ValidPriceAndDescription_CorrectCoinsPerBitAdded()
    {
        settingsApi.Setup(api => api.SettingsGetSettingWithHttpInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(System.Net.HttpStatusCode.NoContent, null)));

        var price = new Coflnet.Sky.Sniper.Client.Model.PriceEstimate { Median = 1000000, ItemKey = "item_key", MedianKey = "median_key" };

        sniperClient.Setup(s => s.GetPrices(It.IsAny<IEnumerable<SaveAuction>>())).Returns<IEnumerable<SaveAuction>>(s => Task.FromResult(Enumerable.Repeat(price, s.Count()).ToList()));

        var res = await service.GetModifications(GetMockInventory(), "test", "test");
        var result = res.ToList();

        var expectedResult = $"*Coins per bit: *740.7*";
        //At element 20 we have "KISMET_FEATHER" worth 1350 bits, expected value should be 100000/1350 = 740
        result[20].ElementAt(0).Value.Should().Match(expectedResult);
    }


    InventoryDataWithSettings GetMockInventory()
    {
        string mockInventoryFile = File.ReadAllText(@"./MockObjects/CommunityShop_InventoryDataWithSettings.json");
        return JsonConvert.DeserializeObject<InventoryDataWithSettings>(mockInventoryFile);
    }

    [Test]
    public void Parse121()
    {
        var base64 = File.ReadAllText("MockObjects/inventory1.21.json");
        var auctions = service.GetAuctionsFromNbt(base64);
        auctions.Count.Should().Be(24);
        auctions[0].auction.Tag.Should().Be("RED_ROSE:6");
        auctions.Last().auction.Tier.Should().Be(Tier.COMMON);
    }
    [Test]
    public void Parse1215()
    {
        var base64 = File.ReadAllText("MockObjects/inventory1.21.5.json");
        var auctions = service.GetAuctionsFromNbt(base64);
        auctions.Count.Should().Be(26);
        auctions[0].auction.Tag.Should().Be("FARM_SUIT_HELMET");
        auctions.First().auction.Tier.Should().Be(Tier.COMMON);
        auctions.First().desc.Count().Should().Be(21);
        auctions.First().desc.First().Should().StartWith("§7Defense: §a+15");
    }

    [Test]
    public void GetsReforgeCost()
    {
        var breakdown = service.GetModifiersOnItem(new SaveAuction() { Tag = "test", Reforge = ItemReferences.Reforge.mossy }, new()
        {

        });
        Assert.That(1, Is.EqualTo(breakdown.Count()));
        Assert.That("OVERGROWN_GRASS", Is.EqualTo(breakdown.First().First().id));
    }

    [Test]
    public void GetPetCraftCost()
    {
        var targetPrice = Random.Shared.Next(10_000, 20_000);
        var cost = service.FullCraftCost(new SaveAuction() { Tag = "PET_MONKEY", ItemName = "§7[Lvl 1] §6Monkey", Tier = Tier.COMMON }, new()
        {
            allCrafts = new() { { "PET_MONKEY", new() { CraftCost = 100_000 } } },
            itemPrices = new() { { "PET_MONKEY_COMMON_0", targetPrice } }
        });
        Assert.That(cost.obtainPrice, Is.EqualTo(targetPrice));
    }

    [Test]
    public void ParseDungeonChest()
    {
        var data = File.ReadAllText("MockObjects/dungeonChest.json");
        var auctions = service.GetAuctionsFromNbt(data);
        Console.WriteLine(JsonConvert.SerializeObject(auctions.Take(9 * 6), Formatting.Indented));
        auctions[11].auction.Tag.Should().Be("ENCHANTMENT_ULTIMATE_WISDOM_1");
        auctions[12].auction.Tag.Should().Be("ENCHANTMENT_ULTIMATE_BANK_2");
        auctions[13].auction.Tag.Should().Be("ENCHANTMENT_ULTIMATE_LAST_STAND_1");
        auctions[14].auction.Tag.Should().Be("ESSENCE_UNDEAD");
        auctions[14].auction.Count.Should().Be(47);
        auctions[15].auction.Tag.Should().Be("ESSENCE_WITHER");
        auctions[15].auction.Count.Should().Be(31);
        auctions[31].auction.Tag.Should().Be("SKYBLOCK_CLAIM_CHEST");
        Coflnet.Sky.Api.Services.Description.DungeonChestInfo
            .GetCostFromDungeonChest(auctions.Select(a => (a.auction, a.desc)).ToList()).Should().Be(1_000_000);
    }

    [TestCase("DRILL", 5_000_000)]
    [TestCase("PROMISING_PICKAXE", 0)]
    public void IgnoresEnchantOnPromising(string tag, int price)
    {
        var enchantVal = service.GetEnchantBreakdown(new SaveAuction() { Tag = tag, Enchantments = [new Enchantment(Enchantment.EnchantmentType.efficiency, 6)] }, new Dictionary<string,ItemPrice>()
        {
            {"SIL_EX",new(){
                BuyPrice = 5_000_000, SellPrice = 4_900_000,
            }}
        });
        enchantVal.Sum(e => e.Item2).Should().Be(price);
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

    /* [Test]
     public async Task GetsAbilityScrolls()
     {
         var json = """
         {"id":null,"itemName":"§f§f§dHeroic Hyperion §6✪§6✪§6✪§6✪§6✪§c➍","tag":"HYPERION",
             "extraAttributes": {
         "rarity_upgrades": 1,
         "stats_book": 10396,
         "modifier": "heroic",
         "art_of_war_count": 1,
         "upgrade_level": 9,
         "uuid": "3762c473-0ac3-4ff4-9ec0-569774104c85",
         "ability_scroll": [
             "IMPLOSION_SCROLL",
             "SHADOW_WARP_SCROLL",
             "WITHER_SHIELD_SCROLL"
         ],
         "hot_potato_count": 15,
         "gems": {
             "COMBAT_0": "PERFECT",
             "unlocked_slots": [
                 "COMBAT_0",
                 "DEFENSIVE_0",
                 "SAPPHIRE_0"
             ],
             "COMBAT_0_gem": "SAPPHIRE",
             "SAPPHIRE_0": "PERFECT"
         },
         "champion_combat_xp": 6687558.162825049,
         "uid": "569774104c85",
         "timestamp": 1695996960000,
         "tier": 8
         }}
         """;
         var parsed = JsonConvert.DeserializeObject<ItemRepresent>(json);
         var service = new ModDescriptionService(null, null, null, null, null, null, null, null, null, null, null, null, null, new(null, null), null);
         var controller = new Coflnet.Sky.Api.Controller.ModController(null, null, null, null, service, null, null, null, new AuctionConverter(null, null));
         var result = await controller.GetPricingBreakdown(new[] { parsed });
         var breakdown = result.First().craftPrice;
         Assert.That(14, Is.EqualTo(breakdown.Count()), JsonConvert.SerializeObject(breakdown, Formatting.Indented));
         Assert.That(breakdown.Select(b => b.ItemTag), Has.Member("IMPLOSION_SCROLL"));
     }

     [Test]
     public void ParsesAttributesFromItem()
     {
         var json = """
         {"id":null,"itemName":"§f§f§6Aurora Chestplate","tag":"AURORA_CHESTPLATE","extraAttributes":{"attributes":{"veteran":1,"mana_regeneration":2},"uid":"e308b5733897","boss_tier":1,"uuid":"3b49ada2-3f4a-4aea-874b-e308b5733897","timestamp":1706160188547,"tier":5},"enchantments":null}
         """;
         var item = JsonConvert.DeserializeObject<Item>(json);
         var auction = new SaveAuction() { };
         auction.SetFlattenedNbt(NBT.FlattenNbtData(item.ExtraAttributes));
         Assert.That("1", Is.EqualTo(auction.FlatenedNBT.GetValueOrDefault("veteran")), JsonConvert.SerializeObject(auction.FlatenedNBT, Formatting.Indented));
     }*/
}

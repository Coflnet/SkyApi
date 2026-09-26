using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using ProfitableCraft = Coflnet.Sky.Crafts.Client.Model.ProfitableCraft;
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
using AwesomeAssertions;
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
    Mock<ICraftsApi> craftsApi;
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
        craftsApi = new Mock<ICraftsApi>();

        IItemsApi itemsApi = new Mock<IItemsApi>().Object;
        ItemSkinHandler itemSkinHandler = new ItemSkinHandler(itemsApi);
        service = new(craftsApi.Object, settingsService, Mock.Of<IdConverter>(), Mock.Of<IServiceScopeFactory>(),
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
        info.Count.Should().BeGreaterThanOrEqualTo(4); // header + 3 entries
        info[0].Value.Should().Contain("Best Bingo Points options");
        info.Any(l => l.Value.Contains("Bingo Ring")).Should().BeTrue();
        info.Any(l => l.Value.Contains("13,333")).Should().BeTrue();
    }

    [Test]
    public void PlayerPageFlipHighlight_HighlightsAndShowsProfitOnlyForProfitableAuctions()
    {
        var data = new DataContainer
        {
            auctionRepresent =
            [
                (new SaveAuction(), []),
                (new SaveAuction(), ["§7Buy it now: §a1,000,000 coins"]),
                (new SaveAuction(), ["§7Buy it now: §a1,000,000 coins"])
            ],
            PriceEst =
            [
                new() { ItemKey = "EMPTY", MedianKey = "EMPTY" },
                new() { Median = 500_000, ItemKey = "OVERPRICED", MedianKey = "OVERPRICED" },
                new() { Median = 2_000_000, ItemKey = "FLIP", MedianKey = "FLIP" }
            ],
            mods = [new(), new(), new()],
            modService = service
        };

        new PlayerPageFlipHighlight().Apply(data);

        data.mods[0].Should().NotContain(mod => mod.Type == DescModification.ModType.HIGHLIGHT);
        data.mods[1].Should().NotContain(mod => mod.Type == DescModification.ModType.HIGHLIGHT);
        data.mods[2].Should().ContainSingle(mod => mod.Type == DescModification.ModType.HIGHLIGHT);
        var expectedProfit = FlipInstance.ProfitAfterFees(2_000_000, 1_000_000);
        data.mods[2].Should().ContainSingle(mod => mod.Value == $"Med profit: §6{service.FormatNumber(expectedProfit)}");
    }

    [Test]
    async public Task AddCoinsPerBitValue_ValidPriceAndDescription_CorrectCoinsPerBitAdded()
    {
        settingsApi.Setup(api => api.GetSettingWithHttpInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(System.Net.HttpStatusCode.NoContent, null)));

        var price = new Coflnet.Sky.Sniper.Client.Model.PriceEstimate { Median = 1000000, ItemKey = "item_key", MedianKey = "median_key" };

        sniperClient.Setup(s => s.GetPrices(It.IsAny<IEnumerable<SaveAuction>>(), default))
            .Returns<IEnumerable<SaveAuction>, bool>((s, ai) => Task.FromResult(Enumerable.Repeat(price, s.Count()).ToList()));

        var res = await service.GetModifications(GetMockInventory(), "test", "test");
        var result = res.ToList();

        var expectedResult = $"*Coins per bit: *740.7*";
        //At element 20 we have "KISMET_FEATHER" worth 1350 bits, expected value should be 100000/1350 = 740
        result[20].ElementAt(0).Value.Should().Match(expectedResult);
    }


    [Test]
    public async Task ExplicitEmptyPreviewLayoutDoesNotFallBackToSavedFields()
    {
        var inventory = GetMockInventory();
        var draft = new DescriptionSetting { Fields = new() };
        inventory.Settings = draft;
        settingsApi.Setup(api => api.GetSettingWithHttpInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(System.Net.HttpStatusCode.NoContent, null));
        settingsApi.Setup(api => api.GetSettingWithHttpInfoAsync("mod", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(System.Net.HttpStatusCode.OK, "\"preview-user\""));
        sniperClient.Setup(s => s.GetPrices(It.IsAny<IEnumerable<SaveAuction>>(), default))
            .Returns<IEnumerable<SaveAuction>, bool>((s, ai) => Task.FromResult(s.Select(_ => new Coflnet.Sky.Sniper.Client.Model.PriceEstimate()).ToList()));

        await service.GetModifications(inventory, "test", "test");

        Assert.That(inventory.Settings, Is.SameAs(draft));
        Assert.That(inventory.Settings.Fields, Is.Empty);
    }

    [Test]
    public async Task ExplicitDisabledPreviewReturnsNoLoreModifications()
    {
        var inventory = GetMockInventory();
        inventory.ChestName = "Lore preview";
        inventory.Settings = new DescriptionSetting { Fields = new() { new() { DescriptionField.LBIN } }, Disabled = true };
        settingsApi.Setup(api => api.GetSettingWithHttpInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(System.Net.HttpStatusCode.NoContent, null));
        settingsApi.Setup(api => api.GetSettingWithHttpInfoAsync("mod", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(System.Net.HttpStatusCode.OK, "\"preview-user\""));
        sniperClient.Setup(s => s.GetPrices(It.IsAny<IEnumerable<SaveAuction>>(), default))
            .Returns<IEnumerable<SaveAuction>, bool>((s, ai) => Task.FromResult(s.Select(_ => new Coflnet.Sky.Sniper.Client.Model.PriceEstimate { Median = 100 }).ToList()));

        var result = (await service.GetModifications(inventory, "test", "test")).ToList();

        Assert.That(result, Is.Not.Empty);
        Assert.That(result.All(modifications => !modifications.Any()), Is.True);
    }

    InventoryDataWithSettings GetMockInventory()
    {
        string mockInventoryFile = File.ReadAllText(@"./MockObjects/CommunityShop_InventoryDataWithSettings.json");
        return JsonConvert.DeserializeObject<InventoryDataWithSettings>(mockInventoryFile);
    }

    [TestCase(null, "Hideonwall", "SHARD_HIDEONWALL")]
    [TestCase("ATTRIBUTE_SHARD", "Hideonwall", "SHARD_HIDEONWALL")]
    [TestCase(null, "Beetle", "SHARD_CROPEETLE")]
    [TestCase("ATTRIBUTE_SHARD", "Beetle", "SHARD_CROPEETLE")]
    [TestCase("ATTRIBUTE_SHARD", "Attribute", "ATTRIBUTE_SHARD")]
    public void InventoryUploadUsesTheDescriptionShardId(string rawId, string name, string expected)
    {
        var extra = new fNbt.Tags.NbtCompound("ExtraAttributes") {
            new fNbt.Tags.NbtString("uuid", "00000000-0000-0000-0000-000000000001") };
        if (rawId != null)
            extra.Add(new fNbt.Tags.NbtString("id", rawId));
        var item = new fNbt.Tags.NbtCompound() {
            new fNbt.Tags.NbtString("id", "minecraft:player_head"), new fNbt.Tags.NbtByte("Count", 1),
            new fNbt.Tags.NbtShort("Damage", 3),
            new fNbt.Tags.NbtCompound("tag") { extra,
                new fNbt.Tags.NbtCompound("display") {
                    new fNbt.Tags.NbtString("Name", "§6§lSELL §9" + name + " Shard"),
                    new fNbt.Tags.NbtList("Lore", new[] { new fNbt.Tags.NbtString(null, "§7Offer amount: §a129§7x"),
                        new fNbt.Tags.NbtString(null, "§7Price per unit: §610 coins"), new fNbt.Tags.NbtString(null, "§8Expired!") })
                }
            }
        };
        var nbt = new fNbt.NbtFile(new fNbt.Tags.NbtCompound("") {
            new fNbt.Tags.NbtList("i", new[] { new fNbt.Tags.NbtCompound(), item }) });
        using var stream = new MemoryStream();
        nbt.SaveToStream(stream, fNbt.NbtCompression.GZip);
        var inventory = new InventoryData { FullInventoryNbt = Convert.ToBase64String(stream.ToArray()) };
        var auctions = service.ConvertToAuctions(inventory);
        var forwarded = service.InventoryToItems(inventory, auctions);
        Assert.That(auctions[1].auction.Tag, Is.EqualTo(expected));
        Assert.That(forwarded[1].Tag, Is.EqualTo(expected));
        Assert.That(forwarded[1].Description, Does.Contain("Expired!"));
        Assert.That(forwarded[0].Tag, Is.Null);
        Assert.That(service.InventoryToItems(inventory)[1].Tag, Is.EqualTo(expected));
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

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task CraftCostOnly_DuplicateRecipesDoNotPreventCacheRefresh(bool buyOrderPrices, bool reverseRecipes)
    {
        var inventory = GetMockInventory();
        inventory.ChestName = "Inventory";
        inventory.Settings = new DescriptionSetting
        {
            Fields = [[DescriptionField.CRAFT_COST]],
            BuyOrderPrices = buyOrderPrices
        };
        var newest = new ProfitableCraft
        {
            ItemId = "FEATHER_ARTIFACT", CraftCost = 2_000, BuyOrderCraftCost = 1_500,
            LastUpdated = new DateTime(2026, 9, 21)
        };
        var recipes = new List<ProfitableCraft>
        {
            new() { ItemId = "FEATHER_ARTIFACT", CraftCost = 1_000, LastUpdated = newest.LastUpdated.AddDays(-1) },
            newest,
            new() { ItemId = "KISMET_FEATHER", CraftCost = 1_200_000, BuyOrderCraftCost = 1_100_000 },
            new() { ItemId = "NO_RECIPE", CraftCost = 0 }
        };
        if (reverseRecipes)
            recipes.Reverse();
        craftsApi.Setup(api => api.GetAllAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(recipes);
        sniperClient.Setup(s => s.GetPrices(It.IsAny<IEnumerable<SaveAuction>>(), default))
            .Returns<IEnumerable<SaveAuction>, bool>((items, _) => Task.FromResult(items.Select(_ => new Coflnet.Sky.Sniper.Client.Model.PriceEstimate()).ToList()));

        await service.GetModifications(inventory, "test", "");
        Assert.That(() => service.DeserializedCache.Crafts.Count, Is.EqualTo(2).After(2000, 10));
        Assert.That(service.DeserializedCache.Crafts["FEATHER_ARTIFACT"], Is.SameAs(newest));

        var result = (await service.GetModifications(inventory, "test", "")).ToList();

        Assert.That(result[20].Single().Value, Is.EqualTo(buyOrderPrices
            ? "§7clean craft: §e1,100,000 " : "§7clean craft: §e1,200,000 "));
    }

    [Test]
    public async Task FieldsOnSameLineAreSeparatedBySingleSpace()
    {
        var inventory = GetMockInventory();
        inventory.ChestName = "Inventory";
        // TAG already ends with a space, NONE adds nothing, the key fields have no trailing space
        inventory.Settings = new DescriptionSetting
        {
            Fields = [[DescriptionField.TAG, DescriptionField.NONE, DescriptionField.MEDIAN_KEY, DescriptionField.ITEM_KEY]]
        };
        sniperClient.Setup(s => s.GetPrices(It.IsAny<IEnumerable<SaveAuction>>(), default))
            .Returns<IEnumerable<SaveAuction>, bool>((items, _) => Task.FromResult(items.Select(_ =>
                new Coflnet.Sky.Sniper.Client.Model.PriceEstimate { MedianKey = "median_key", ItemKey = "item_key" }).ToList()));

        var result = (await service.GetModifications(inventory, "test", "")).ToList();

        Assert.That(result[20].Single().Value, Is.EqualTo("KISMET_FEATHER Med-Key: median_key Item-Key: item_key"));
    }

    [Test]
    public void FullCraftCost_PetWithoutLevelInNameOrDescription_DoesNotThrow()
    {
        // Synthetic/crafted pet auctions (e.g. PET_SIZED_CUPCAKE used internally for craft cost)
        // have no "Lvl" in their name and are not part of auctionRepresent, so the description
        // fallback yields null. CleanItemprice must not call Regex.Match on a null input.
        var cost = service.FullCraftCost(new SaveAuction() { Tag = "PET_SIZED_CUPCAKE", ItemName = "Pet-Sized Cupcake", Tier = Tier.COMMON }, new()
        {
            allCrafts = new(),
            itemPrices = new() { { "PET_SIZED_CUPCAKE", 5_000 } },
            auctionRepresent = new()
        });
        cost.obtainPrice.Should().Be(5_000);
    }

    [TestCase(false, 7_000)]
    [TestCase(true, 6_000)]
    public void FullCraftCost_BuyOrderSettingSwitchesModifierPrice(bool buyOrderPrices, long expected)
    {
        var pet = new SaveAuction
        {
            Tag = "PET_SIZED_CUPCAKE",
            ItemName = "Pet-Sized Cupcake",
            Tier = Tier.COMMON,
            ItemCreatedAt = DateTime.UtcNow,
            FlatenedNBT = new() { { "heldItem", "PET_ITEM_TEST" } }
        };
        var cost = service.FullCraftCost(pet, new()
        {
            inventory = new() { Settings = new DescriptionSetting { BuyOrderPrices = buyOrderPrices } },
            allCrafts = new(),
            itemPrices = new() { { "PET_SIZED_CUPCAKE", 5_000 } },
            bazaarPrices = new Dictionary<string, ItemPrice>
            {
                { "PET_ITEM_TEST", new() { BuyPrice = 2_000, SellPrice = 1_000 } }
            }.ToImmutableDictionary(),
            auctionRepresent = new()
        });
        cost.summary.Should().Be(expected);
    }

    [Test]
    public void FullCraftCost_ModifierPriceFallsBackToSellPriceWhenNoSellOffers()
    {
        // default mode wants BuyPrice, but the modifier item has no sell offers (BuyPrice 0),
        // so it should fall back to SellPrice instead of pricing the modifier at 0
        var pet = new SaveAuction
        {
            Tag = "PET_SIZED_CUPCAKE",
            ItemName = "Pet-Sized Cupcake",
            Tier = Tier.COMMON,
            ItemCreatedAt = DateTime.UtcNow,
            FlatenedNBT = new() { { "heldItem", "PET_ITEM_TEST" } }
        };
        var cost = service.FullCraftCost(pet, new()
        {
            inventory = new() { Settings = new DescriptionSetting { BuyOrderPrices = false } },
            allCrafts = new(),
            itemPrices = new() { { "PET_SIZED_CUPCAKE", 5_000 } },
            bazaarPrices = new Dictionary<string, ItemPrice>
            {
                { "PET_ITEM_TEST", new() { BuyPrice = 0, SellPrice = 1_000 } }
            }.ToImmutableDictionary(),
            auctionRepresent = new()
        });
        cost.summary.Should().Be(6_000);
    }

    [Test]
    public void FullCraftCost_MissingCleanPrice_UsesExactMedianInsteadOfInvalidCraftCost()
    {
        var sealRing = new SaveAuction
        {
            Tag = "SEAL_RING",
            ItemName = "Seal Ring",
            Tier = Tier.RARE,
            ItemCreatedAt = DateTime.UtcNow
        };
        var data = new DataContainer
        {
            auctionRepresent = [(sealRing, [])],
            PriceEst =
            [
                new()
                {
                    ItemKey = "SEAL_RING",
                    MedianKey = "SEAL_RING",
                    Median = 8_300_000
                }
            ],
            allCrafts = new()
            {
                ["SEAL_RING"] = new() { CraftCost = 5_120_000_000 }
            },
            bazaarPrices = ImmutableDictionary<string, ItemPrice>.Empty,
            itemPrices = new()
        };

        var cost = service.FullCraftCost(sealRing, data);

        cost.obtainPrice.Should().Be(8_300_000);
        cost.summary.Should().Be(8_300_000);
        cost.craftPrice.Should().Be(5_120_000_000);
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

    [Test]
    public void EnchantBreakdownUsesBuyOrderPriceWhenEnabled()
    {
        // the immutable snapshot is cached per pricing mode, alternating modes must not mix them up
        var bazaar = new Dictionary<string, ItemPrice>
        {
            { "SIL_EX", new() { BuyPrice = 5_000_000, SellPrice = 4_900_000 } }
        }.ToImmutableDictionary();
        var auction = new SaveAuction() { Tag = "DRILL", Enchantments = [new Enchantment(Enchantment.EnchantmentType.efficiency, 6)] };

        service.GetEnchantBreakdown(auction, bazaar, false).Sum(e => e.Item2).Should().Be(5_000_000);
        service.GetEnchantBreakdown(auction, bazaar, true).Sum(e => e.Item2).Should().Be(4_900_000);
        service.GetEnchantBreakdown(auction, bazaar, false).Sum(e => e.Item2).Should().Be(5_000_000);
    }

    [Test]
    public void EnchantBreakdownFallsBackToSellPriceWhenNoSellOffers()
    {
        // fresh dictionary instance: the lookup is cached per ImmutableDictionary, must not reuse
        // the one from EnchantBreakdownUsesBuyOrderPriceWhenEnabled
        var bazaar = new Dictionary<string, ItemPrice>
        {
            { "SIL_EX", new() { BuyPrice = 0, SellPrice = 4_900_000 } }
        }.ToImmutableDictionary();
        var auction = new SaveAuction() { Tag = "DRILL", Enchantments = [new Enchantment(Enchantment.EnchantmentType.efficiency, 6)] };

        // default mode (useBuyOrderPrices: false) wants BuyPrice, but there are no sell offers (0)
        // so it should fall back to SellPrice
        service.GetEnchantBreakdown(auction, bazaar, false).Sum(e => e.Item2).Should().Be(4_900_000);
    }

    [TestCase(false, "§7Enchants: §e5,000,000 ")]
    [TestCase(true, "§7Enchants: §e4,900,000 ")]
    public void EnchantFieldHonoursBuyOrderSetting(bool buyOrderPrices, string expected)
    {
        var data = new Coflnet.Sky.Api.Services.Description.DataContainer
        {
            inventory = new InventoryDataWithSettings { Settings = new DescriptionSetting { BuyOrderPrices = buyOrderPrices } },
            bazaarPrices = new Dictionary<string, ItemPrice>
            {
                { "SIL_EX", new() { BuyPrice = 5_000_000, SellPrice = 4_900_000 } }
            }.ToImmutableDictionary()
        };
        var builder = new System.Text.StringBuilder();

        service.AddEnchantCost(new SaveAuction() { Tag = "DRILL", Enchantments = [new Enchantment(Enchantment.EnchantmentType.efficiency, 6)] }, builder, data);

        builder.ToString().Should().Be(expected);
    }

    [TestCase(false, "50.0")]
    [TestCase(true, "40.0")]
    public void SkyblockGemsValue_DefaultUsesBuyPrice_SettingUsesSellPrice(bool buyOrderPrices, string expectedPerGem)
    {
        // previously-wrong site: this always read SellPrice regardless of the user's setting
        var auction = new SaveAuction { Tag = "GEMSTONE_ITEM", Count = 1 };
        var data = new Coflnet.Sky.Api.Services.Description.DataContainer
        {
            auctionRepresent = new() { (auction, new[] { "§7100 SkyBlock Gems" }) },
            PriceEst = new() { new Coflnet.Sky.Sniper.Client.Model.PriceEstimate() },
            bazaarPrices = new Dictionary<string, ItemPrice>
            {
                { "GEMSTONE_ITEM", new() { BuyPrice = 5_000, SellPrice = 4_000 } }
            }.ToImmutableDictionary(),
            mods = new() { new() },
            modService = service,
            inventory = new() { Settings = new DescriptionSetting { BuyOrderPrices = buyOrderPrices } }
        };

        new SkyblockGemsValue().Apply(data);

        data.mods[0].Should().ContainSingle();
        data.mods[0][0].Value.Should().Contain(expectedPerGem);
    }

    private static IEnumerable<TestCaseData> MatchingModifierCases()
    {
        // "Bazaar Orders" satisfies both "Bazaar Orders$" and "^Bazaar " (which only needs the trailing space)
        yield return new TestCaseData("Bazaar Orders", new[] { typeof(BazaarOrderAdjust), typeof(BazaarInfo) });
        // 4 leading spaces also satisfy the 2-space "You  " prefix used for auction house highlighting
        yield return new TestCaseData("You    Trade", new[] { typeof(TradeInfoDisplay), typeof(AuctionHouseHighlighting) });
        yield return new TestCaseData("Create BIN Auction", new[] { typeof(ListPriceRecommend) });
        yield return new TestCaseData("Auctions Browser", new[] { typeof(FlipOnNextPage), typeof(StartedAgoToEndsIn), typeof(AuctionHouseHighlighting) });
        yield return new TestCaseData("Community Shop", new[] { typeof(BitsCoinValue), typeof(SkyblockGemsValue) });
        yield return new TestCaseData(null, new[] { typeof(InventoryInfo) }); // null chest name falls back to "Crafting"
        yield return new TestCaseData("Pet - Round 3", new[] { typeof(DarkAuctionPetAdjust) });
        yield return new TestCaseData("(1/2) Fish Family", new[] { typeof(FishFamilyCalculator) });
        yield return new TestCaseData("Obsidian Chest", new[] { typeof(DungeonChestInfo) });
        yield return new TestCaseData("Paid Chest", new[] { typeof(KuudraChestInfo) });
        yield return new TestCaseData("Something ➜ Instant Buy", new[] { typeof(BazaarPriceUpdater), typeof(InstantBuyMaxAmount) });
    }

    [TestCaseSource(nameof(MatchingModifierCases))]
    public void GetMatchingModifiers_ReturnsExpectedModifierTypesInOrder(string chestName, Type[] expectedTypes)
    {
        var matches = service.GetMatchingModifiers(chestName);

        matches.Select(m => m.Modifier.GetType()).Should().Equal(expectedTypes);
    }

    [Test]
    public void ChestNamePatterns_AllRegexesAreNonNull()
    {
        typeof(ChestNamePatterns).GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(p => p.PropertyType == typeof(System.Text.RegularExpressions.Regex))
            .Should().NotBeEmpty()
            .And.OnlyContain(p => p.GetValue(null) != null);
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

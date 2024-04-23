using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
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

namespace SkyApi.Services.Description;
public class ModDescriptionServiceTests
{

    [Test]
    async public Task AddCoinsPerBitValue_ValidPriceAndDescription_CorrectCoinsPerBitAdded()
    {
        // Arrange

        IConfiguration configuration = new Mock<IConfiguration>().Object;
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi.Setup(api => api.SettingsUserIdSettingKeyGetWithHttpInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(System.Net.HttpStatusCode.NoContent, null)));
        SettingsService settingsService = new(configuration, Mock.Of<ILogger<SettingsService>>(), settingsApi.Object);

        ISynchronousClient synchronousClient = new Mock<ISynchronousClient>().Object;
        IAsynchronousClient asynchronousClient = new Mock<IAsynchronousClient>().Object;
        IReadableConfiguration readableConfiguration = new Mock<IReadableConfiguration>().Object;
        PlayerNameApi playerNameApi = new PlayerNameApi(synchronousClient, asynchronousClient, readableConfiguration);

        IItemsApi itemsApi = new Mock<IItemsApi>().Object;
        ItemSkinHandler itemSkinHandler = new ItemSkinHandler(itemsApi);

        PlayerNameService playerNameService = new(playerNameApi, Mock.Of<ILogger<PlayerNameService>>());

        var price = new Coflnet.Sky.Sniper.Client.Model.PriceEstimate { Median = 1000000, ItemKey = "item_key", MedianKey = "median_key" };

        var sniperClient = new Mock<ISniperClient>();
        sniperClient.Setup(s => s.GetPrices(It.IsAny<IEnumerable<SaveAuction>>())).Returns<IEnumerable<SaveAuction>>(s => Task.FromResult(Enumerable.Repeat(price, s.Count()).ToList()));

        ModDescriptionService modDescriptionService = new(Mock.Of<ICraftsApi>(), settingsService, Mock.Of<IdConverter>(), Mock.Of<IServiceScopeFactory>(),
            Mock.Of<BazaarApi>(), playerNameService, Mock.Of<ILogger<ModDescriptionService>>(), Mock.Of<IConfiguration>(), Mock.Of<IStateUpdateService>(), sniperClient.Object,
            itemSkinHandler, new(null, null, null), null, null, null);

        // Act
        var res = await modDescriptionService.GetModifications(GetMockInventory(), "test", "test");
        var result = res.ToList();

        // Assert
        var expectedResult = $"*Coins per bit: *740.0*";

        //At element 20 we have "KISMET_FEATHER" worth 1350 bits, expected value should be 100000/1350 = 740
        result[20].ElementAt(0).Value.Should().Match(expectedResult);
    }

    InventoryDataWithSettings GetMockInventory()
    {
        string mockInventoryFile = File.ReadAllText(@"./MockObjects/CommunityShop_InventoryDataWithSettings.json");
        return JsonConvert.DeserializeObject<InventoryDataWithSettings>(mockInventoryFile);
    }

    [Test]
    public async Task GetsReforgeCost()
    {
        var service = new ModDescriptionService(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var breakdown = service.GetModifiersOnItem(new SaveAuction() { Tag = "test", Reforge = ItemReferences.Reforge.mossy }, new()
        {

        });
        Assert.That(1, Is.EqualTo(breakdown.Count()));
        Assert.That("OVERGROWN_GRASS", Is.EqualTo(breakdown.First().First().id));
    }

    [Test]
    public async Task GetPetCraftCost()
    {
        var service = new ModDescriptionService(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var cost = service.FullCraftCost(new SaveAuction() { Tag = "PET_MONKEY", ItemName = "§7[Lvl 1] §6Monkey", Tier = Tier.COMMON }, new()
        {
            allCrafts = new() { { "PET_MONKEY", new() { CraftCost = 100_000 } } },
            itemPrices = new() { { "PET_MONKEY_COMMON_0", 10_000 } }
        });
        Assert.That(10_000, Is.EqualTo(cost.craftPrice));
    }

    [Test]
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
    }
}

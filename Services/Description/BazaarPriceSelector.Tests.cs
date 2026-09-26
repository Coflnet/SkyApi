using System.Collections.Immutable;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Bazaar.Client.Model;
using NUnit.Framework;

namespace SkyApi.Services.Description;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class BazaarPriceSelectorTests
{
    [Test]
    public void Default_UsesBuyPrice()
    {
        var price = new ItemPrice { BuyPrice = 7_000, SellPrice = 6_000 };
        BazaarPriceSelector.Select(price, useBuyOrderPrices: false).Should().Be(7_000);
    }

    [Test]
    public void BuyOrderSetting_UsesSellPrice()
    {
        var price = new ItemPrice { BuyPrice = 7_000, SellPrice = 6_000 };
        BazaarPriceSelector.Select(price, useBuyOrderPrices: true).Should().Be(6_000);
    }

    [Test]
    public void Default_FallsBackToSellPrice_WhenBuyPriceIsZero()
    {
        // no sell offers on the bazaar -> BuyPrice reports 0, fall back to SellPrice
        var price = new ItemPrice { BuyPrice = 0, SellPrice = 4_900_000 };
        BazaarPriceSelector.Select(price, useBuyOrderPrices: false).Should().Be(4_900_000);
    }

    [Test]
    public void BuyOrderSetting_FallsBackToBuyPrice_WhenSellPriceIsZero()
    {
        // no buy orders on the bazaar -> SellPrice reports 0, fall back to BuyPrice
        var price = new ItemPrice { BuyPrice = 3_200, SellPrice = 0 };
        BazaarPriceSelector.Select(price, useBuyOrderPrices: true).Should().Be(3_200);
    }

    [Test]
    public void BothSidesZero_ReturnsZero()
    {
        var price = new ItemPrice { BuyPrice = 0, SellPrice = 0 };
        BazaarPriceSelector.Select(price, useBuyOrderPrices: false).Should().Be(0);
        BazaarPriceSelector.Select(price, useBuyOrderPrices: true).Should().Be(0);
    }

    [Test]
    public void NullPrice_ReturnsZero()
    {
        BazaarPriceSelector.Select(null, useBuyOrderPrices: false).Should().Be(0);
    }

    [Test]
    public void DataContainer_GetItemprice_DefaultUsesBuyPrice()
    {
        var data = new DataContainer
        {
            bazaarPrices = new Dictionary<string, ItemPrice>
            {
                { "ENCHANTED_REDSTONE", new() { BuyPrice = 5_000, SellPrice = 4_000 } }
            }.ToImmutableDictionary()
        };

        data.GetItemprice("ENCHANTED_REDSTONE").Should().Be(5_000);
    }

    [Test]
    public void DataContainer_GetItemprice_HonoursBuyOrderSetting()
    {
        var data = new DataContainer
        {
            inventory = new() { Settings = new DescriptionSetting { BuyOrderPrices = true } },
            bazaarPrices = new Dictionary<string, ItemPrice>
            {
                { "ENCHANTED_REDSTONE", new() { BuyPrice = 5_000, SellPrice = 4_000 } }
            }.ToImmutableDictionary()
        };

        data.GetItemprice("ENCHANTED_REDSTONE").Should().Be(4_000);
    }

    [Test]
    public void DataContainer_GetItemprice_FallsBackWhenSelectedSideIsZero()
    {
        var data = new DataContainer
        {
            inventory = new() { Settings = new DescriptionSetting { BuyOrderPrices = false } },
            bazaarPrices = new Dictionary<string, ItemPrice>
            {
                { "SIL_EX", new() { BuyPrice = 0, SellPrice = 4_900_000 } }
            }.ToImmutableDictionary()
        };

        data.GetItemprice("SIL_EX").Should().Be(4_900_000);
    }

    [Test]
    public void DataContainer_GetItemprice_ItemPricesDictionaryTakesPrecedenceOverBazaar()
    {
        var data = new DataContainer
        {
            itemPrices = new() { { "PET_MONKEY_COMMON_0", 123_456 } },
            bazaarPrices = new Dictionary<string, ItemPrice>
            {
                { "PET_MONKEY_COMMON_0", new() { BuyPrice = 999_999, SellPrice = 999_999 } }
            }.ToImmutableDictionary()
        };

        data.GetItemprice("PET_MONKEY_COMMON_0").Should().Be(123_456);
    }
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

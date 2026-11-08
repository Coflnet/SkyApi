using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using ProfitableCraft = Coflnet.Sky.Crafts.Client.Model.ProfitableCraft;

namespace SkyApi.Services.Tasks.Setups.Acquisition;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class GearPriceBookTests
{
    private ModDescriptionService descriptions;
    private ModDescriptionPriceBook book;

    [SetUp]
    public void Setup()
    {
        descriptions = new ModDescriptionService(null, null, null, null, null, null, Mock.Of<ILogger<ModDescriptionService>>(),
            Mock.Of<IConfiguration>(), null, null, null, null, null, null, null, null, null);
        book = new ModDescriptionPriceBook(descriptions, new HypixelItemService(new HttpClient(), NullLogger<HypixelItemService>.Instance),
            NullLogger<ModDescriptionPriceBook>.Instance);
    }

    private void Bazaar(params (string Tag, double Buy)[] prices)
    {
        var all = new Dictionary<string, ItemPrice>();
        foreach (var (tag, buy) in prices)
            all[tag] = new ItemPrice { ProductId = tag, BuyPrice = buy, SellPrice = buy * 0.9 };
        descriptions.DeserializedCache.BazaarItems = all.ToImmutableDictionary();
    }

    [Test]
    public void Is_not_loaded_before_the_bazaar_prices_arrive()
    {
        book.IsLoaded.Should().BeFalse();
        Bazaar(("HOT_POTATO_BOOK", 80_000));
        book.IsLoaded.Should().BeTrue();
    }

    [Test]
    public void Enchant_price_is_the_bazaar_book_of_that_level()
    {
        Bazaar(("ENCHANTMENT_HARVESTING_1", 10_000), ("ENCHANTMENT_HARVESTING_6", 2_000_000));

        book.EnchantPrice(Enchantment.EnchantmentType.harvesting, 6).Should().Be(2_000_000);
    }

    [Test]
    public void Enchant_without_any_bazaar_book_has_no_price()
    {
        Bazaar(("HOT_POTATO_BOOK", 80_000));

        book.EnchantPrice(Enchantment.EnchantmentType.sunder, 5).Should().BeNull();
    }

    [Test]
    public void Enchant_lookup_follows_a_new_bazaar_snapshot()
    {
        Bazaar(("ENCHANTMENT_HARVESTING_6", 2_000_000));
        book.EnchantPrice(Enchantment.EnchantmentType.harvesting, 6);
        Bazaar(("ENCHANTMENT_HARVESTING_6", 3_000_000));

        book.EnchantPrice(Enchantment.EnchantmentType.harvesting, 6).Should().Be(3_000_000);
    }

    [Test]
    public void Clean_price_is_the_lower_of_clean_and_craft_cost()
    {
        descriptions.DeserializedCache.ItemPrices["HOE"] = 5_000_000;
        descriptions.DeserializedCache.Crafts["HOE"] = new ProfitableCraft { ItemId = "HOE", CraftCost = 3_000_000 };
        descriptions.DeserializedCache.ItemPrices["ONLY_CLEAN"] = 1_000;

        book.CleanItemPrice("HOE").Should().Be(3_000_000);
        book.CleanItemPrice("ONLY_CLEAN").Should().Be(1_000);
        book.CleanItemPrice("UNKNOWN").Should().BeNull();
    }

    [Test]
    public void Unknown_item_has_no_price_instead_of_zero()
    {
        Bazaar(("HOT_POTATO_BOOK", 80_000));

        book.ItemPrice("HOT_POTATO_BOOK").Should().Be(80_000);
        book.ItemPrice("NOT_ON_THE_BAZAAR").Should().BeNull();
    }
    /// <summary>Regression: EnchantValue scaled the level I price by 2^(L-1) for levels the bazaar never sells (Turbo VI)</summary>
    [Test]
    public void Level_without_its_own_book_is_not_scaled_from_level_one()
    {
        Bazaar(("ENCHANTMENT_TURBO_WHEAT_1", 10_000), ("ENCHANTMENT_TURBO_WHEAT_5", 500_000));

        book.EnchantPrice(Enchantment.EnchantmentType.turbo_wheat, 6).Should().BeNull();
        book.EnchantPrice(Enchantment.EnchantmentType.turbo_wheat, 5).Should().Be(500_000);
    }

    [Test]
    public void Duplex_books_are_sold_as_reiterate()
    {
        ModDescriptionPriceBook.BookTag(Enchantment.EnchantmentType.ultimate_duplex, 3).Should().Be("ENCHANTMENT_ULTIMATE_REITERATE_3");
    }

    [Test]
    public void Books_combine_up_to_the_enchanting_table_maximum()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "{\"max_xp_table_levels\":{\"harvesting\":5}}");
            var withTable = new ModDescriptionPriceBook(descriptions, null, NullLogger<ModDescriptionPriceBook>.Instance, path);

            withTable.CanCombine(Enchantment.EnchantmentType.harvesting, 5).Should().BeTrue();
            withTable.CanCombine(Enchantment.EnchantmentType.harvesting, 6).Should().BeFalse();
            withTable.CanCombine(Enchantment.EnchantmentType.cultivating, 2).Should().BeFalse();
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public void Missing_combine_table_never_combines()
    {
        var missing = new ModDescriptionPriceBook(descriptions, null, NullLogger<ModDescriptionPriceBook>.Instance, "/nonexistent/enchants.json");

        missing.CanCombine(Enchantment.EnchantmentType.harvesting, 5).Should().BeFalse();
    }
}

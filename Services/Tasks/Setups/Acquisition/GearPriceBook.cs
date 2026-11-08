using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

/// <summary>An item to buy for an upgrade, coins are paid on top (eg the reforge fee)</summary>
public record PricedIngredient(string ItemTag, int Amount, long Coins = 0);

/// <summary>Prices of everything an upgrade can need. Null means "no price known".</summary>
public interface IGearPriceBook
{
    /// <summary>False until the bazaar and clean item prices were loaded once</summary>
    bool IsLoaded { get; }
    long? ItemPrice(string tag);
    /// <summary>Bazaar price of the book of exactly this level, null when the bazaar doesn't sell it</summary>
    long? EnchantPrice(Enchantment.EnchantmentType type, int level);
    /// <summary>True when two books of <paramref name="level"/>-1 combine to <paramref name="level"/> on an anvil</summary>
    bool CanCombine(Enchantment.EnchantmentType type, int level);
    /// <summary>Lowest of the clean item price and the craft cost</summary>
    long? CleanItemPrice(string tag);
    /// <summary>Rarity of the item without upgrades, null when unknown</summary>
    Task<Tier?> BaseRarityAsync(string tag);
    /// <summary>Reforge stone and the coin fee, null when the reforge has no known stone</summary>
    Task<PricedIngredient> ReforgeCostAsync(ItemReferences.Reforge reforge, Tier tier);
    /// <summary>Essence and items to go from <paramref name="from"/> to <paramref name="to"/> stars, null when unknown</summary>
    Task<IReadOnlyList<PricedIngredient>> StarCostAsync(string tag, int from, int to);
    /// <summary>What unlocking the slot costs, empty when it needs no unlock, null when unknown</summary>
    Task<IReadOnlyList<PricedIngredient>> SlotUnlockCostAsync(string tag, string slot, IReadOnlyCollection<string> unlockedSlots);
}

/// <summary>
/// Reads prices from the caches of <see cref="ModDescriptionService"/> (bazaar, clean prices, crafts) and
/// item data (rarity, stars, gem slots) from <see cref="HypixelItemService"/>.
/// Enchant books are only priced when the bazaar sells that exact level: the scaling fallbacks of
/// <see cref="PropertyMapper.EnchantValue"/> guess prices for levels that are never sold as books (eg Turbo VI/VII).
/// </summary>
public class ModDescriptionPriceBook : IGearPriceBook
{
    public const string NeuEnchantsPath = "NEU-REPO/constants/enchants.json";
    private readonly ModDescriptionService descriptions;
    private readonly HypixelItemService items;
    private readonly ILogger<ModDescriptionPriceBook> logger;
    private readonly PropertyMapper mapper = new();
    private readonly Lazy<Task> reforgesLoaded;
    private readonly Lazy<IReadOnlyDictionary<string, int>> combineMax;

    public ModDescriptionPriceBook(ModDescriptionService descriptions, HypixelItemService items, ILogger<ModDescriptionPriceBook> logger)
        : this(descriptions, items, logger, NeuEnchantsPath)
    {
    }

    internal ModDescriptionPriceBook(ModDescriptionService descriptions, HypixelItemService items, ILogger<ModDescriptionPriceBook> logger,
        string neuEnchantsPath)
    {
        this.descriptions = descriptions;
        this.items = items;
        this.logger = logger;
        reforgesLoaded = new(LoadReforges);
        combineMax = new(() => LoadCombineMax(neuEnchantsPath));
    }

    public bool IsLoaded => descriptions.DeserializedCache.BazaarItems.Count > 0;

    public long? ItemPrice(string tag) => Positive(descriptions.GetItemprice(tag));

    /// <summary>Bazaar tag of an enchant book, duplex is sold as reiterate</summary>
    public static string BookTag(Enchantment.EnchantmentType type, int level)
    {
        var name = type == Enchantment.EnchantmentType.ultimate_duplex ? "ultimate_reiterate" : type.ToString();
        return $"ENCHANTMENT_{name.ToUpperInvariant()}_{level}";
    }

    public long? EnchantPrice(Enchantment.EnchantmentType type, int level)
    {
        if (!descriptions.DeserializedCache.BazaarItems.TryGetValue(BookTag(type, level), out var price))
            return null;
        return Positive((long)BazaarPriceSelector.Select(price, useBuyOrderPrices: false));
    }

    public bool CanCombine(Enchantment.EnchantmentType type, int level)
        => level >= 2 && combineMax.Value.TryGetValue(type.ToString(), out var max) && level <= max;

    public long? CleanItemPrice(string tag)
    {
        var cache = descriptions.DeserializedCache;
        var clean = cache.ItemPrices.GetValueOrDefault(tag);
        var craft = (long)(cache.Crafts.GetValueOrDefault(tag)?.CraftCost ?? 0);
        var known = new[] { clean, craft }.Where(p => p > 0).ToList();
        return known.Count == 0 ? null : known.Min();
    }

    public async Task<Tier?> BaseRarityAsync(string tag)
    {
        var all = await items.GetItemsAsync();
        if (tag == null || all == null || !all.TryGetValue(tag, out var item))
            return null;
        return Enum.TryParse<Tier>(item.Tier, true, out var tier) && Enum.IsDefined(tier) ? tier : null;
    }

    public async Task<PricedIngredient> ReforgeCostAsync(ItemReferences.Reforge reforge, Tier tier)
    {
        await reforgesLoaded.Value;
        var (stone, coins) = mapper.GetReforgeCost(reforge, tier);
        return string.IsNullOrEmpty(stone) ? null : new PricedIngredient(stone, 1, coins);
    }

    public async Task<IReadOnlyList<PricedIngredient>> StarCostAsync(string tag, int from, int to)
    {
        var costs = (await items.GetStarCost(tag, from, to)).ToList();
        if (costs.Count == 0)
            return null;
        return costs.Select(c => c.Type == "ESSENCE"
                ? new PricedIngredient($"ESSENCE_{c.EssenceType}", c.Amount)
                : new PricedIngredient(c.ItemId, c.Amount)).ToList();
    }

    /// <summary>Slots without unlock costs in the item data are unlocked by default and cost nothing</summary>
    public async Task<IReadOnlyList<PricedIngredient>> SlotUnlockCostAsync(string tag, string slot, IReadOnlyCollection<string> unlockedSlots)
    {
        if (unlockedSlots.Contains(slot))
            return Array.Empty<PricedIngredient>();
        var all = await items.GetItemsAsync();
        var (costs, unavailable) = items.GetSlotCostSync(tag, unlockedSlots.ToList(), unlockedSlots.Append(slot).ToList(), all);
        if (unavailable.Count > 0)
            return null;
        return costs.Select(c => c.ItemId == null
                ? new PricedIngredient(null, 0, c.Coins)
                : new PricedIngredient(c.ItemId, c.Amount ?? 1, c.Coins)).ToList();
    }

    /// <summary>NEU max_xp_table_levels: books up to this level can be combined on an anvil</summary>
    private IReadOnlyDictionary<string, int> LoadCombineMax(string path)
    {
        try
        {
            var table = JObject.Parse(File.ReadAllText(path))["max_xp_table_levels"] as JObject;
            return table?.Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, int>();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load the anvil combine levels, books are priced at their own level");
            return new Dictionary<string, int>();
        }
    }

    private async Task LoadReforges()
    {
        try
        {
            await mapper.LoadNeuConstants();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load reforge stones, reforges stay unpriced");
        }
    }

    private static long? Positive(long value) => value > 0 ? value : null;
}

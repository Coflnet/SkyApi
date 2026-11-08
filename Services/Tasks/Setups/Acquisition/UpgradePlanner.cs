using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

/// <summary>
/// Prices what a base item lacks compared to the spec. Earned modifiers (counters, counter enchants above I, pet level)
/// can't be bought, they become <see cref="GrindRequirement"/>s. Missing prices make the plan incomplete instead of failing.
/// </summary>
public class UpgradePlanner(IGearPriceBook prices)
{
    public const string HotPotatoBook = "HOT_POTATO_BOOK";
    public const string FumingPotatoBook = "FUMING_POTATO_BOOK";
    private const int HotPotatoCap = 10;
    private const int EfficiencyBookMax = 5;
    /// <summary>Turbo enchants above this level come from applied items (Turbo Gourds), not from books</summary>
    private const int TurboBookMax = 5;
    private readonly PropertyMapper mapper = new();

    /// <summary>Enchants whose levels above I come from a counter on the item (cultivating, compact, expertise, ...)</summary>
    public static bool IsCounterEnchant(Enchantment.EnchantmentType type) => Constants.EnchantToAttribute.ContainsKey(type);

    /// <summary>The plan for one candidate, null when it can never meet the spec (other pet rarity, rarity too low)</summary>
    /// <param name="candidate">The base item, a clean item has an empty NBT and the base rarity of the tag</param>
    /// <param name="auctionUuid">Null for the clean item</param>
    public async Task<CheapestBase> PlanAsync(SaveAuction candidate, string auctionUuid, long basePrice, GearTarget target)
    {
        var deficits = GearSpecVerifier.Deficits(candidate, target);
        if (deficits.Any(d => d.Kind is DeficitKind.Rarity or DeficitKind.PetTier))
            return null;
        var upgrades = new List<AcquisitionUpgrade>();
        var grind = new List<GrindRequirement>();
        foreach (var deficit in deficits)
            await AddDeficit(candidate, target, deficit, upgrades, grind);
        var total = basePrice + upgrades.Where(u => u.Priced).Sum(u => u.Cost);
        return new CheapestBase(auctionUuid, basePrice, upgrades, total, grind, upgrades.Any(u => !u.Priced));
    }

    private async Task AddDeficit(SaveAuction candidate, GearTarget target, GearDeficit d, List<AcquisitionUpgrade> upgrades, List<GrindRequirement> grind)
    {
        switch (d.Kind)
        {
            case DeficitKind.Enchant:
                AddEnchant(d, upgrades, grind);
                break;
            case DeficitKind.Stars:
                upgrades.AddRange(Ingredients("stars", "stars", d, await TryGet(() => prices.StarCostAsync(target.Tag, (int)d.Have, (int)d.Need))));
                break;
            case DeficitKind.HotPotato:
                AddHotPotato(d, upgrades);
                break;
            case DeficitKind.Recombobulator:
                upgrades.AddRange(Ingredients("recombobulator", d.Name, d, AppliedIngredients(target.Tag, d.Name, 1)));
                break;
            case DeficitKind.Reforge:
                upgrades.Add(await Reforge(candidate, target, d));
                break;
            case DeficitKind.Gem:
                await AddGem(candidate, target.Tag, d, upgrades);
                break;
            case DeficitKind.Part:
            case DeficitKind.PetItem:
                upgrades.Add(Line(d.Kind == DeficitKind.Part ? "part" : "petItem", d.Name, null, null, d.NeedValue, 1, 0));
                break;
            case DeficitKind.Applied:
                upgrades.AddRange(Ingredients("applied", d.Name, d, AppliedIngredients(target.Tag, d.Name, d.Need - d.Have)));
                break;
            case DeficitKind.Counter:
                grind.Add(new GrindRequirement("counter", d.Name, d.Have, d.Need));
                break;
            case DeficitKind.PetLevel:
                grind.Add(new GrindRequirement("petLevel", d.Name, d.Have, d.Need));
                break;
            default:
                upgrades.Add(new AcquisitionUpgrade("unsupported", d.Name, null, null, null, 0, 0, false));
                break;
        }
    }

    private void AddEnchant(GearDeficit d, List<AcquisitionUpgrade> upgrades, List<GrindRequirement> grind)
    {
        var type = Enum.Parse<Enchantment.EnchantmentType>(d.Name);
        if (IsCounterEnchant(type) && d.Need >= 2)
        {
            AddCounterEnchant(type, d, upgrades, grind);
            return;
        }
        if (type == Enchantment.EnchantmentType.efficiency && d.Need > EfficiencyBookMax)
        {
            upgrades.AddRange(EfficiencyAboveBooks(d));
            return;
        }
        if (IsTurbo(type) && d.Need > TurboBookMax)
        {
            // VI and VII are applied Turbo Gourds, their item tags are not verified, so this stays unpriced
            upgrades.Add(new AcquisitionUpgrade("enchant", d.Name, (int)d.Have, (int)d.Need, null, 1, 0, false));
            return;
        }
        upgrades.Add(CheapestBook(type, d));
    }

    /// <summary>Levels above I are earned through the counter, an item without the enchant needs the level I book first</summary>
    private void AddCounterEnchant(Enchantment.EnchantmentType type, GearDeficit d, List<AcquisitionUpgrade> upgrades, List<GrindRequirement> grind)
    {
        if (d.Have == 0)
            upgrades.Add(Book(type, d.Name, 0, 1, 1));
        grind.Add(new GrindRequirement("enchant", d.Name, d.Have, d.Need));
    }

    /// <summary>The book of the target level, or a second book of the current level when combining them is cheaper</summary>
    private AcquisitionUpgrade CheapestBook(Enchantment.EnchantmentType type, GearDeficit d)
    {
        var target = Book(type, d.Name, (int)d.Have, (int)d.Need, (int)d.Need);
        if (d.Have != d.Need - 1 || !prices.CanCombine(type, (int)d.Need))
            return target;
        var combine = Book(type, d.Name, (int)d.Have, (int)d.Need, (int)d.Have);
        if (!combine.Priced || target.Priced && target.Cost <= combine.Cost)
            return target;
        return combine;
    }

    private AcquisitionUpgrade Book(Enchantment.EnchantmentType type, string name, int from, int to, int bookLevel)
        => Priced("enchant", name, from, to, ModDescriptionPriceBook.BookTag(type, bookLevel), 1, prices.EnchantPrice(type, bookLevel));

    /// <summary>Efficiency VI-X are added one level at a time with Sil Ex on top of an efficiency V book</summary>
    private IEnumerable<AcquisitionUpgrade> EfficiencyAboveBooks(GearDeficit d)
    {
        var have = (int)d.Have;
        if (have < EfficiencyBookMax)
            yield return Book(Enchantment.EnchantmentType.efficiency, d.Name, have, EfficiencyBookMax, EfficiencyBookMax);
        var levels = (int)d.Need - Math.Max(have, EfficiencyBookMax);
        yield return Priced("enchant", d.Name, Math.Max(have, EfficiencyBookMax), (int)d.Need, "SIL_EX", levels, prices.ItemPrice("SIL_EX"));
    }

    private static bool IsTurbo(Enchantment.EnchantmentType type) => type.ToString().StartsWith("turbo_", StringComparison.Ordinal);

    private void AddHotPotato(GearDeficit d, List<AcquisitionUpgrade> upgrades)
    {
        var hot = Math.Max(0, Math.Min(d.Need, HotPotatoCap) - Math.Min(d.Have, HotPotatoCap));
        var fuming = Math.Max(0, Math.Max(0, d.Need - HotPotatoCap) - Math.Max(0, d.Have - HotPotatoCap));
        if (hot > 0)
            upgrades.Add(Line("hotPotato", "hpc", (int)d.Have, (int)Math.Min(d.Need, HotPotatoCap), HotPotatoBook, (int)hot, 0));
        if (fuming > 0)
            upgrades.Add(Line("hotPotato", "hpc", (int)Math.Max(d.Have, HotPotatoCap), (int)d.Need, FumingPotatoBook, (int)fuming, 0));
    }

    private async Task<AcquisitionUpgrade> Reforge(SaveAuction candidate, GearTarget target, GearDeficit d)
    {
        var tier = target.MinRarity ?? (candidate.Tier == Tier.UNKNOWN ? Tier.LEGENDARY : candidate.Tier);
        var stone = await TryGet(() => prices.ReforgeCostAsync(target.Reforge.Value, tier));
        if (stone == null)
            return new AcquisitionUpgrade("reforge", d.NeedValue, null, null, null, 1, 0, false);
        return Line("reforge", d.NeedValue, null, null, stone.ItemTag, 1, stone.Coins);
    }

    private async Task AddGem(SaveAuction candidate, string tag, GearDeficit d, List<AcquisitionUpgrade> upgrades)
    {
        if (d.Have == 0) // a slot that already holds a gem is unlocked
            upgrades.AddRange(Ingredients("gemSlot", d.Name, d, await TryGet(() => prices.SlotUnlockCostAsync(tag, d.Name, UnlockedSlots(candidate)))));
        upgrades.Add(Line("gem", d.Name, null, null, d.NeedValue, 1, 0));
    }

    /// <summary>The unlocked_slots NBT value, separated by commas or spaces</summary>
    public static List<string> UnlockedSlots(SaveAuction candidate)
        => (candidate.FlatenedNBT?.GetValueOrDefault("unlocked_slots") ?? "")
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Items that add <paramref name="count"/> of an applied modifier (books, recombobulator, ...), null when unknown</summary>
    private IReadOnlyList<PricedIngredient> AppliedIngredients(string tag, string key, long count)
    {
        // the delta as value with an empty base: PropertyMapper skips counts that differ by one from a non empty base
        if (!mapper.TryGetIngredients(tag, key, count.ToString(), null, out var items) || items == null || items.Count == 0)
            return null;
        return items.GroupBy(i => i).Select(g => new PricedIngredient(g.Key, g.Count())).ToList();
    }

    private IEnumerable<AcquisitionUpgrade> Ingredients(string kind, string name, GearDeficit d, IReadOnlyList<PricedIngredient> items)
    {
        if (items == null)
            return new[] { new AcquisitionUpgrade(kind, name, (int)d.Have, (int)d.Need, null, 0, 0, false) };
        return items.Select(i => Line(kind, name, (int)d.Have, (int)d.Need, i.ItemTag, i.Amount, i.Coins)).ToList();
    }

    private AcquisitionUpgrade Line(string kind, string name, int? from, int? to, string itemTag, int amount, long coins)
    {
        if (itemTag == null)
            return new AcquisitionUpgrade(kind, name, from, to, null, amount, coins, true);
        var unit = prices.ItemPrice(itemTag);
        return Priced(kind, name, from, to, itemTag, amount, unit, coins);
    }

    private static AcquisitionUpgrade Priced(string kind, string name, int? from, int? to, string itemTag, int amount, long? unit, long coins = 0)
        => new(kind, name, from, to, itemTag, amount, (unit ?? 0) * amount + coins, unit != null);

    private static async Task<T> TryGet<T>(Func<Task<T>> load) where T : class
    {
        try
        {
            return await load();
        }
        catch (Exception)
        {
            return null; // a failing lookup leaves that upgrade unpriced
        }
    }
}

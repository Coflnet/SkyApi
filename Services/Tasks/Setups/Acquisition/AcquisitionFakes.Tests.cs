using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Moq;

namespace SkyApi.Services.Tasks.Setups.Acquisition;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary>Price book backed by dictionaries, unknown prices are null</summary>
public class FakePriceBook : IGearPriceBook
{
    public bool IsLoaded { get; set; } = true;
    public Dictionary<string, long> Items { get; } = new();
    public Dictionary<(Enchantment.EnchantmentType, int), long> Enchants { get; } = new();
    public Dictionary<string, long> Clean { get; } = new();
    public Dictionary<ItemReferences.Reforge, PricedIngredient> Reforges { get; } = new();
    public Dictionary<string, IReadOnlyList<PricedIngredient>> Stars { get; } = new();
    public Dictionary<string, IReadOnlyList<PricedIngredient>> SlotUnlocks { get; } = new();

    public long? ItemPrice(string tag) => tag != null && Items.TryGetValue(tag, out var p) ? p : null;
    public HashSet<(Enchantment.EnchantmentType, int)> Combinable { get; } = new();
    public Dictionary<string, Tier> Rarities { get; } = new();
    public long? EnchantPrice(Enchantment.EnchantmentType type, int level)
        => Enchants.TryGetValue((type, level), out var p) ? p : null;
    public bool CanCombine(Enchantment.EnchantmentType type, int level) => Combinable.Contains((type, level));
    public Task<Tier?> BaseRarityAsync(string tag) => Task.FromResult<Tier?>(Rarities.TryGetValue(tag, out var t) ? t : null);
    public long? CleanItemPrice(string tag) => Clean.TryGetValue(tag, out var p) ? p : null;
    public Task<PricedIngredient> ReforgeCostAsync(ItemReferences.Reforge reforge, Tier tier)
        => Task.FromResult(Reforges.GetValueOrDefault(reforge));
    public Task<IReadOnlyList<PricedIngredient>> StarCostAsync(string tag, int from, int to)
        => Task.FromResult(Stars.GetValueOrDefault($"{tag}:{from}-{to}"));
    public Task<IReadOnlyList<PricedIngredient>> SlotUnlockCostAsync(string tag, string slot, IReadOnlyCollection<string> unlockedSlots)
        => Task.FromResult(SlotUnlocks.GetValueOrDefault(slot) ?? (IReadOnlyList<PricedIngredient>)Array.Empty<PricedIngredient>());
}

/// <summary>Listing source over a list, matching with the real filter engine</summary>
public class FakeListingSource : IGearListingSource
{
    private static readonly FilterEngine Engine = new(new Mock<INBT>().Object);
    public bool IsReady { get; set; } = true;
    public List<SaveAuction> Auctions { get; } = new();
    public int ActiveBinCalls;
    public Func<IReadOnlyList<SaveAuction>> ActiveBinsOverride;
    public Func<Dictionary<string, string>, CancellationToken, Task<(long Price, string Uuid)?>> Database
        = (_, _) => Task.FromResult<(long, string)?>(null);
    public Dictionary<string, string> LastDatabaseFilters;

    public IReadOnlyList<SaveAuction> ActiveBins(string tag)
    {
        Interlocked.Increment(ref ActiveBinCalls);
        if (ActiveBinsOverride != null)
            return ActiveBinsOverride();
        return Auctions.Where(a => a.Tag == tag).OrderBy(a => a.StartingBid).ToList();
    }

    public Func<SaveAuction, bool> Matcher(Dictionary<string, string> filters)
        => filters.Count == 0 ? _ => true : Engine.GetMatcher(new Dictionary<string, string>(filters));

    public Task<(long Price, string Uuid)?> LowestFromDatabaseAsync(string tag, Dictionary<string, string> filters, CancellationToken cancellationToken)
    {
        LastDatabaseFilters = filters;
        return Database(filters, cancellationToken);
    }
}

public static class AuctionFactory
{
    private static int counter;

    public static string Uuid(int n) => n.ToString("x32");

    public static SaveAuction Bin(string tag, long price, Tier tier = Tier.LEGENDARY, Dictionary<string, string> nbt = null,
        params (Enchantment.EnchantmentType Type, int Level)[] enchants)
    {
        return new SaveAuction
        {
            Uuid = Uuid(Interlocked.Increment(ref counter)),
            Tag = tag,
            StartingBid = price,
            Bin = true,
            Tier = tier,
            End = DateTime.UtcNow.AddHours(1),
            Enchantments = enchants.Select(e => new Enchantment(e.Type, (byte)e.Level)).ToList(),
            FlatenedNBT = nbt ?? new()
        };
    }
}

using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

/// <summary>
/// How to get an item that meets a gear spec. Each part is null when it is not available, the matching reason says why.
/// </summary>
/// <param name="Direct">Cheapest active BIN that already meets the whole spec</param>
/// <param name="DirectUnavailableReason">Slug why <paramref name="Direct"/> is null</param>
/// <param name="Base">Cheapest base item plus the upgrades it is missing</param>
/// <param name="BaseUnavailableReason">Slug why <paramref name="Base"/> is null</param>
/// <param name="Filters">The auction filters for the spec (filterable modifiers only), usable for an auction house link</param>
/// <param name="ComputedAt">When this result was computed</param>
public record AcquisitionResult(
    DirectPurchase Direct,
    string DirectUnavailableReason,
    CheapestBase Base,
    string BaseUnavailableReason,
    IReadOnlyDictionary<string, string> Filters,
    DateTimeOffset ComputedAt)
{
    public static AcquisitionResult Failed(string reason, DateTimeOffset at)
        => new(null, reason, null, reason, new Dictionary<string, string>(), at);
}

/// <param name="AuctionUuid">32 lowercase hex characters</param>
/// <param name="Price">BIN price in coins</param>
/// <param name="Command">In game command that opens the auction</param>
public record DirectPurchase(string AuctionUuid, long Price, string Command);

/// <param name="AuctionUuid">Auction of the base item, null when the base is a clean item at its clean price</param>
/// <param name="BasePrice">Price of the base item</param>
/// <param name="Upgrades">Upgrades to buy and apply</param>
/// <param name="Total">Base price plus every priced upgrade</param>
/// <param name="GrindRequired">Earned modifiers the base lacks, they can't be bought</param>
/// <param name="Incomplete">True when at least one upgrade has no price, <paramref name="Total"/> is then too low</param>
public record CheapestBase(
    string AuctionUuid,
    long BasePrice,
    IReadOnlyList<AcquisitionUpgrade> Upgrades,
    long Total,
    IReadOnlyList<GrindRequirement> GrindRequired,
    bool Incomplete);

/// <param name="Kind">enchant, stars, hotPotato, recombobulator, reforge, gem, gemSlot, part, applied, petItem, unsupported</param>
/// <param name="Name">Enchant name, gem slot, part key or modifier key</param>
/// <param name="FromLevel">Level on the base, null for items without levels</param>
/// <param name="ToLevel">Target level</param>
/// <param name="ItemTag">Item to buy, null when unknown</param>
/// <param name="Amount">How many of <paramref name="ItemTag"/></param>
/// <param name="Cost">Coins for this line, 0 when not priced</param>
/// <param name="Priced">False when no price was found</param>
public record AcquisitionUpgrade(
    string Kind,
    string Name,
    int? FromLevel,
    int? ToLevel,
    string ItemTag,
    int Amount,
    long Cost,
    bool Priced);

/// <param name="Kind">counter, enchant (levels above I that come from a counter) or petLevel</param>
/// <param name="Name">Counter key, enchant name or pet type</param>
/// <param name="Have">Value on the base</param>
/// <param name="Need">Value of the spec</param>
public record GrindRequirement(string Kind, string Name, long Have, long Need)
{
    public long Deficit => Math.Max(0, Need - Have);
}

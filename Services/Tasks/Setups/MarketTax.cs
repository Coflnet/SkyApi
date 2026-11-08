using System;

namespace Coflnet.Sky.Api.Services.Tasks.Setups;

/// <summary>
/// Coin taxes taken when selling on the Skyblock markets.
/// Verified 2026-11-07 against the official wiki:
/// https://hypixelskyblock.minecraft.wiki/w/Auction_House (BIN creation fee 1% below 10M, 2% from 10M to 100M,
/// 2.5% above 100M; collection tax up to 1% above 1M coins, capped so the seller keeps at least 1M)
/// https://hypixelskyblock.minecraft.wiki/w/Bazaar (1.25% on all sales, the Bazaar Flipper perk lowers it by at most 0.25%)
/// </summary>
public static class MarketTax
{
    public const double BazaarRate = 0.0125;
    public const double AhCollectionRate = 0.01;
    public const double AhCollectionThreshold = 1_000_000;
    public const double AhMidTierStart = 10_000_000;
    public const double AhHighTierStart = 100_000_000;
    /// <summary>PricesService reports AH sell prices as lowest BIN times this haircut</summary>
    public const double PricesServiceAhHaircut = 0.98;

    /// <summary>Exactly 100M still pays 2%, the wiki applies 2.5% only to prices above 100M</summary>
    public static double AhCreationRate(double listPrice)
    {
        if (listPrice < AhMidTierStart)
            return 0.01;
        return listPrice <= AhHighTierStart ? 0.02 : 0.025;
    }

    /// <summary>Collection tax, only above 1M and never taking the seller below 1M</summary>
    public static double AhCollectionTax(double listPrice)
    {
        if (listPrice <= AhCollectionThreshold)
            return 0;
        return Math.Min(AhCollectionRate * listPrice, listPrice - AhCollectionThreshold);
    }

    public static double AhFee(double listPrice)
        => listPrice <= 0 ? 0 : listPrice * AhCreationRate(listPrice) + AhCollectionTax(listPrice);

    /// <summary>Coins kept after selling a BIN auction at the given price</summary>
    public static double NetAh(double listPrice) => listPrice <= 0 ? 0 : listPrice - AhFee(listPrice);

    /// <summary>Coins kept after a bazaar sale at the given price</summary>
    public static double NetBazaar(double price) => price <= 0 ? 0 : price * (1 - BazaarRate);
}

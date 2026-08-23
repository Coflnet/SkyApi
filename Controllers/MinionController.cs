using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.AspNetCore.Mvc;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Ranks minions for a concrete situation: how long the player stays away, what they can spend, and
/// how they intend to sell. A single "best minion" list cannot answer any of those.
/// </summary>
[ApiController]
[Route("api/minions")]
[ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, NoStore = false)]
public class MinionController : ControllerBase
{
    /// <summary>Derpy's TURBO MINIONS doubles what every minion produces.</summary>
    private const double DerpyOutputMultiplier = 2;
    /// <summary>Derpy's MOAR SKILLZ raises skill experience by half.</summary>
    private const double DerpyExperienceMultiplier = 1.5;
    /// <summary>The share of npc value each automated shipping item pays for overflow.</summary>
    private const double BudgetHopperShare = 0.5;
    private const double EnchantedHopperShare = 0.7;

    private readonly MinionService minions;
    private readonly IBazaarApi bazaar;
    private readonly ISniperApi sniper;
    private readonly HypixelItemService items;
    private readonly MinionCompactionService compactionService;

    /// <summary>Creates a new instance of <see cref="MinionController"/></summary>
    public MinionController(MinionService minions, IBazaarApi bazaar, ISniperApi sniper,
        HypixelItemService items, MinionCompactionService compactionService)
    {
        this.minions = minions;
        this.bazaar = bazaar;
        this.sniper = sniper;
        this.items = items;
        this.compactionService = compactionService;
    }

    /// <summary>
    /// Ranks minions for the supplied situation.
    /// </summary>
    /// <param name="offlineHours">Hours between collections. Past the point storage fills, extra speed buys nothing.</param>
    /// <param name="budget">Coins available to craft and upgrade with; omit for no limit.</param>
    /// <param name="sell">offer, instant or npc</param>
    /// <param name="buy">instant or order</param>
    /// <param name="objective">coins or experience</param>
    /// <param name="speedBoost">Additive speed from fuel and upgrades, as a fraction. 0.4 means +40%.</param>
    /// <param name="hopper">none, budget or enchanted</param>
    /// <param name="compaction">Whether a Super Compactor 3000 may be assumed</param>
    /// <param name="derpy">Rank for Derpy's term, which doubles output and raises skill experience by half</param>
    /// <param name="limit">How many to return</param>
    [Route("best")]
    [HttpGet]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, NoStore = false,
        VaryByQueryKeys = new[] { "offlineHours", "budget", "sell", "buy", "objective", "speedBoost", "hopper", "compaction", "derpy", "limit" })]
    public async Task<MinionRankingResponse> GetBest(
        double offlineHours = 24,
        double? budget = null,
        string sell = "offer",
        string buy = "instant",
        string objective = "coins",
        double speedBoost = 0,
        string hopper = "none",
        bool compaction = true,
        bool derpy = false,
        int limit = 10)
    {
        if (offlineHours <= 0)
            throw new ArgumentException("offlineHours has to be positive", nameof(offlineHours));

        var query = new MinionQuery
        {
            OfflineSeconds = offlineHours * 3600,
            Budget = budget ?? double.PositiveInfinity,
            Sell = ParseSell(sell),
            Buy = ParseBuy(buy),
            Objective = ParseObjective(objective),
            SpeedBoost = speedBoost,
            HopperNpcShare = ParseHopper(hopper),
            AllowCompaction = compaction,
            OutputMultiplier = derpy ? DerpyOutputMultiplier : 1,
            ExperienceMultiplier = derpy ? DerpyExperienceMultiplier : 1
        };

        var (prices, experience) = await LoadMarket();
        var productTags = minions.MinionData.Values
            .SelectMany(minion => minion.Products)
            .Where(product => product.Tag != null)
            .Select(product => product.Tag);
        var steps = await compactionService.GetAsync(productTags, prices.Keys.Where(t => t.StartsWith("ENCHANTED_")));
        var calculator = new MinionCalculator(prices, experience, steps);
        var ranked = calculator.Rank(minions.MinionData.Values, query)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(Describe)
            .ToList();
        return new MinionRankingResponse(ranked, DateTime.UtcNow);
    }

    private static MinionRanking Describe(MinionOutlook outlook) => new(
        outlook.Name,
        outlook.Tier,
        Math.Round(outlook.CoinsPerDay, 1),
        Math.Round(outlook.ExperiencePerDay, 1),
        Math.Round(outlook.SetupCost, 1),
        double.IsInfinity(outlook.PaybackDays) ? null : Math.Round(outlook.PaybackDays, 1),
        Math.Round(outlook.SecondsBetweenHarvests, 2),
        Math.Round(outlook.SecondsToFill / 3600, 2),
        outlook.StorageLimited,
        outlook.Compacted,
        outlook.MissingRequirements,
        outlook.UnpricedIngredients,
        outlook.ProductTags);

    private async Task<(Dictionary<string, MinionItemPrice> Prices, Dictionary<string, double> Experience)> LoadMarket()
    {
        var snipedTask = sniper.ApiSniperPricesCleanGetAsync();
        var itemsTask = items.GetItemsAsync();
        var prices = new Dictionary<string, MinionItemPrice>();
        foreach (var entry in await bazaar.GetAllPricesAsync())
            prices[entry.ProductId] = new MinionItemPrice(entry.BuyPrice, entry.SellPrice);
        // Auction items have one price rather than two sides, so both sides are that price.
        foreach (var entry in await snipedTask)
            if (!prices.ContainsKey(entry.Key))
                prices[entry.Key] = new MinionItemPrice(entry.Value, entry.Value);

        var experience = new Dictionary<string, double>();
        foreach (var (tag, item) in await itemsTask)
        {
            var perItem = item.Experience?.Values
                .Where(byAction => byAction.ContainsKey("MINION_STORAGE"))
                .Sum(byAction => byAction["MINION_STORAGE"]) ?? 0;
            if (perItem > 0)
                experience[tag] = perItem;
        }
        return (prices, experience);
    }

    private static MinionSellMode ParseSell(string value) => value?.ToLowerInvariant() switch
    {
        "offer" or null or "" => MinionSellMode.SellOffer,
        "instant" => MinionSellMode.InstaSell,
        "npc" => MinionSellMode.Npc,
        _ => throw new ArgumentException("sell has to be offer, instant or npc", nameof(value))
    };

    private static MinionBuyMode ParseBuy(string value) => value?.ToLowerInvariant() switch
    {
        "instant" or null or "" => MinionBuyMode.InstaBuy,
        "order" => MinionBuyMode.BuyOrder,
        _ => throw new ArgumentException("buy has to be instant or order", nameof(value))
    };

    private static MinionObjective ParseObjective(string value) => value?.ToLowerInvariant() switch
    {
        "coins" or null or "" => MinionObjective.Coins,
        "experience" or "exp" or "xp" => MinionObjective.Experience,
        _ => throw new ArgumentException("objective has to be coins or experience", nameof(value))
    };

    private static double? ParseHopper(string value) => value?.ToLowerInvariant() switch
    {
        "none" or null or "" => null,
        "budget" => BudgetHopperShare,
        "enchanted" => EnchantedHopperShare,
        _ => throw new ArgumentException("hopper has to be none, budget or enchanted", nameof(value))
    };
}

/// <summary>One minion's outlook under the requested situation.</summary>
/// <param name="Name">Display name of the minion</param>
/// <param name="Tier">Tier the figures are for; the best the budget reaches unless one was asked for</param>
/// <param name="CoinsPerDay">Coins per day after any recurring fuel cost</param>
/// <param name="ExperiencePerDay">Skill experience per day from collecting the produced items</param>
/// <param name="SetupCost">Coins to craft and upgrade to this tier, plus the upgrade items</param>
/// <param name="PaybackDays">Days of production to earn the setup cost back, null when it never does</param>
/// <param name="SecondsBetweenHarvests">Seconds per yield, which is twice the time between actions</param>
/// <param name="HoursToFill">Hours until storage is full and the minion stops</param>
/// <param name="StorageLimited">Whether storage fills before the player returns</param>
/// <param name="Compacted">Whether the figures assume a Super Compactor 3000</param>
/// <param name="MissingRequirements">Anything the recipe needs that coins cannot buy, such as pelts</param>
/// <param name="UnpricedIngredients">Ingredients with no bazaar listing, which make the setup cost a floor</param>
/// <param name="ProductTags">Item tags this minion produces</param>
public record MinionRanking(
    string Name,
    int Tier,
    double CoinsPerDay,
    double ExperiencePerDay,
    double SetupCost,
    double? PaybackDays,
    double SecondsBetweenHarvests,
    double HoursToFill,
    bool StorageLimited,
    bool Compacted,
    IReadOnlyList<string> MissingRequirements,
    IReadOnlyList<string> UnpricedIngredients,
    IReadOnlyList<string> ProductTags)
{
    /// <summary>
    /// Price pages for what this minion produces. These are the numbers the ranking moves with, and a
    /// generator tag cannot be derived from the display name reliably enough to link to.
    /// </summary>
    public IEnumerable<string> ItemPages => ProductTags.Select(tag => "https://sky.coflnet.com/item/" + tag);
}

/// <summary>The ranking and when it was produced.</summary>
/// <param name="Minions">Best first by the requested objective</param>
/// <param name="GeneratedAt">When the underlying prices were read</param>
public record MinionRankingResponse(IReadOnlyList<MinionRanking> Minions, DateTime GeneratedAt);

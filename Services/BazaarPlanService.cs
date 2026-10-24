using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Models.Bazaar;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Client;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Coflnet.Sky.Api.Services;

/// <summary>
/// Builds the personal bazaar plan of one user: validates the request, fills in smart defaults, asks the fleet
/// planner for a single trader preview and maps its answer one to one. No plan numbers are derived here.
/// </summary>
public class BazaarPlanService
{
    /// <summary>Order slots a player has on the bazaar.</summary>
    public const int TotalOrderSlots = 21;
    /// <summary>Longest allowed investing horizon in hours.</summary>
    public const double MaxInvestingHorizonHours = 48;
    /// <summary>Link where Starter Premium can be bought.</summary>
    public const string PremiumUrl = "https://sky.coflnet.com/premium";
    private const int MaxOrders = 10;
    private const double PurseShare = 2.0 / 3.0;
    private const double MaxSellTaxRate = 0.1;
    private const string NamesCacheKey = "bazaarplan:itemnames";
    private static readonly TimeSpan NamesCacheTime = TimeSpan.FromMinutes(10);

    private readonly IFleetApi fleetApi;
    private readonly IItemsApi itemsApi;
    private readonly IVerifiedAccountResolver accounts;
    private readonly IBazaarUserOrders orders;
    private readonly IPurseLoader purse;
    private readonly IMemoryCache cache;
    private readonly ILogger<BazaarPlanService> logger;

    /// <summary>Creates the service.</summary>
    public BazaarPlanService(IFleetApi fleetApi, IItemsApi itemsApi, IVerifiedAccountResolver accounts,
        IBazaarUserOrders orders, IPurseLoader purse, IMemoryCache cache, ILogger<BazaarPlanService> logger)
    {
        this.fleetApi = fleetApi;
        this.itemsApi = itemsApi;
        this.accounts = accounts;
        this.orders = orders;
        this.purse = purse;
        this.cache = cache;
        this.logger = logger;
    }

    /// <summary>Computes the plan of the user.</summary>
    /// <exception cref="CoflnetException">invalid_modes, invalid_horizon, budget_required, no_verified_account, invalid_plan_request or plan_unavailable</exception>
    /// <param name="user">The caller</param>
    /// <param name="tier">Tier of the caller</param>
    /// <param name="req">Overrides</param>
    /// <param name="userId">Id used for quote ownership and sampling</param>
    /// <param name="forTracking">Paid tiers get the full plan, free tiers the same sample as on the plan route; no quote is handed out</param>
    public async Task<BazaarPlanResponse> Plan(GoogleUser user, string tier, BazaarPlanRequest req, string userId, bool forTracking = false)
    {
        req ??= new BazaarPlanRequest();
        var modes = CanonicalModes(req.Modes);
        var investing = modes.Contains("Investing");
        double? horizon = null;
        if (investing)
        {
            if (req.InvestingHorizonHours is not { } h || double.IsNaN(h) || h <= 0 || h > MaxInvestingHorizonHours)
                throw new CoflnetException("invalid_horizon", $"The investing horizon must be greater than 0 and at most {MaxInvestingHorizonHours} hours.");
            horizon = h;
        }

        if (req.ActiveHorizonHours is { } ah && (double.IsNaN(ah) || double.IsInfinity(ah) || ah <= 0))
            throw new CoflnetException("invalid_horizon", "The active horizon must be a finite number of hours greater than 0.");
        if (req.SellTaxRate is { } tax && (double.IsNaN(tax) || tax < 0 || tax > MaxSellTaxRate))
            throw new CoflnetException("invalid_tax", $"The sell tax rate must be between 0 and {MaxSellTaxRate.ToString(CultureInfo.InvariantCulture)}.");
        if (req.BudgetCoins is { } b && (double.IsNaN(b) || double.IsInfinity(b) || b < 0))
            throw new CoflnetException("invalid_budget", "The budget must be a finite number of coins, 0 or more.");

        string uuid, name;
        try
        {
            uuid = await accounts.GetVerifiedUuid(user);
            name = await accounts.GetName(uuid);
        }
        catch (Exception e) when (e is not CoflnetException)
        {
            throw Unavailable(e, "Account lookup failed for plan of user {UserId}", userId);
        }
        var warnings = new List<string>();

        List<BazaarPlayerOrder> open;
        var ordersEstimated = false;
        var ordersKnown = true;
        try
        {
            if (string.IsNullOrEmpty(name))
                throw new InvalidOperationException("player name unknown");
            open = await orders.Get(userId, name) ?? new();
            ordersEstimated = open.Any(o => o.IsEstimate);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Open bazaar orders unavailable for plan of user {UserId}", userId);
            open = new();
            ordersEstimated = true;
            ordersKnown = false;
            warnings.Add("orders_unavailable");
        }

        var live = open.Where(o => !o.IsExpired).ToList();
        var openBuys = live.Where(o => !o.IsSell && o.Amount - o.FilledAmount > 0).ToList();
        var commitment = openBuys.Sum(o => (double)(o.Amount - o.FilledAmount) * o.PricePerUnit);
        var (budget, planBudget, budgetSource) = await ResolveBudget(req.BudgetCoins, name, commitment, userId);
        var held = HeldExposure(live);
        var freeSlots = ordersKnown ? Math.Max(0, TotalOrderSlots - live.Count) : (int?)null;
        var openTags = openBuys.Select(o => o.ItemTag).Distinct().ToList();

        var request = new FleetAllocateRequest(
            mode: "Fast", capacityShare: 1, targetHoldTimeHours: 1,
            instanceId: "preview:skyapi", requestId: Guid.NewGuid().ToString(), preview: true, includeUnqualifiedInvesting: true,
            traders: new List<FleetTraderRequest>
            {
                new(id: uuid, priority: TierRank(tier), budgetCoins: planBudget, maxItems: Math.Min(MaxOrders, freeSlots ?? MaxOrders),
                    isBot: false, strategies: modes, investingHorizonHours: horizon, activeHorizonHours: req.ActiveHorizonHours ?? 1,
                    sellTaxRate: req.SellTaxRate, sellTaxSource: req.SellTaxRate.HasValue ? "AccountAttested" : "Assumption",
                    accountState: new FleetAccountStateDto(
                        stateAsOf: DateTime.UtcNow,
                        revision: open.Count == 0 ? null : open.Max(o => o.Created).ToString("O", CultureInfo.InvariantCulture),
                        openBuyItemTags: openTags, heldExposure: held, freeOrderSlots: freeSlots, reserveOwner: "Caller"))
            });

        var response = await Allocate(request);
        var assignment = response.Assignments?.FirstOrDefault(a => a.TraderId == uuid) ?? response.Assignments?.FirstOrDefault()
            ?? throw new CoflnetException("plan_unavailable", "The planner returned no allocation, please try again shortly.");

        var names = await LoadNames();
        var result = new BazaarPlanResponse
        {
            Quote = MapQuote(response.Quote),
            Status = response.Status,
            InvestingStatus = assignment.InvestingStatus,
            InvestingReasonCodes = assignment.InvestingReasonCodes ?? new(),
            Portfolio = MapPortfolio(assignment.Portfolio),
            Orders = (assignment.Orders ?? new()).Select(o => MapOrder(o, names)).ToList(),
            UnqualifiedInvestingCandidates = (assignment.UnqualifiedInvestingCandidates ?? new()).Select(o => MapOrder(o, names)).ToList(),
            Modes = modes,
            InvestingHorizonHours = horizon,
            BudgetCoins = budget,
            BudgetSource = budgetSource,
            AccountState = new BazaarPlanAccountState
            {
                OpenOrdersEstimated = ordersEstimated,
                OpenBuyItemTags = openTags,
                HeldItemTags = held.Select(h => h.ItemTag).ToList(),
                FreeOrderSlots = freeSlots,
                Revision = request.Traders[0].AccountState.Revision
            },
            Tier = tier,
            Warnings = warnings,
            HiddenCount = 0
        };

        if (!IsPaid(tier))
            ApplyFreeTier(result, userId);
        if (forTracking)
            return result;
        if (result.Quote != null)
            cache.Set(CacheKey(userId, result.Quote.QuoteId), result, new DateTimeOffset(AsUtc(result.Quote.ExpiresAt)));
        return result;
    }

    /// <summary>The plan response a quote was handed out with, null when it expired or is unknown.</summary>
    public BazaarPlanResponse GetQuote(string userId, string quoteId)
    {
        if (string.IsNullOrEmpty(quoteId) || !cache.TryGetValue(CacheKey(userId, quoteId), out BazaarPlanResponse plan))
            return null;
        return AsUtc(plan.Quote.ExpiresAt) > DateTime.UtcNow ? plan : null;
    }

    /// <summary>Normalizes a timestamp to UTC; an unspecified kind is taken as UTC, like the planner sends it.</summary>
    public static DateTime AsUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc)
    };

    private CoflnetException Unavailable(Exception e, string template, string userId)
    {
        logger.LogError(e, template, userId);
        return new CoflnetException("plan_unavailable", "The plan is currently unavailable, please try again shortly.");
    }

    /// <summary>Whether the tier includes at least Starter Premium.</summary>
    public static bool IsPaid(string tier) => tier is "starter_premium" or "premium" or "premium_plus";

    private static string CacheKey(string userId, string quoteId) => $"bazaarplan:{userId}:{quoteId}";

    private static int TierRank(string tier) => tier switch
    {
        "premium_plus" => 3,
        "premium" => 2,
        "starter_premium" => 1,
        _ => 0
    };

    private static List<string> CanonicalModes(List<string> requested)
    {
        if (requested == null)
            return new() { "Active" };
        var result = new List<string>();
        foreach (var mode in requested)
        {
            var canonical = mode?.Trim().ToLowerInvariant() switch
            {
                "active" => "Active",
                "investing" => "Investing",
                _ => throw new CoflnetException("invalid_modes", $"Unknown mode '{mode}', use active and/or investing.")
            };
            if (result.Contains(canonical))
                throw new CoflnetException("invalid_modes", $"Mode '{canonical}' was given more than once.");
            result.Add(canonical);
        }
        if (result.Count == 0)
            throw new CoflnetException("invalid_modes", "Give at least one mode, active and/or investing.");
        return result;
    }

    /// <summary>
    /// Resolves the budget shown to the user and the coins the planner may spend. The purse is already net of the coins
    /// placed in open BUY orders, so those must never be subtracted from a purse based figure a second time.
    /// <list type="bullet">
    /// <item>purse-estimate (no budget, or a budget of 0, given): cap = floor(2/3 * (purse + openBuyCommitment)), the two
    /// thirds apply to the total bazaar exposure. spendable = min(cap - openBuyCommitment, purse), never below 0. The
    /// purse is already net of placed buys so it is never reduced by them again. SkyApi knows no unplaced intents.</item>
    /// <item>request (explicit budget): the budget is the user's total cap including coins already in open BUY orders,
    /// spendable = min(budget - openBuyCommitment, purse) when the purse is known, else budget - openBuyCommitment,
    /// never below 0.</item>
    /// </list>
    /// </summary>
    private async Task<(double budget, double spendable, string source)> ResolveBudget(double? requested, string playerName, double openBuyCommitment, string userId)
    {
        double? coins;
        try
        {
            coins = await purse.LoadPurse(playerName);
        }
        catch (Exception e) when (e is not CoflnetException)
        {
            throw Unavailable(e, "Purse lookup failed for plan of user {UserId}", userId);
        }
        if (requested is > 0)
        {
            var spendable = requested.Value - openBuyCommitment;
            if (coins is > 0)
                spendable = Math.Min(spendable, coins.Value);
            return (requested.Value, Math.Max(0, spendable), "request");
        }
        if (coins is > 0)
        {
            var cap = Math.Floor((coins.Value + openBuyCommitment) * PurseShare);
            return (cap, Math.Max(0, Math.Min(cap - openBuyCommitment, coins.Value)), "purse-estimate");
        }
        throw new CoflnetException("budget_required", "Your purse is unknown, please set a budget.");
    }

    private static List<HeldExposureDto> HeldExposure(List<BazaarPlayerOrder> live)
    {
        var held = new Dictionary<string, (double units, double cost)>();
        void Add(string tag, double units, double price)
        {
            if (units <= 0)
                return;
            held.TryGetValue(tag, out var current);
            held[tag] = (current.units + units, current.cost + units * price);
        }
        foreach (var order in live)
        {
            if (order.IsSell)
                Add(order.ItemTag, order.Amount - order.FilledAmount, order.PricePerUnit);
            else
                Add(order.ItemTag, order.FilledAmount - (order.ClaimedAmount ?? 0), order.PricePerUnit);
        }
        return held.Select(h => new HeldExposureDto(h.Key, h.Value.units, h.Value.cost)).ToList();
    }

    private async Task<FleetAllocateResponse> Allocate(FleetAllocateRequest request)
    {
        try
        {
            return await fleetApi.FleetAllocatePostAsync(request)
                ?? throw new CoflnetException("plan_unavailable", "The planner returned an empty answer, please try again shortly.");
        }
        catch (ApiException e) when (e.ErrorCode == 400)
        {
            throw new CoflnetException("invalid_plan_request", RejectionMessage(e));
        }
        catch (CoflnetException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Bazaar fleet planner failed");
            throw new CoflnetException("plan_unavailable", "The planner is currently unavailable, please try again shortly.");
        }
    }

    private static string RejectionMessage(ApiException e)
    {
        try
        {
            var errors = JObject.Parse(e.ErrorContent?.ToString() ?? "")["errors"];
            var text = errors switch
            {
                JArray array => string.Join(", ", array.Select(a => a.ToString())),
                JObject obj => string.Join(", ", obj.Properties().Select(p => p.Name)),
                _ => null
            };
            if (!string.IsNullOrEmpty(text))
                return "The planner rejected the request: " + text;
        }
        catch (Exception)
        {
            // fall through to the generic message
        }
        return "The planner rejected the request.";
    }

    private async Task<Dictionary<string, string>> LoadNames()
    {
        try
        {
            if (cache.TryGetValue(NamesCacheKey, out Dictionary<string, string> cached))
                return cached;
            var loaded = (await itemsApi.ItemNamesGetAsync()).GroupBy(i => i.Tag).ToDictionary(g => g.Key, g => g.First().Name);
            if (loaded.Count > 0)
                cache.Set(NamesCacheKey, loaded, NamesCacheTime);
            return loaded;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Item names unavailable for bazaar plan");
            return new();
        }
    }

    private void ApplyFreeTier(BazaarPlanResponse result, string userId)
    {
        var all = result.Orders;
        var keep = FreeTierPlanSampler.SampleIndices(all.Count, userId, DateTime.UtcNow);
        result.Orders = keep.Select(i => all[i]).ToList();
        result.HiddenCount = all.Count - result.Orders.Count;
        result.PremiumUrl = result.HiddenCount > 0 ? PremiumUrl : null;
        result.UnqualifiedInvestingCandidates = new();
        if (result.HiddenCount == 0)
            return;

        // totals and investing state describe the visible rows only, otherwise they reveal the hidden ones
        var visible = result.Orders;
        if (result.Portfolio != null)
        {
            var spent = visible.Where(o => !o.IsSell).Sum(o => o.PricePerUnit * o.Amount);
            result.Portfolio.SpentCoins = spent;
            result.Portfolio.RemainingBudgetCoins = Math.Max(0, result.Portfolio.BudgetCoins - spent);
            result.Portfolio.FreeSlotsAfter = Math.Max(0, result.Portfolio.FreeSlotsBefore - visible.Count);
        }
        if (all.Any(o => o.Mode == "Investing"))
        {
            result.InvestingStatus = visible.Any(o => o.Mode == "Investing") ? "Allocated" : "EmptyQualified";
            result.InvestingReasonCodes = new();
        }
    }

    private static BazaarPlanQuote MapQuote(FleetQuoteDto q) => q == null ? null : new()
    {
        QuoteId = q.QuoteId, RequestId = q.RequestId, MarketFrameId = q.MarketFrameId, GeneratedAt = q.GeneratedAt,
        ExpiresAt = q.ExpiresAt, PolicyId = q.PolicyId, PolicyVersion = q.PolicyVersion, ModelVersion = q.ModelVersion,
        EngineVersion = q.EngineVersion, AdmissionRule = q.AdmissionRule
    };

    private static BazaarPlanPortfolio MapPortfolio(PortfolioSummaryDto p) => p == null ? null : new()
    {
        BudgetCoins = p.BudgetCoins, SpentCoins = p.SpentCoins, RemainingBudgetCoins = p.RemainingBudgetCoins,
        ReserveCoins = p.ReserveCoins, ReserveOwner = p.ReserveOwner, FreeSlotsBefore = p.FreeSlotsBefore,
        FreeSlotsAfter = p.FreeSlotsAfter, FreeInventorySlotsBefore = p.FreeInventorySlotsBefore,
        FreeInventorySlotsAfter = p.FreeInventorySlotsAfter, HeldExposureCoins = p.HeldExposureCoins,
        SuppressedItemTags = p.SuppressedItemTags ?? new()
    };

    private static BazaarPlanOrder MapOrder(OrderRecommendationDto o, Dictionary<string, string> names) => new()
    {
        ItemTag = o.ItemTag, ItemName = names.GetValueOrDefault(o.ItemTag) ?? o.ItemTag, IsSell = o.IsSell,
        PricePerUnit = o.PricePerUnit, Amount = o.Amount, Mode = o.Mode, OriginalQuantity = o.OriginalQuantity,
        EntryPrice = o.EntryPrice, ExitPrice = o.ExitPrice, SellTaxRate = o.SellTaxRate, TaxSource = o.TaxSource,
        EstimatedNetProfit = o.EstimatedNetProfit, CapitalLockHorizonHours = o.CapitalLockHorizonHours,
        DeadlineSemantics = o.DeadlineSemantics,
        Confidence = o.Confidence == null ? null : new BazaarPlanConfidence
        {
            EventId = o.Confidence.EventId, Probability = o.Confidence.Probability, LowerBound = o.Confidence.LowerBound,
            ConfidenceLevel = o.Confidence.ConfidenceLevel, SupportDomainId = o.Confidence.SupportDomainId,
            ModelId = o.Confidence.ModelId, CalibrationId = o.Confidence.CalibrationId, Status = o.Confidence.Status,
            ReasonCodes = o.Confidence.ReasonCodes ?? new()
        }
    };
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Coflnet.Sky.Api.Services.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace SkyApi.Services.Tasks.Setups.Measured;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary>Builders for gear models, catalog pieces and priced pieces shared by the setups tests</summary>
public static class MeasuredTestData
{
    public const string FarmingFortune = "Farming Fortune";

    public static GearPiece Piece(string tag, Dictionary<string, int> enchants = null, string rarity = "LEGENDARY")
        => new() { Tag = tag, Rarity = rarity, Enchants = enchants ?? new(), Gems = new(), Parts = new(), Counters = new(), Applied = new(), Stats = new() };

    /// <summary>A catalog item with the id "slot:TAG"</summary>
    public static GearCatalogPiece Entry(string slot, string tag, double contribution, int players = 6, GearPiece item = null,
        params GearPieceEffect[] effects) => new()
    {
        Id = $"{slot}:{tag}",
        Slot = slot,
        Item = item ?? Piece(tag),
        ScoreContribution = contribution,
        UsedAtScorePercentile = 50,
        Effects = effects.ToList(),
        Players = players,
        PlayersLabel = players < 5 ? "2+" : players.ToString(),
        Rounded = players < 5,
    };

    public static GearCatalogPiece PetEntry(string type, double contribution, int players = 6) => new()
    {
        Id = $"pet:{type}",
        Slot = "pet",
        Pet = new GearPet { Type = type, Tier = "LEGENDARY", Level = 100, Stats = new() },
        ScoreContribution = contribution,
        Effects = new(),
        Players = players,
        PlayersLabel = players.ToString(),
    };

    /// <summary>The per piece share (<paramref name="value"/> Farming Fortune) of the full set bonus <paramref name="setId"/></summary>
    public static GearPieceEffect SetShare(string setId, double value, string mode = "add") => new()
    {
        Id = setId, Kind = "set", Mode = mode, Stats = [FarmingFortune], Value = value, SetShare = true, Confidence = "medium", Source = "https://wiki.example",
    };

    /// <summary>Score = Farming Fortune</summary>
    public static GearScoreFormulaInfo Formula(string kind = BuildComposer.WeightedSum) => new()
    {
        Kind = kind, Weights = new Dictionary<string, double> { [FarmingFortune] = 1 }, Expression = "1 × Farming Fortune", EffectCount = 3,
    };

    /// <summary>Curve through the (score, common rate) points at the percentiles 10, 25, 50, 75, 90, observed range = the outer points unless given</summary>
    public static GearRateCurve Curve(IReadOnlyList<(double Score, double Common)> points, double rare = 0, double? min = null, double? max = null,
        int players = 8)
    {
        int[] percentiles = [10, 25, 50, 75, 90];
        return new GearRateCurve
        {
            Players = players,
            RareCoinsPerHour = rare,
            Points = points.Select((p, i) => new GearCurvePoint
            {
                ScorePercentile = percentiles[Math.Min(i, percentiles.Length - 1)], Score = p.Score, CommonCoinsPerHour = p.Common, CoinsPerHour = p.Common + rare,
            }).ToList(),
            ScoreMin = min ?? points[0].Score,
            ScoreMax = max ?? points[^1].Score,
            Method = "theil-sen",
        };
    }

    public static GearScoreSummary Distribution(double p10, double p25, double p50, double p75, double p90, int players = 8) => new()
    {
        Players = players, PlayerBuilds = players, Hours = 40,
        ScoreP10 = p10, ScoreP25 = p25, ScoreP50 = p50, ScoreP75 = p75, ScoreP90 = p90,
    };

    public static GearLateGame LateGame(double rate, int players = 6, double hours = 30, bool rounded = false) => new()
    {
        ScoreFrom = 400,
        Players = players,
        PlayersLabel = rounded ? "2+" : players.ToString(),
        Hours = hours,
        CommonCoinsPerHour = rate * 0.9,
        RareCoinsPerHour = rate * 0.1,
        CoinsPerHour = rate,
        CommonP25 = rounded ? null : rate * 0.8,
        CommonP75 = rounded ? null : rate * 1.1,
        Confidence = rounded ? "medium" : "high",
        Rounded = rounded,
        StatMedians = new Dictionary<string, double> { [FarmingFortune] = 1234 },
    };

    public static GearRecommendedBuild Recommended(double score, double rate, params GearCatalogPiece[] pieces) => new()
    {
        Pieces = pieces.ToDictionary(p => p.Slot, p => p.Id),
        Score = score,
        ScorePercentile = 97,
        CoinsPerHour = rate,
        Basis = RateBases.Curve,
        Effects = new(),
    };

    public static TaskGearModel Model(TaskGearSetups estimates, GearRateCurve curve, IEnumerable<GearCatalogPiece> pieces,
        GearRecommendedBuild endgame = null, GearLateGame lateGame = null, GearScoreSummary distribution = null) => new()
    {
        TaskName = "Task A",
        Category = "farming",
        ComputedAt = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc),
        WindowWeeks = 12,
        MinPlayers = 2,
        MinHours = 1,
        ScoreFormula = Formula(),
        Distribution = distribution,
        LateGame = lateGame,
        Curve = curve,
        Pieces = pieces.ToList(),
        RecommendedEndgame = endgame,
        ModifierEffects = new(),
        Estimates = estimates,
    };

    public static AcquisitionResult Direct(long price, string uuid = null)
    {
        uuid ??= Guid.NewGuid().ToString("N");
        return new AcquisitionResult(new DirectPurchase(uuid, price, "/viewauction " + uuid), null, null, AcquisitionReasons.NoBase,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);
    }

    public static ItemResaleQuote Quote(string tag, double? buy, double? resale = null)
        => new(tag, buy, buy, resale ?? buy * 0.9, 0.02, 0, buy, 20, StabilityLabel.Stable);

    /// <summary>A priced catalog piece costing <paramref name="cost"/> (null = unknown), bought directly</summary>
    public static PricedPiece Priced(GearCatalogPiece entry, double? cost, bool lowerBound = false, bool transient = false, bool noMarket = false,
        string uuid = null)
    {
        var piece = MeasuredPiece.Of(entry);
        var found = cost == null ? null : Direct((long)cost.Value, uuid);
        return new PricedPiece(piece, found, Quote(piece.Tag, cost), cost, cost == null ? null : CostSources.Direct, noMarket, lowerBound, transient);
    }

    public static PricedGearModel PricedModel(TaskGearModel model, params PricedPiece[] pieces)
        => new(model, pieces.ToDictionary(p => p.Piece.Id));

    /// <summary>A model service that does not know any task, so the setups come from the gear setups list</summary>
    public static IGearModelService NoModel() => Returning(GearModelLookup.NotFound);

    public static IGearModelService Returning(GearModelLookup lookup)
    {
        var models = new Mock<IGearModelService>();
        models.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(lookup);
        return models.Object;
    }

    /// <summary>Acquisition that finds a direct buy at the price <paramref name="price"/> gives for the tag (null = nothing listed)</summary>
    public static Mock<IGearAcquisitionService> Acquisition(Func<string, long?> price)
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GearItemSpec spec, CancellationToken _) => ResultFor(spec.Tag, price));
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearPetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GearPetSpec pet, CancellationToken _) => ResultFor("PET_" + pet.Type, price));
        return acquisition;
    }

    private static AcquisitionResult ResultFor(string tag, Func<string, long?> price)
    {
        var value = price(tag);
        if (value == null)
            return new AcquisitionResult(null, AcquisitionReasons.NoListing, null, AcquisitionReasons.NoBase,
                new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        return Direct(value.Value, ((long)tag.GetHashCode() & 0xffffff).ToString("x32"));
    }

    public static TaskSetupsService Service(IPublicTaskService tasks, IGearSetupService gear, IItemResaleService resale,
        IMemoryCache cache = null, IGearModelService models = null, IGearAcquisitionService acquisition = null, TimeProvider time = null)
        => new(tasks, gear, models ?? NoModel(), new MeasuredSetupPricer(acquisition ?? Acquisition(_ => null).Object, resale),
            resale, cache ?? new MemoryCache(new MemoryCacheOptions()), time ?? TimeProvider.System);

    /// <summary>Estimates with a baseline at 1M/h, budget 1.01M, mid 3M and endgame 6M (modelled), community rate 1M</summary>
    public static TaskGearSetups RisingEstimates()
    {
        var estimates = PublicSetupsMapperTests.Gear(
            PublicSetupsMapperTests.Setup("baseline", "baseline", 0, PublicSetupsMapperTests.Item("OLD")),
            PublicSetupsMapperTests.Setup("budget", "upgrade", 10_000, PublicSetupsMapperTests.Item("B")),
            PublicSetupsMapperTests.Setup("mid", "upgrade", 2_000_000, PublicSetupsMapperTests.Item("M")),
            PublicSetupsMapperTests.Setup("endgame", "upgrade", 5_000_000, PublicSetupsMapperTests.Item("E")));
        estimates.CommunityCoinsPerHour = 1_000_000;
        return estimates;
    }
}

/// <summary>
/// One task whose model gives every tier a measured build, the mid and endgame tiers carrying sentinel numbers:
/// community 1M/h; budget = BUDGET_HOE (5M, score 250, 1.5M/h); mid = BUDGET_HOE + MID_HELMET (score 350, 7.77M/h);
/// endgame = END_HOE + MID_HELMET (444M + 55M, 8.88M/h) with its late game cohort of 4321 players and 9876.5 hours.
/// </summary>
public static class FourTierModel
{
    public const string MidUuid = "11111111111111111111111111111111";
    public const string EndUuid = "22222222222222222222222222222222";
    public const string BudgetUuid = "33333333333333333333333333333333";
    public const string BaseUuid = "77777777777777777777777777777777";
    public const double CommunityRate = 1_000_000;
    public const double BudgetRate = 1_500_000;
    public const double MidRate = 7_770_000;
    public const double EndRate = 8_880_000;
    public const long BudgetCost = 5_000_000;
    public const long MidCost = 55_555_555;
    public const long EndCost = 444_444_444;
    public const int SecretPlayers = 4321;
    public const double SecretHours = 9876.5;
    public const double SecretStat = 31337;

    public static GearCatalogPiece BaseHoe() => MeasuredTestData.Entry("tool", "BASE_HOE", 150);
    public static GearCatalogPiece BudgetHoe() => MeasuredTestData.Entry("tool", "BUDGET_HOE", 250);
    public static GearCatalogPiece MidHelmet() => MeasuredTestData.Entry("helmet", "MID_HELMET", 100);
    public static GearCatalogPiece EndHoe() => MeasuredTestData.Entry("tool", "END_HOE", 400,
        item: MeasuredTestData.Piece("END_HOE", new Dictionary<string, int> { ["harvesting"] = 6 }));

    /// <summary>Price and auction of every tag of the model</summary>
    public static readonly Dictionary<string, (long Price, string Uuid)> Prices = new()
    {
        ["BASE_HOE"] = (500_000, BaseUuid),
        ["BUDGET_HOE"] = (BudgetCost, BudgetUuid),
        ["MID_HELMET"] = (MidCost, MidUuid),
        ["END_HOE"] = (EndCost, EndUuid),
    };

    public static TaskGearModel Model(TaskGearSetups estimates = null)
    {
        var curve = MeasuredTestData.Curve([(100, 1_000_000), (250, BudgetRate), (300, 3_000_000), (350, MidRate), (450, 8_000_000)]);
        var lateGame = MeasuredTestData.LateGame(8_000_000, players: SecretPlayers, hours: SecretHours);
        lateGame.StatMedians = new Dictionary<string, double> { [MeasuredTestData.FarmingFortune] = SecretStat };
        var endHoe = EndHoe();
        var midHelmet = MidHelmet();
        var endgame = MeasuredTestData.Recommended(500, EndRate, endHoe, midHelmet);
        endgame.Extrapolated = true;
        var model = MeasuredTestData.Model(estimates, curve, [endHoe, BudgetHoe(), BaseHoe(), midHelmet], endgame, lateGame,
            MeasuredTestData.Distribution(100, 250, 300, 350, 450));
        model.ModifierEffects = TaskSetupsComposerTests.Effects;
        return model;
    }

    public static PricedGearModel Priced(TaskGearModel model = null)
    {
        model ??= Model();
        var pieces = model.Pieces.Select(p => MeasuredTestData.Priced(p, Prices[p.Item.Tag].Price, uuid: Prices[p.Item.Tag].Uuid)).ToArray();
        return MeasuredTestData.PricedModel(model, pieces);
    }

    /// <summary>Acquisition with the prices and auctions of <see cref="Prices"/></summary>
    public static Mock<IGearAcquisitionService> Acquisition()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GearItemSpec spec, CancellationToken _) => Prices.TryGetValue(spec.Tag, out var p)
                ? MeasuredTestData.Direct(p.Price, p.Uuid)
                : MeasuredTestData.Direct(1_000_000, "66666666666666666666666666666666"));
        return acquisition;
    }
}

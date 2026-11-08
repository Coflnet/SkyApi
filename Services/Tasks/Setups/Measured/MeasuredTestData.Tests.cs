using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace SkyApi.Services.Tasks.Setups.Measured;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/// <summary>Builders for measured clusters and priced clusters shared by the setups tests</summary>
public static class MeasuredTestData
{
    public static GearPiece Piece(string tag, Dictionary<string, int> enchants = null, string rarity = "LEGENDARY")
        => new() { Tag = tag, Rarity = rarity, Enchants = enchants ?? new(), Gems = new(), Parts = new(), Counters = new(), Applied = new(), Stats = new() };

    /// <summary>A cluster with a tool and a helmet, key "L{level}|armor=...|tool=..." plus extra key parts</summary>
    public static GearCluster Cluster(string tool, double rate, int players = 6, int level = 1, string helmet = "HELMET_A",
        string extraKey = null, bool rounded = false, GearPiece toolPiece = null)
    {
        var key = $"L{level}|armor={helmet},-,-,-|tool={tool}" + (extraKey == null ? "" : "|" + extraKey);
        return new GearCluster
        {
            Basis = "measured",
            Key = key,
            Level = level,
            Players = players,
            Observations = players * 3,
            Hours = players * 2.5,
            CommonCoinsPerHour = rate * 0.9,
            RareCoinsPerHour = rate * 0.1,
            CoinsPerHour = rate,
            Confidence = players >= 5 ? "high" : "low",
            Rounded = rounded,
            Slots = new Dictionary<string, GearPiece> { ["tool"] = toolPiece ?? Piece(tool), ["helmet"] = Piece(helmet) },
            StatMedians = new Dictionary<string, double> { ["Farming Fortune"] = 1234 },
        };
    }

    public static AcquisitionResult Direct(long price, string uuid = null)
    {
        uuid ??= Guid.NewGuid().ToString("N");
        return new AcquisitionResult(new DirectPurchase(uuid, price, "/viewauction " + uuid), null, null, AcquisitionReasons.NoBase,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow);
    }

    public static ItemResaleQuote Quote(string tag, double? buy, double? resale = null)
        => new(tag, buy, buy, resale ?? buy * 0.9, 0.02, 0, buy, 20, StabilityLabel.Stable);

    /// <summary>
    /// A priced cluster whose items cost <paramref name="toolCost"/> and <paramref name="helmetCost"/> (null = unknown).
    /// <paramref name="toolLowerBound"/> and <paramref name="toolTransient"/> flag the tool's price.
    /// </summary>
    public static PricedCluster Priced(GearCluster cluster, double? toolCost, double? helmetCost = 0, bool toolLowerBound = false, bool toolTransient = false)
    {
        var pieces = MeasuredPiece.Of(cluster);
        return new PricedCluster(cluster, pieces.Select(p => p.Slot == "tool"
            ? PricedPieceOf(p, toolCost, toolLowerBound, toolTransient)
            : PricedPieceOf(p, helmetCost, false, false)).ToList());
    }

    private static PricedPiece PricedPieceOf(MeasuredPiece piece, double? cost, bool lowerBound, bool transient)
        => new(piece, null, Quote(piece.Tag, cost), cost, cost == null ? null : CostSources.Direct, false, lowerBound, transient);

    public static TaskGearClusters Clusters(TaskGearSetups estimates, params GearCluster[] clusters) => new()
    {
        TaskName = "Task A",
        Category = "farming",
        ComputedAt = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc),
        WindowWeeks = 12,
        MinPlayers = 2,
        MinHours = 1,
        Clusters = clusters.ToList(),
        ModifierEffects = new(),
        Estimates = estimates,
    };

    /// <summary>A cluster service that does not know any task, so the setups come from the gear setups list</summary>
    public static IGearClusterService NoClusters()
    {
        var clusters = new Mock<IGearClusterService>();
        clusters.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ClusterLookup.NotFound);
        return clusters.Object;
    }

    public static IGearClusterService Returning(ClusterLookup lookup)
    {
        var clusters = new Mock<IGearClusterService>();
        clusters.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(lookup);
        return clusters.Object;
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
        IMemoryCache cache = null, IGearClusterService clusters = null, IGearAcquisitionService acquisition = null, TimeProvider time = null)
        => new(tasks, gear, clusters ?? NoClusters(), new MeasuredSetupPricer(acquisition ?? Acquisition(_ => null).Object, resale),
            resale, cache ?? new MemoryCache(new MemoryCacheOptions()), time ?? TimeProvider.System);

    /// <summary>Estimates with a baseline at 1M/h, budget 1.01M, mid 3M and endgame 6M (modelled), community rate 1M</summary>
    public static TaskGearSetups RisingEstimates()
    {
        var estimates = SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Gear(
            SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Setup("baseline", "baseline", 0, SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Item("OLD")),
            SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Setup("budget", "upgrade", 10_000, SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Item("B")),
            SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Setup("mid", "upgrade", 2_000_000, SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Item("M")),
            SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Setup("endgame", "upgrade", 5_000_000, SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Item("E")));
        estimates.CommunityCoinsPerHour = 1_000_000;
        return estimates;
    }
}

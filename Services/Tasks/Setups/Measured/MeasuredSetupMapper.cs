using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>
/// Maps a measured tier to the public model. A locked setup keeps only its name, tier, basis and the item tags:
/// no confidence, rate, score, cost, player count, modifiers or acquisition. Stat medians are only in the details view.
/// </summary>
public static class MeasuredSetupMapper
{
    /// <summary>Player count shown (as "2+") for rounded numbers</summary>
    public const int RoundedPlayers = 2;
    private static readonly string[] ArmorSuffixes = ["_HELMET", "_CHESTPLATE", "_LEGGINGS", "_BOOTS"];
    private static readonly string[] ArmorSlots = ["helmet", "chestplate", "leggings", "boots"];

    /// <param name="open">True when the tier's numbers may be shown</param>
    /// <param name="details">True for the premium details view</param>
    public static PublicGearSetup Map(TierChoice choice, double? communityRate, bool open, bool details)
    {
        var build = choice.Measured;
        var setup = new PublicGearSetup
        {
            Locked = !open,
            Id = SetupId(choice.Tier),
            Name = SetupName(build),
            Tier = choice.Tier,
            Status = PublicSetupsMapper.StatusUpgrade,
            Basis = SetupBasis.Measured,
            Confidence = open ? build.Confidence : null,
            Items = build.Pieces.Select(p => MapItem(p, open)).ToList(),
        };
        if (!open)
            return setup;
        AddScore(setup, build);
        // the cohort describes the endgame players' gear, it never travels on another tier
        if (choice.Tier == SetupTierSelector.Endgame)
            AddLateGame(setup, build.LateGame, details);
        AddRate(setup, choice, communityRate);
        AddCosts(setup, build, choice.DeltaCoinsPerHour);
        return setup;
    }

    public const string LowConfidence = "low";
    /// <summary>Players from which medium or high confidence is shown, below every measurement is "low"</summary>
    public const int ExactFromPlayers = 5;

    /// <summary>Upstream says medium for 3 and 4 players, that would tell the rounded player count</summary>
    public static string ConfidenceOf(string confidence, int players, bool rounded)
        => rounded || players < ExactFromPlayers || string.IsNullOrEmpty(confidence) ? LowConfidence : confidence;

    /// <summary>Rounded numbers come from fewer than 5 players, the exact count is never shown then</summary>
    public static int DisplayedPlayers(int players, bool rounded) => rounded ? RoundedPlayers : players;

    public static string PlayersLabel(int players, bool rounded)
        => rounded ? RoundedPlayers + "+" : players.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Opaque id, stable per task and tier. Anything derived from the pieces (even a hash) would let a locked
    /// setup's modifiers be guessed by trying candidate pieces.
    /// </summary>
    public static string SetupId(string tier) => "measured-" + tier;

    /// <summary>Tool and armor set, eg. "Theoretical Hoe Wheat 3 + Fermento armor"</summary>
    public static string SetupName(PricedBuild build)
    {
        var tags = build.Pieces.Where(p => p.Piece.Pet == null)
            .GroupBy(p => p.Piece.Slot).ToDictionary(g => g.Key, g => g.First().Piece.Tag);
        var parts = new List<string>();
        if (tags.TryGetValue("tool", out var tool) && !string.IsNullOrEmpty(tool))
            parts.Add(Readable(tool));
        var armor = ArmorName(tags);
        if (armor != null)
            parts.Add(armor);
        return parts.Count == 0 ? "Measured setup" : string.Join(" + ", parts);
    }

    private static string ArmorName(Dictionary<string, string> tags)
    {
        var sets = ArmorSlots.Select(s => tags.GetValueOrDefault(s)).Where(t => !string.IsNullOrEmpty(t))
            .Select(ArmorSet).Distinct().ToList();
        if (sets.Count == 0)
            return null;
        return sets.Count == 1 ? Readable(sets[0]) + " armor" : "mixed armor";
    }

    private static string ArmorSet(string tag)
    {
        var suffix = ArmorSuffixes.FirstOrDefault(tag.EndsWith);
        return suffix == null ? tag : tag[..^suffix.Length];
    }

    private static string Readable(string tag)
        => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag.Replace('_', ' ').ToLowerInvariant());

    private static void AddScore(PublicGearSetup setup, PricedBuild build)
    {
        setup.Score = build.Score;
        setup.ScorePercentile = build.ScorePercentile;
        setup.RateBasis = build.RateBasis;
        setup.Extrapolated = build.Extrapolated;
        setup.Capped = build.Capped;
    }

    /// <summary>The endgame's measurement is its late game cohort; a composed build has no players of its own</summary>
    private static void AddLateGame(PublicGearSetup setup, GearLateGame cohort, bool details)
    {
        if (cohort == null)
            return;
        setup.Players = DisplayedPlayers(cohort.Players, cohort.Rounded);
        setup.PlayersAtLeast = cohort.Rounded;
        setup.PlayersLabel = PlayersLabel(cohort.Players, cohort.Rounded);
        setup.Hours = cohort.Hours;
        setup.CommonP25 = cohort.CommonP25;
        setup.CommonP75 = cohort.CommonP75;
        setup.LateGame = LateGameOf(cohort, details);
        if (details)
            setup.StatMedians = cohort.StatMedians ?? new();
    }

    private static PublicLateGame LateGameOf(GearLateGame cohort, bool details) => new()
    {
        ScoreFrom = cohort.ScoreFrom,
        Widened = cohort.Widened,
        Players = DisplayedPlayers(cohort.Players, cohort.Rounded),
        PlayersAtLeast = cohort.Rounded,
        PlayersLabel = PlayersLabel(cohort.Players, cohort.Rounded),
        Hours = cohort.Hours,
        CommonCoinsPerHour = cohort.CommonCoinsPerHour,
        RareCoinsPerHour = cohort.RareCoinsPerHour,
        CoinsPerHour = cohort.CoinsPerHour,
        CommonP25 = cohort.CommonP25,
        CommonP75 = cohort.CommonP75,
        Confidence = ConfidenceOf(cohort.Confidence, cohort.Players, cohort.Rounded),
        StatMedians = details ? cohort.StatMedians ?? new() : null,
    };

    private static void AddRate(PublicGearSetup setup, TierChoice choice, double? communityRate)
    {
        var rate = choice.Measured.Rate;
        setup.CoinsPerHour = rate;
        setup.DeltaCoinsPerHour = choice.DeltaCoinsPerHour;
        setup.Multiplier = communityRate is > 0 ? rate / communityRate : null;
    }

    /// <summary>Every item is bought (nothing is owned without a measured baseline), an untradeable item is never a cost</summary>
    private static void AddCosts(PublicGearSetup setup, PricedBuild build, double? delta)
    {
        var bought = build.Pieces.Where(p => !p.NoMarket).ToList();
        var quotes = bought.Select(p => (p.Quote ?? ItemResaleQuote.Unknown(p.Piece.Tag)) with { Buy = p.Cost });
        var resale = ResaleMath.Aggregate(quotes, new HashSet<string>(), delta);
        PublicSetupsMapper.AddCostNumbers(setup, resale, delta, false);
        // the cost to buy is known without a resale price, only the resale and payback numbers need one
        if (bought.All(p => p.Cost != null))
            setup.BuyCost = bought.Sum(p => p.Cost!.Value);
        setup.PriceLowerBound = bought.Any(p => p.LowerBound);
    }

    private static PublicGearItem MapItem(PricedPiece priced, bool open)
    {
        var item = new PublicGearItem { Slot = priced.Piece.Slot, ItemTag = priced.Piece.Tag, Tradeable = !priced.NoMarket };
        if (!open)
            return item;
        item.ModifiersMeasured = priced.Piece.ModifiersMeasured;
        item.Owned = false;
        item.Spec = SpecOf(priced.Piece);
        item.Acquisition = AcquisitionOf(priced);
        AddCatalogNumbers(item, priced.Piece.Catalog);
        AddItemNumbers(item, priced);
        return item;
    }

    private static void AddCatalogNumbers(PublicGearItem item, GearCatalogPiece entry)
    {
        if (entry == null)
            return;
        item.ScoreContribution = entry.ScoreContribution;
        // where a piece of fewer than 5 players is used would place those few players in the distribution
        item.UsedAtScorePercentile = entry.Rounded || entry.Players < ExactFromPlayers ? null : entry.UsedAtScorePercentile;
        item.Players = DisplayedPlayers(entry.Players, entry.Rounded);
        item.PlayersAtLeast = entry.Rounded;
        item.PlayersLabel = PlayersLabel(entry.Players, entry.Rounded);
    }

    private static void AddItemNumbers(PublicGearItem item, PricedPiece priced)
    {
        if (priced.NoMarket)
            return;
        var quote = priced.Quote ?? ItemResaleQuote.Unknown(priced.Piece.Tag);
        item.BuyPrice = priced.Cost;
        item.PriceLowerBound = priced.LowerBound;
        item.ResaleValue = quote.Resale;
        item.Stability = PublicSetupsMapper.Label(quote.Label);
        item.Cv = quote.Cv;
        item.TrendPerWeek = quote.TrendPerWeek;
        item.P10 = quote.P10;
        item.VolumePerDay = quote.VolumePerDay;
    }

    private static PublicGearSpec SpecOf(MeasuredPiece piece)
        => piece.Pet != null ? PetSpecOf(piece.Pet) : ItemSpecOf(piece.Piece);

    private static PublicGearSpec ItemSpecOf(GearPiece piece) => new()
    {
        Rarity = piece.Rarity,
        Reforge = piece.Reforge,
        Stars = piece.Stars,
        HotPotatoCount = piece.HotPotatoCount,
        Recombobulated = piece.Recombobulated,
        Enchants = piece.Enchants ?? new(),
        Gems = piece.Gems ?? new(),
        Parts = piece.Parts ?? new(),
        Counters = piece.Counters ?? new(),
        Applied = piece.Applied ?? new(),
        ItemType = piece.ItemType,
        Stats = piece.Stats ?? new(),
    };

    private static PublicGearSpec PetSpecOf(GearPet pet) => new()
    {
        Rarity = pet.Tier,
        PetLevel = pet.Level > 0 ? pet.Level : null,
        HeldItem = pet.HeldItem,
        Stats = pet.Stats ?? new(),
    };

    private static PublicAcquisition AcquisitionOf(PricedPiece priced)
    {
        var found = priced.Acquisition;
        if (found == null)
            return null;
        return new PublicAcquisition
        {
            Cost = priced.Cost,
            CostSource = priced.CostSource,
            Direct = found.Direct == null ? null : new PublicDirectPurchase
            {
                Price = found.Direct.Price, AuctionUuid = found.Direct.AuctionUuid, Command = found.Direct.Command,
            },
            DirectUnavailableReason = found.DirectUnavailableReason,
            CheapestBase = BaseOf(found.Base),
            BaseUnavailableReason = found.BaseUnavailableReason,
        };
    }

    private static PublicCheapestBase BaseOf(CheapestBase found)
    {
        if (found == null)
            return null;
        return new PublicCheapestBase
        {
            AuctionUuid = found.AuctionUuid,
            Command = found.AuctionUuid == null ? null : "/viewauction " + found.AuctionUuid,
            BasePrice = found.BasePrice,
            Upgrades = (found.Upgrades ?? []).Select(u => new PublicAcquisitionUpgrade
            {
                Kind = u.Kind, Name = u.Name, FromLevel = u.FromLevel, ToLevel = u.ToLevel,
                ItemTag = u.ItemTag, Amount = u.Amount, Cost = u.Cost, Priced = u.Priced,
            }).ToList(),
            Total = found.Total,
            GrindRequired = (found.GrindRequired ?? []).Select(g => new PublicGrindRequirement
            {
                Kind = g.Kind, Name = g.Name, Have = g.Have, Need = g.Need, Deficit = g.Deficit,
            }).ToList(),
            Incomplete = found.Incomplete,
        };
    }
}

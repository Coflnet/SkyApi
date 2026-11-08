using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using Coflnet.Sky.PlayerState.Client.Model;
using Moq;
using NUnit.Framework;
using static SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData;

namespace SkyApi.Services.Tasks.Setups.Measured;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class MeasuredSetupPricerTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UtcNow;

    private static CheapestBase Base(long total, bool incomplete = false, bool grind = false)
        => new(null, total / 2, [], total, grind ? [new GrindRequirement("counter", "farmed_cultivating", 0, 1000)] : [], incomplete);

    private static AcquisitionResult Found(long? direct, CheapestBase cheapestBase, string reason = AcquisitionReasons.NoListing)
    {
        var uuid = new string('a', 32);
        var purchase = direct == null ? null : new DirectPurchase(uuid, direct.Value, "/viewauction " + uuid);
        return new AcquisitionResult(purchase, direct == null ? reason : null, cheapestBase, cheapestBase == null ? reason : null,
            new Dictionary<string, string>(), At);
    }

    [Test]
    public void Direct_buy_wins_when_it_is_cheaper_than_the_complete_base()
        => MeasuredSetupPricer.CostOf(Found(100, Base(150)), Quote("X", 999)).Should().Be(((double?)100, CostSources.Direct));

    [Test]
    public void Complete_base_wins_when_it_is_cheaper()
        => MeasuredSetupPricer.CostOf(Found(200, Base(150)), Quote("X", 999)).Should().Be(((double?)150, CostSources.Base));

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Incomplete_or_grind_base_is_not_a_cost_and_the_market_price_is_used(bool incomplete, bool grind)
        => MeasuredSetupPricer.CostOf(Found(null, Base(10, incomplete, grind)), Quote("X", 999)).Should().Be(((double?)999, CostSources.Market));

    [Test]
    public void Nothing_priced_is_an_unknown_cost()
        => MeasuredSetupPricer.CostOf(Found(null, null), ItemResaleQuote.Unknown("X")).Should().Be(((double?)null, (string)null));

    /// <summary>A tool and a helmet of the catalog, as a measured build would have them</summary>
    private static List<MeasuredPiece> Build(string tool = "HOE", string helmet = "HELMET_A", GearPiece toolPiece = null)
        => [MeasuredPiece.Of(Entry("tool", tool, 100, item: toolPiece ?? Piece(tool))), MeasuredPiece.Of(Entry("helmet", helmet, 50))];

    private static PricedBuild AsBuild(PricedPieces priced, IEnumerable<MeasuredPiece> pieces)
        => new(pieces.Select(p => priced.Pieces[p.Id]).ToList(), 1, 1, null, RateBases.Curve, false, "low");

    private static async Task<PricedBuild> PriceOne(Func<string, long?> direct, Func<string, IReadOnlyDictionary<string, string>, ItemResaleQuote> quote,
        IReadOnlySet<string> untradeable = null)
    {
        var pricer = new MeasuredSetupPricer(MeasuredTestData.Acquisition(direct).Object, Resale(quote).Object);
        var pieces = Build();
        return AsBuild(await pricer.PriceAsync(pieces, untradeable), pieces);
    }

    /// <summary>Regression: an item without listings and sales counted as untradeable, so the setup looked free</summary>
    [Test]
    public async Task Item_without_any_price_is_not_free_without_evidence_it_is_untradeable()
    {
        var build = await PriceOne(tag => tag == "HOE" ? 1000 : null, (tag, _) => tag == "HOE" ? Quote(tag, 1000) : ItemResaleQuote.Unknown(tag));

        var helmet = build.Pieces.Single(p => p.Piece.Slot == "helmet");
        helmet.NoMarket.Should().BeFalse();
        helmet.Transient.Should().BeFalse();
        build.FullCost.Should().BeNull("the build cost is unknown, not 1000");
        build.TransientlyUnpriced.Should().BeFalse();
    }

    [Test]
    public async Task Item_the_profile_marks_untradeable_is_left_out_of_the_cost()
    {
        var build = await PriceOne(tag => tag == "HOE" ? 1000 : null, (tag, _) => tag == "HOE" ? Quote(tag, 1000) : ItemResaleQuote.Unknown(tag),
            new HashSet<string> { "HELMET_A" });

        build.Pieces.Single(p => p.Piece.Slot == "helmet").NoMarket.Should().BeTrue();
        build.FullCost.Should().Be(1000);
    }

    [Test]
    public async Task Plain_tag_market_price_is_a_flagged_lower_bound()
    {
        var filters = new Dictionary<string, string> { ["harvesting"] = ">5" };
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcquisitionResult(null, AcquisitionReasons.NoListing, null, AcquisitionReasons.NoBase, filters, At));
        var resale = Resale((tag, f) => f == null ? Quote(tag, 4000) : ItemResaleQuote.Unknown(tag));

        var pieces = Build();
        var build = AsBuild(await new MeasuredSetupPricer(acquisition.Object, resale.Object).PriceAsync(pieces), pieces);

        build.Pieces.Should().OnlyContain(p => p.LowerBound && p.CostSource == CostSources.Market);
        build.LowerBound.Should().BeTrue();
    }

    [Test]
    public async Task Market_price_for_a_base_that_needs_grinding_is_a_lower_bound()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Found(null, Base(10, grind: true)));

        var priced = await new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 999)).Object).PriceAsync(Build());

        priced.Pieces.Values.Should().OnlyContain(p => p.LowerBound && p.Cost == 999);
    }

    [Test]
    public async Task Failed_lookup_without_a_price_makes_the_build_transiently_unpriced()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Found(null, null, AcquisitionReasons.Timeout));

        var pieces = Build();
        var priced = await new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => ItemResaleQuote.Unknown(tag)).Object).PriceAsync(pieces);

        AsBuild(priced, pieces).TransientlyUnpriced.Should().BeTrue();
        priced.Degraded.Should().BeTrue();
    }

    private static Mock<IItemResaleService> Resale(Func<string, IReadOnlyDictionary<string, string>, ItemResaleQuote> quote)
    {
        var resale = new Mock<IItemResaleService>();
        resale.Setup(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tag, IReadOnlyDictionary<string, string> filters, CancellationToken _) => quote(tag, filters));
        return resale;
    }

    [Test]
    public async Task One_acquisition_per_distinct_spec_and_bounded_concurrency()
    {
        var running = 0;
        var maxRunning = 0;
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .Returns(async (GearItemSpec spec, CancellationToken _) =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref maxRunning, now);
                await Task.Delay(20);
                Interlocked.Decrement(ref running);
                return Direct(1000);
            });
        // ten distinct tools, and two catalog entries (different ids) with the same helmet spec
        var pieces = Enumerable.Range(0, 10).Select(i => MeasuredPiece.Of(Entry("tool", "TOOL_" + i, i))).ToList();
        pieces.Add(MeasuredPiece.Of(Entry("helmet", "HELMET_A", 1)));
        var sameSpec = Entry("helmet", "HELMET_A", 1);
        sameSpec.Id = "helmet:HELMET_A:other";
        pieces.Add(MeasuredPiece.Of(sameSpec));
        var pricer = new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 1000)).Object);

        var priced = await pricer.PriceAsync(pieces);

        acquisition.Verify(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()), Times.Exactly(11));
        maxRunning.Should().BeLessThanOrEqualTo(MeasuredSetupPricer.MaxParallelAcquisitions);
        priced.Pieces.Should().HaveCount(12).And.OnlyContain(p => p.Value.Cost == 1000);
        priced.Degraded.Should().BeFalse();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }

    [Test]
    public async Task Resale_is_quoted_with_the_acquisition_filters_and_falls_back_to_the_plain_tag()
    {
        var filters = new Dictionary<string, string> { ["harvesting"] = ">5" };
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcquisitionResult(Direct(5000).Direct, null, null, AcquisitionReasons.NoBase, filters, At));
        var resale = Resale((tag, f) => f == null ? Quote(tag, 4000, 3500) : ItemResaleQuote.Unknown(tag));
        var pricer = new MeasuredSetupPricer(acquisition.Object, resale.Object);

        var priced = await pricer.PriceAsync(Build());

        var tool = priced.Pieces["tool:HOE"];
        tool.Cost.Should().Be(5000, "the cost is the acquisition's");
        tool.Quote.Resale.Should().Be(3500, "the variant has no sales, so the plain item is the resale value");
        resale.Verify(r => r.GetQuoteAsync("HOE", It.Is<IReadOnlyDictionary<string, string>>(d => d != null && d["harvesting"] == ">5"), It.IsAny<CancellationToken>()), Times.Once);
        resale.Verify(r => r.GetQuoteAsync("HOE", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Transient_acquisition_or_failing_quote_marks_the_result_degraded()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcquisitionResult.Failed(AcquisitionReasons.ListingsNotLoaded, At));
        var resale = new Mock<IItemResaleService>();
        resale.Setup(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("prices down"));

        var pieces = Build();
        var priced = await new MeasuredSetupPricer(acquisition.Object, resale.Object).PriceAsync(pieces);

        priced.Degraded.Should().BeTrue();
        AsBuild(priced, pieces).FullCost.Should().BeNull("an unknown price is not untradeable");
    }

    [Test]
    public async Task Tag_only_piece_and_pet_are_marked_and_looked_up_without_modifiers()
    {
        var catalog = Build(toolPiece: new GearPiece { Tag = "HOE" });
        catalog.Add(MeasuredPiece.Of(new GearCatalogPiece { Id = "pet:ELEPHANT", Slot = "pet", Pet = new GearPet { Type = "ELEPHANT" } }));
        var acquisition = MeasuredTestData.Acquisition(_ => 1000);
        var pricer = new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 1000)).Object);

        var priced = await pricer.PriceAsync(catalog);

        var pieces = priced.Pieces.Values.ToDictionary(p => p.Piece.Slot);
        pieces["tool"].Piece.ModifiersMeasured.Should().BeFalse();
        pieces["helmet"].Piece.ModifiersMeasured.Should().BeTrue();
        pieces["pet"].Piece.ModifiersMeasured.Should().BeFalse();
        pieces["pet"].Piece.Tag.Should().Be("PET_ELEPHANT");
        acquisition.Verify(a => a.GetAsync(It.Is<GearItemSpec>(s => s.Tag == "HOE" && s.Rarity == null && s.Enchants.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
        acquisition.Verify(a => a.GetAsync(It.Is<GearPetSpec>(p => p.Type == "ELEPHANT" && p.Tier == null && p.Level == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Slow_lookups_beyond_the_budget_count_as_timed_out_and_degrade_the_result()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .Returns(async (GearItemSpec spec, CancellationToken token) =>
            {
                if (spec.Tag == "SLOW")
                    await Task.Delay(Timeout.Infinite, token);
                return Direct(1000);
            });
        var pricer = new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 1000)).Object,
            TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5));

        var slowBuild = Build("SLOW");
        var fastBuild = Build("FAST", "H2");
        var priced = await pricer.PriceAsync(slowBuild.Concat(fastBuild).ToList()).WaitAsync(TimeSpan.FromSeconds(10));

        priced.Degraded.Should().BeTrue();
        var slow = priced.Pieces["tool:SLOW"];
        slow.Acquisition.DirectUnavailableReason.Should().Be(AcquisitionReasons.Timeout);
        slow.Transient.Should().BeTrue("the plain item's market price of a timed out spec must not price the build");
        AsBuild(priced, slowBuild).TransientlyUnpriced.Should().BeTrue();
        var fast = AsBuild(priced, fastBuild);
        fast.FullCost.Should().Be(2000);
        fast.TransientlyUnpriced.Should().BeFalse();
    }

    /// <summary>Regression: a spec whose modifiers no filter covers was priced at the plain item's market price without a flag</summary>
    [Test]
    public async Task Market_price_without_filters_for_measured_modifiers_is_a_lower_bound()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Found(null, null)); // no listing, no base, no filters (eg. only post hoc modifiers)
        var tagOnly = Build(toolPiece: new GearPiece { Tag = "HOE" });

        var priced = await new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 999)).Object).PriceAsync(tagOnly);

        var pieces = priced.Pieces.Values.ToDictionary(p => p.Piece.Slot);
        pieces["helmet"].LowerBound.Should().BeTrue("its rarity was measured but nothing filtered for it");
        pieces["tool"].LowerBound.Should().BeFalse("a tag only piece is priced as the plain item on purpose");
        pieces.Values.Should().OnlyContain(p => !p.Transient);
    }

    [Test]
    public async Task Failed_lookup_with_a_direct_price_stays_priced()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Found(1000, null, AcquisitionReasons.PricesNotLoaded));

        var pieces = Build();
        var priced = await new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 999)).Object).PriceAsync(pieces);

        AsBuild(priced, pieces).TransientlyUnpriced.Should().BeFalse("a matching auction is a real price");
        priced.Degraded.Should().BeTrue();
    }
}

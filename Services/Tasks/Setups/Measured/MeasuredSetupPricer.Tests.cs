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

    private static async Task<PricedCluster> PriceOne(Func<string, long?> direct, Func<string, IReadOnlyDictionary<string, string>, ItemResaleQuote> quote,
        IReadOnlySet<string> untradeable = null, GearCluster cluster = null)
    {
        var pricer = new MeasuredSetupPricer(MeasuredTestData.Acquisition(direct).Object, Resale(quote).Object);
        return (await pricer.PriceAsync([cluster ?? Cluster("HOE", 1)], untradeable)).Clusters.Single();
    }

    /// <summary>Regression: an item without listings and sales counted as untradeable, so the setup looked free</summary>
    [Test]
    public async Task Item_without_any_price_is_not_free_without_evidence_it_is_untradeable()
    {
        var cluster = await PriceOne(tag => tag == "HOE" ? 1000 : null, (tag, _) => tag == "HOE" ? Quote(tag, 1000) : ItemResaleQuote.Unknown(tag));

        var helmet = cluster.Pieces.Single(p => p.Piece.Slot == "helmet");
        helmet.NoMarket.Should().BeFalse();
        helmet.Transient.Should().BeFalse();
        cluster.FullCost.Should().BeNull("the setup cost is unknown, not 1000");
        cluster.TransientlyUnpriced.Should().BeFalse();
    }

    [Test]
    public async Task Item_the_profile_marks_untradeable_is_left_out_of_the_cost()
    {
        var cluster = await PriceOne(tag => tag == "HOE" ? 1000 : null, (tag, _) => tag == "HOE" ? Quote(tag, 1000) : ItemResaleQuote.Unknown(tag),
            new HashSet<string> { "HELMET_A" });

        cluster.Pieces.Single(p => p.Piece.Slot == "helmet").NoMarket.Should().BeTrue();
        cluster.FullCost.Should().Be(1000);
    }

    [Test]
    public async Task Plain_tag_market_price_is_a_flagged_lower_bound()
    {
        var filters = new Dictionary<string, string> { ["harvesting"] = ">5" };
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcquisitionResult(null, AcquisitionReasons.NoListing, null, AcquisitionReasons.NoBase, filters, At));
        var resale = Resale((tag, f) => f == null ? Quote(tag, 4000) : ItemResaleQuote.Unknown(tag));

        var priced = await new MeasuredSetupPricer(acquisition.Object, resale.Object).PriceAsync([Cluster("HOE", 1)]);

        var cluster = priced.Clusters.Single();
        cluster.Pieces.Should().OnlyContain(p => p.LowerBound && p.CostSource == CostSources.Market);
        cluster.LowerBound.Should().BeTrue();
    }

    [Test]
    public async Task Market_price_for_a_base_that_needs_grinding_is_a_lower_bound()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Found(null, Base(10, grind: true)));

        var priced = await new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 999)).Object).PriceAsync([Cluster("HOE", 1)]);

        priced.Clusters.Single().Pieces.Should().OnlyContain(p => p.LowerBound && p.Cost == 999);
    }

    [Test]
    public async Task Failed_lookup_without_a_price_makes_the_cluster_transiently_unpriced()
    {
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Found(null, null, AcquisitionReasons.Timeout));

        var priced = await new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => ItemResaleQuote.Unknown(tag)).Object).PriceAsync([Cluster("HOE", 1)]);

        priced.Clusters.Single().TransientlyUnpriced.Should().BeTrue();
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
        // ten clusters with distinct tools, all sharing one helmet
        var clusters = Enumerable.Range(0, 10).Select(i => Cluster("TOOL_" + i, 1_000_000 + i)).ToList();
        var pricer = new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 1000)).Object);

        var priced = await pricer.PriceAsync(clusters);

        acquisition.Verify(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()), Times.Exactly(11));
        maxRunning.Should().BeLessThanOrEqualTo(MeasuredSetupPricer.MaxParallelAcquisitions);
        priced.Clusters.Should().HaveCount(10).And.OnlyContain(c => c.FullCost == 2000);
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

        var priced = await pricer.PriceAsync([Cluster("HOE", 1)]);

        var tool = priced.Clusters.Single().Pieces.Single(p => p.Piece.Slot == "tool");
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

        var priced = await new MeasuredSetupPricer(acquisition.Object, resale.Object).PriceAsync([Cluster("HOE", 1)]);

        priced.Degraded.Should().BeTrue();
        priced.Clusters.Single().FullCost.Should().BeNull("an unknown price is not untradeable");
    }

    [Test]
    public async Task Tag_only_piece_and_pet_are_marked_and_looked_up_without_modifiers()
    {
        var cluster = Cluster("HOE", 1, toolPiece: new GearPiece { Tag = "HOE" });
        cluster.Pet = new GearPet { Type = "ELEPHANT" };
        var acquisition = MeasuredTestData.Acquisition(_ => 1000);
        var pricer = new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 1000)).Object);

        var priced = await pricer.PriceAsync([cluster]);

        var pieces = priced.Clusters.Single().Pieces.ToDictionary(p => p.Piece.Slot);
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

        var priced = await pricer.PriceAsync([Cluster("SLOW", 1), Cluster("FAST", 1, helmet: "H2")]).WaitAsync(TimeSpan.FromSeconds(10));

        priced.Degraded.Should().BeTrue();
        var slowCluster = priced.Clusters.Single(c => c.Cluster.Slots["tool"].Tag == "SLOW");
        var slow = slowCluster.Pieces.Single(p => p.Piece.Slot == "tool");
        slow.Acquisition.DirectUnavailableReason.Should().Be(AcquisitionReasons.Timeout);
        slow.Transient.Should().BeTrue("the plain item's market price of a timed out spec must not price the setup");
        slowCluster.TransientlyUnpriced.Should().BeTrue();
        var fast = priced.Clusters.Single(c => c.Cluster.Slots["tool"].Tag == "FAST");
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
        var tagOnly = Cluster("HOE", 1, toolPiece: new GearPiece { Tag = "HOE" });

        var priced = await new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 999)).Object).PriceAsync([tagOnly]);

        var pieces = priced.Clusters.Single().Pieces.ToDictionary(p => p.Piece.Slot);
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

        var priced = await new MeasuredSetupPricer(acquisition.Object, Resale((tag, _) => Quote(tag, 999)).Object).PriceAsync([Cluster("HOE", 1)]);

        priced.Clusters.Single().TransientlyUnpriced.Should().BeFalse("a matching auction is a real price");
        priced.Degraded.Should().BeTrue();
    }
}

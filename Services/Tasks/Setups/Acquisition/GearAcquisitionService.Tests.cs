using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using static Coflnet.Sky.Core.Enchantment;

namespace SkyApi.Services.Tasks.Setups.Acquisition;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class GearAcquisitionServiceTests
{
    private const string Hoe = "THEORETICAL_HOE_WHEAT_3";
    private FakeListingSource listings;
    private FakePriceBook prices;
    private FakeTimeProvider clock;
    private GearAcquisitionService service;

    [SetUp]
    public void Setup()
    {
        listings = new FakeListingSource();
        prices = new FakePriceBook();
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));
        service = New(clock);
    }

    private GearAcquisitionService New(TimeProvider time, TimeSpan? deadline = null, int maxParallel = GearAcquisitionService.MaxParallel,
        int maxCacheEntries = GearAcquisitionService.MaxCacheEntries)
        => new(listings, prices, time, NullLogger<GearAcquisitionService>.Instance, deadline ?? GearAcquisitionService.DefaultDeadline,
            maxParallel, maxCacheEntries);

    private static GearItemSpec Harvesting(int level) => new() { Tag = Hoe, Enchants = new() { { "harvesting", level } } };

    private SaveAuction Add(long price, int harvesting, Dictionary<string, string> nbt = null)
    {
        var auction = AuctionFactory.Bin(Hoe, price, nbt: nbt, enchants: (EnchantmentType.harvesting, harvesting));
        listings.Auctions.Add(auction);
        return auction;
    }

    [Test]
    public async Task Direct_is_the_cheapest_listing_that_meets_the_spec()
    {
        Add(1_000_000, 5);
        var match = Add(9_000_000, 6);
        Add(12_000_000, 6);

        var result = await service.GetAsync(Harvesting(6));

        result.Direct.Should().Be(new DirectPurchase(match.Uuid, 9_000_000, "/viewauction " + match.Uuid));
        result.DirectUnavailableReason.Should().BeNull();
        result.Filters.Should().Contain("harvesting", ">5");
    }

    [Test]
    public async Task Base_with_upgrade_beats_a_more_expensive_direct_listing()
    {
        prices.Enchants[(EnchantmentType.harvesting, 6)] = 2_000_000;
        var cheap = Add(1_000_000, 5);
        Add(9_000_000, 6);

        var result = await service.GetAsync(Harvesting(6));

        result.Base.AuctionUuid.Should().Be(cheap.Uuid);
        result.Base.Total.Should().Be(3_000_000);
        result.Direct.Price.Should().Be(9_000_000);
    }

    [Test]
    public async Task Direct_listing_wins_the_base_when_upgrading_costs_more()
    {
        prices.Enchants[(EnchantmentType.harvesting, 6)] = 20_000_000;
        Add(1_000_000, 5);
        var match = Add(9_000_000, 6);

        var result = await service.GetAsync(Harvesting(6));

        result.Base.AuctionUuid.Should().Be(match.Uuid);
        result.Base.Upgrades.Should().BeEmpty();
    }

    [Test]
    public async Task Base_that_needs_grinding_loses_against_one_that_already_has_the_counter()
    {
        var spec = new GearItemSpec { Tag = Hoe, Counters = new() { { "farmed_cultivating", 1_000_000 } } };
        Add(1_000, 1, new() { { "farmed_cultivating", "10" } });
        var ground = Add(5_000_000, 1, new() { { "farmed_cultivating", "2000000" } });

        var result = await service.GetAsync(spec);

        result.Base.AuctionUuid.Should().Be(ground.Uuid);
        result.Base.GrindRequired.Should().BeEmpty();
        result.Direct.AuctionUuid.Should().Be(ground.Uuid);
    }

    [Test]
    public async Task Only_grinding_candidates_return_the_cheapest_with_its_deficit()
    {
        var spec = new GearItemSpec { Tag = Hoe, Counters = new() { { "farmed_cultivating", 1_000_000 } } };
        Add(1_000, 1, new() { { "farmed_cultivating", "10" } });

        var result = await service.GetAsync(spec);

        result.Direct.Should().BeNull();
        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.NoListing);
        result.Base.GrindRequired.Should().ContainSingle().Which.Deficit.Should().Be(999_990);
    }

    [Test]
    public async Task Clean_item_price_is_a_base_candidate()
    {
        prices.Clean[Hoe] = 500_000;
        prices.Enchants[(EnchantmentType.harvesting, 6)] = 2_000_000;
        Add(9_000_000, 6);

        var result = await service.GetAsync(Harvesting(6));

        result.Base.AuctionUuid.Should().BeNull();
        result.Base.BasePrice.Should().Be(500_000);
        result.Base.Total.Should().Be(2_500_000);
    }

    [Test]
    public async Task Gem_requirement_is_verified_on_the_nbt()
    {
        var spec = new GearItemSpec { Tag = Hoe, Gems = new() { { "PERIDOT_0", "FINE_PERIDOT_GEM" } } };
        Add(1_000, 1, new() { { "PERIDOT_0", "ROUGH" } });
        var match = Add(2_000, 1, new() { { "PERIDOT_0", "PERFECT" } });

        var result = await service.GetAsync(spec);

        result.Direct.AuctionUuid.Should().Be(match.Uuid);
    }

    [Test]
    public async Task Unsupported_modifier_makes_direct_unavailable_and_the_base_incomplete()
    {
        Add(1_000, 6);
        var spec = Harvesting(6);
        spec.Enchants["made_up_enchant"] = 2;

        var result = await service.GetAsync(spec);

        result.Direct.Should().BeNull();
        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.Unsupported);
        result.Base.Incomplete.Should().BeTrue();
    }

    [Test]
    public async Task Not_loaded_replica_falls_back_to_the_database_for_filterable_specs()
    {
        listings.IsReady = false;
        var uuid = AuctionFactory.Uuid(77);
        listings.Database = (_, _) => Task.FromResult<(long, string)?>((4_000_000, uuid));

        var result = await service.GetAsync(Harvesting(6));

        result.Direct.Should().Be(new DirectPurchase(uuid, 4_000_000, "/viewauction " + uuid));
        listings.LastDatabaseFilters.Should().Contain("harvesting", ">5");
        listings.ActiveBinCalls.Should().Be(0);
    }

    [Test]
    public async Task Not_loaded_replica_cannot_verify_nbt_only_requirements()
    {
        listings.IsReady = false;
        var spec = new GearItemSpec { Tag = Hoe, Gems = new() { { "PERIDOT_0", "FINE_PERIDOT_GEM" } } };

        var result = await service.GetAsync(spec);

        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.ListingsNotLoaded);
        listings.LastDatabaseFilters.Should().BeNull();
    }

    [Test]
    public async Task Failing_sources_degrade_to_reasons_and_the_clean_price_base()
    {
        listings.ActiveBinsOverride = () => throw new InvalidOperationException("replica broken");
        listings.Database = (_, _) => throw new InvalidOperationException("db down");
        prices.Clean[Hoe] = 500_000;
        prices.Enchants[(EnchantmentType.harvesting, 6)] = 2_000_000;

        var result = await service.GetAsync(Harvesting(6));

        result.Direct.Should().BeNull();
        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.Failed);
        result.Base.AuctionUuid.Should().BeNull();
        result.Base.Total.Should().Be(2_500_000);
    }

    [Test]
    public async Task Prices_not_loaded_only_disable_the_base()
    {
        prices.IsLoaded = false;
        Add(9_000_000, 6);

        var result = await service.GetAsync(Harvesting(6));

        result.Direct.Should().NotBeNull();
        result.Base.Should().BeNull();
        result.BaseUnavailableReason.Should().Be(AcquisitionReasons.PricesNotLoaded);
    }

    [Test]
    public async Task Hanging_database_hits_the_deadline()
    {
        listings.IsReady = false;
        listings.Database = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        };
        var fast = New(TimeProvider.System, TimeSpan.FromMilliseconds(100));

        var result = await fast.GetAsync(Harvesting(6)).WaitAsync(TimeSpan.FromSeconds(10));

        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.Timeout);
    }

    [Test]
    public async Task Results_are_cached_per_normalized_spec()
    {
        Add(9_000_000, 6);
        await service.GetAsync(Harvesting(6));
        clock.Advance(TimeSpan.FromMinutes(2));

        await service.GetAsync(new GearItemSpec { Tag = Hoe.ToLowerInvariant(), Enchants = new() { { "Harvesting", 6 } } });
        listings.ActiveBinCalls.Should().Be(1);

        clock.Advance(TimeSpan.FromMinutes(2));
        await service.GetAsync(Harvesting(6));
        listings.ActiveBinCalls.Should().Be(2);
    }

    [Test]
    public async Task Transient_failures_are_cached_shorter()
    {
        prices.IsLoaded = false;
        await service.GetAsync(Harvesting(6));
        clock.Advance(TimeSpan.FromSeconds(31));

        await service.GetAsync(Harvesting(6));

        listings.ActiveBinCalls.Should().Be(2);
    }

    [Test]
    public async Task Missing_tag_is_a_reason_not_an_exception()
    {
        var result = await service.GetAsync(new GearItemSpec());

        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.MissingTag);
        result.BaseUnavailableReason.Should().Be(AcquisitionReasons.MissingTag);
    }

    [Test]
    public async Task Pet_direct_uses_tier_level_and_held_item()
    {
        var pet = AuctionFactory.Bin("PET_ELEPHANT", 30_000_000, nbt: new() { { "exp", "25353230" }, { "candyUsed", "0" }, { "heldItem", "GREEN_BANDANA" } });
        pet.ItemName = "[Lvl 100] Elephant";
        var low = AuctionFactory.Bin("PET_ELEPHANT", 10_000_000, nbt: new() { { "exp", "100" }, { "candyUsed", "0" }, { "heldItem", "GREEN_BANDANA" } });
        low.ItemName = "[Lvl 2] Elephant";
        listings.Auctions.AddRange(new[] { pet, low });

        var result = await service.GetAsync(new GearPetSpec { Type = "ELEPHANT", Tier = "LEGENDARY", Level = 100, HeldItem = "GREEN_BANDANA" });

        result.Direct.AuctionUuid.Should().Be(pet.Uuid);
        result.Base.AuctionUuid.Should().Be(pet.Uuid); // the low pet needs 98 levels of grinding
    }

    [TestCase("0123456789ABCDEF0123456789abcdef", "0123456789abcdef0123456789abcdef")]
    [TestCase("01234567-89ab-cdef-0123-456789abcdef", "0123456789abcdef0123456789abcdef")]
    [TestCase("not-a-uuid", null)]
    [TestCase(null, null)]
    public void Uuid_is_normalized_to_32_hex(string input, string expected)
    {
        GearAcquisitionService.NormalizeUuid(input).Should().Be(expected);
    }
    /// <summary>Regression: the database lookup kept running after the deadline while its slot was already free again</summary>
    [Test]
    public async Task Database_lookup_that_ignores_the_deadline_keeps_its_slot_until_it_ends()
    {
        listings.IsReady = false;
        var query = new TaskCompletionSource<(long Price, string Uuid)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        listings.Database = (_, _) => query.Task; // ignores the token
        var fast = New(TimeProvider.System, TimeSpan.FromMilliseconds(100), maxParallel: 2);

        var result = await fast.GetAsync(Harvesting(6)).WaitAsync(TimeSpan.FromSeconds(10));

        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.Timeout);
        fast.FreeSlots.Should().Be(1);
        query.SetResult(null);
        await WaitUntil(() => fast.FreeSlots == 2);
    }

    /// <summary>Regression: the shared computation ran synchronously on the first caller's thread</summary>
    [Test]
    public async Task Computation_does_not_block_the_caller_and_the_deadline_covers_synchronous_work()
    {
        using var gate = new ManualResetEventSlim(false);
        listings.ActiveBinsOverride = () => { gate.Wait(TimeSpan.FromSeconds(10)); return new List<SaveAuction>(); };
        var fast = New(TimeProvider.System, TimeSpan.FromMilliseconds(100));

        var call = fast.GetAsync(Harvesting(6));
        var returnedBeforeTheWork = !call.IsCompleted;
        var result = await call.WaitAsync(TimeSpan.FromSeconds(5));
        gate.Set();

        returnedBeforeTheWork.Should().BeTrue();
        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.Timeout);
    }

    /// <summary>Regression: a computation past the deadline was thrown away, so a slow spec timed out on every build</summary>
    [Test]
    public async Task Slow_spec_times_out_for_the_caller_and_the_finished_result_is_served_next()
    {
        Add(9_000_000, 6);
        using var gate = new ManualResetEventSlim(false);
        listings.ActiveBinsOverride = () =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return listings.Auctions.OrderBy(a => a.StartingBid).ToList();
        };
        var fast = New(TimeProvider.System, TimeSpan.FromMilliseconds(100));

        var first = await fast.GetAsync(Harvesting(6)).WaitAsync(TimeSpan.FromSeconds(5));
        gate.Set();
        await WaitUntil(() => fast.CachedEntries == 1 && fast.FreeSlots == GearAcquisitionService.MaxParallel);
        var second = await fast.GetAsync(Harvesting(6));

        first.DirectUnavailableReason.Should().Be(AcquisitionReasons.Timeout);
        second.Direct.Should().NotBeNull("the computation finished after the deadline and cached its result");
        listings.ActiveBinCalls.Should().Be(1);
    }

    [Test]
    public async Task Concurrent_callers_of_one_spec_share_one_computation()
    {
        using var gate = new ManualResetEventSlim(false);
        listings.ActiveBinsOverride = () => { gate.Wait(TimeSpan.FromSeconds(10)); return new List<SaveAuction>(); };
        var shared = New(TimeProvider.System);

        var first = shared.GetAsync(Harvesting(6));
        var second = shared.GetAsync(Harvesting(6));
        await WaitUntil(() => listings.ActiveBinCalls > 0);
        gate.Set();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        listings.ActiveBinCalls.Should().Be(1);
        results[0].Should().BeSameAs(results[1]);
    }

    /// <summary>Regression: the 30 cheapest pets were all of a lower rarity, the wanted one was never a candidate</summary>
    [Test]
    public async Task Candidates_that_can_never_reach_the_spec_do_not_use_up_the_candidate_limit()
    {
        for (var i = 0; i < GearAcquisitionService.BaseCandidates + 5; i++)
            listings.Auctions.Add(AuctionFactory.Bin("PET_ELEPHANT", 1_000 + i, Tier.RARE, new() { { "candyUsed", "0" } }));
        var legendary = AuctionFactory.Bin("PET_ELEPHANT", 50_000_000, Tier.LEGENDARY, new() { { "candyUsed", "0" } });
        legendary.ItemName = "[Lvl 1] Elephant";
        listings.Auctions.Add(legendary);

        var result = await service.GetAsync(new GearPetSpec { Type = "ELEPHANT", Tier = "LEGENDARY", Level = 100 });

        result.Base.AuctionUuid.Should().Be(legendary.Uuid);
        result.Base.GrindRequired.Should().ContainSingle().Which.Kind.Should().Be("petLevel");
    }

    [TestCase(Tier.EPIC, true)]
    [TestCase(Tier.RARE, false)]
    public async Task Clean_item_is_priced_at_the_base_rarity_of_the_tag(Tier baseRarity, bool cleanIsCandidate)
    {
        prices.Clean[Hoe] = 500_000;
        prices.Rarities[Hoe] = baseRarity;
        prices.Items["RECOMBOBULATOR_3000"] = 7_000_000;

        var result = await service.GetAsync(new GearItemSpec { Tag = Hoe, Rarity = "LEGENDARY" });

        if (!cleanIsCandidate)
        {
            result.Base.Should().BeNull();
            return;
        }
        result.Base.AuctionUuid.Should().BeNull();
        result.Base.Upgrades.Should().ContainSingle().Which.Kind.Should().Be("recombobulator");
    }

    /// <summary>Regression: the database path returned listings whose rarity was never checked</summary>
    [Test]
    public async Task Database_fallback_is_not_used_when_the_rarity_can_not_be_verified()
    {
        listings.IsReady = false;
        listings.Database = (_, _) => Task.FromResult<(long, string)?>((1, AuctionFactory.Uuid(5)));
        var spec = Harvesting(6);
        spec.Rarity = "LEGENDARY";

        var result = await service.GetAsync(spec);

        result.Direct.Should().BeNull();
        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.ListingsNotLoaded);
        listings.LastDatabaseFilters.Should().BeNull();
    }

    [Test]
    public async Task Pet_without_tier_does_not_fail_on_the_database_fallback()
    {
        listings.IsReady = false;

        var result = await service.GetAsync(new GearPetSpec { Type = "ELEPHANT", Level = 100 });

        result.DirectUnavailableReason.Should().Be(AcquisitionReasons.ListingsNotLoaded);
    }

    [Test]
    public async Task Full_cache_drops_the_entries_that_expire_first_instead_of_everything()
    {
        var small = New(clock, maxCacheEntries: 10);
        for (var level = 1; level <= 10; level++)
        {
            await small.GetAsync(Harvesting(level));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        await small.GetAsync(Harvesting(11));
        small.CachedEntries.Should().Be(10);
        var calls = listings.ActiveBinCalls;
        await small.GetAsync(Harvesting(10));
        await small.GetAsync(Harvesting(11));
        listings.ActiveBinCalls.Should().Be(calls); // the newest entries survived
        await small.GetAsync(Harvesting(1));
        listings.ActiveBinCalls.Should().Be(calls + 1); // the oldest was evicted
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until)
            await Task.Delay(10);
        condition().Should().BeTrue();
    }
}

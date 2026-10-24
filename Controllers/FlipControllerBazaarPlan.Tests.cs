using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Api.Services.Mod.Tests;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Client;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

/// <summary>Tests of the personal bazaar plan endpoints.</summary>
[TestFixture]
public class FlipControllerBazaarPlanTests
{
    private const string Url = "/api/flip/bazaar/plan";

    /// <summary>Plans need a logged in user.</summary>
    [Test]
    public async Task Plan_requires_authentication_401()
    {
        using var h = new Harness();

        var response = await h.Post(Url, "{}", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(h.Fleet.Requests, Is.Empty);
    }

    /// <summary>Free users get the plan too, only with sampled rows.</summary>
    [Test]
    public async Task Plan_is_available_without_premium()
    {
        using var h = new Harness();

        var response = await h.Post(Url, "{}", "free-1");

        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        Assert.That(JObject.Parse(body).Value<string>("tier"), Is.EqualTo("logged_in"));
    }

    /// <summary>Without any input the plan is active only, with the horizon of an hour and two thirds of the purse.</summary>
    [Test]
    public async Task Plan_defaults_modes_horizon_and_budget_from_purse_and_labels_source()
    {
        using var h = new Harness { Purse = 6_000_000 };

        var response = await h.Post(Url, "{}", "free-1");
        var plan = JObject.Parse(await response.Content.ReadAsStringAsync());

        var trader = h.Fleet.Requests.Single().Traders.Single();
        Assert.Multiple(() =>
        {
            Assert.That(trader.Strategies, Is.EqualTo(new[] { "Active" }));
            Assert.That(trader.ActiveHorizonHours, Is.EqualTo(1));
            Assert.That(trader.InvestingHorizonHours, Is.Null);
            Assert.That(trader.BudgetCoins, Is.EqualTo(4_000_000));
            Assert.That(trader.SellTaxSource, Is.EqualTo("Assumption"));
            Assert.That(plan.Value<double>("budgetCoins"), Is.EqualTo(4_000_000));
            Assert.That(plan.Value<string>("budgetSource"), Is.EqualTo("purse-estimate"));
            Assert.That(plan["modes"]!.Select(m => (string)m), Is.EqualTo(new[] { "Active" }));
        });

        h.Fleet.Requests.Clear();
        var explicitBudget = JObject.Parse(await (await h.Post(Url, "{\"budgetCoins\": 1500000}", "free-1")).Content.ReadAsStringAsync());
        Assert.That(explicitBudget.Value<string>("budgetSource"), Is.EqualTo("request"));
        Assert.That(h.Fleet.Requests.Single().Traders.Single().BudgetCoins, Is.EqualTo(1_500_000));
    }

    /// <summary>An unknown purse and no budget asks the caller for one.</summary>
    [Test]
    public async Task Plan_without_budget_and_purse_asks_for_a_budget()
    {
        using var h = new Harness { Purse = null };

        var response = await h.Post(Url, "{}", "free-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(JObject.Parse(await response.Content.ReadAsStringAsync()).Value<string>("slug"), Is.EqualTo("budget_required"));
        Assert.That(h.Fleet.Requests, Is.Empty);
    }

    /// <summary>The horizon is never clamped, 0 and above 48 hours are rejected before the planner is asked.</summary>
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("48.5")]
    [TestCase("49")]
    public async Task Plan_rejects_horizon_zero_and_above_48_with_400(string horizon)
    {
        using var h = new Harness();

        var response = await h.Post(Url, "{\"modes\":[\"investing\"],\"investingHorizonHours\":" + horizon + "}", "free-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(JObject.Parse(await response.Content.ReadAsStringAsync()).Value<string>("slug"), Is.EqualTo("invalid_horizon"));
        Assert.That(h.Fleet.Requests, Is.Empty);
    }

    /// <summary>Investing has no default horizon.</summary>
    [Test]
    public async Task Plan_rejects_investing_without_horizon()
    {
        using var h = new Harness();

        var response = await h.Post(Url, "{\"modes\":[\"investing\"]}", "free-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(JObject.Parse(await response.Content.ReadAsStringAsync()).Value<string>("slug"), Is.EqualTo("invalid_horizon"));
    }

    /// <summary>Unknown, empty and duplicate modes are rejected.</summary>
    [TestCase("[\"fast\"]")]
    [TestCase("[]")]
    [TestCase("[\"active\",\"Active\"]")]
    public async Task Plan_rejects_invalid_modes(string modes)
    {
        using var h = new Harness();

        var response = await h.Post(Url, "{\"modes\":" + modes + "}", "free-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(JObject.Parse(await response.Content.ReadAsStringAsync()).Value<string>("slug"), Is.EqualTo("invalid_modes"));
        Assert.That(h.Fleet.Requests, Is.Empty);
    }

    /// <summary>The answer belongs to one user, it must not be cached or shared.</summary>
    [Test]
    public async Task Plan_responses_are_not_shared_between_users_and_are_no_store()
    {
        using var h = new Harness();
        h.Fleet.Orders = traderId => [Order("ITEM_" + traderId[^2..])];

        var first = await h.Post(Url, "{}", "premium-1");
        var second = await h.Post(Url, "{}", "premium-2");

        var a = JObject.Parse(await first.Content.ReadAsStringAsync());
        var b = JObject.Parse(await second.Content.ReadAsStringAsync());
        Assert.Multiple(() =>
        {
            Assert.That(first.Headers.CacheControl!.NoStore, Is.True);
            Assert.That(first.Headers.CacheControl.Public, Is.False);
            Assert.That(second.Headers.CacheControl!.NoStore, Is.True);
            Assert.That(h.Fleet.Requests.Select(r => r.Traders.Single().Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(a["orders"]![0]!.Value<string>("itemTag"), Is.Not.EqualTo(b["orders"]![0]!.Value<string>("itemTag")));
            Assert.That(a["quote"]!.Value<string>("quoteId"), Is.Not.EqualTo(b["quote"]!.Value<string>("quoteId")));
        });
    }

    /// <summary>An empty qualified result and an unavailable candidate set are different answers.</summary>
    [Test]
    public async Task Plan_maps_empty_qualified_distinct_from_unavailable()
    {
        using var h = new Harness();
        h.Fleet.Canned["uuid-1"] = "empty-qualified-response";
        h.Fleet.Canned["uuid-2"] = "unavailable-response";
        const string body = "{\"modes\":[\"investing\"],\"investingHorizonHours\":24}";

        var empty = JObject.Parse(await (await h.Post(Url, body, "premium-1")).Content.ReadAsStringAsync());
        var unavailable = JObject.Parse(await (await h.Post(Url, body, "premium-2")).Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(empty.Value<string>("investingStatus"), Is.EqualTo("EmptyQualified"));
            Assert.That(empty["investingReasonCodes"]!.Select(c => (string)c), Is.EqualTo(new[] { "NoQualifiedCandidates" }));
            Assert.That(unavailable.Value<string>("investingStatus"), Is.EqualTo("Unavailable"));
            Assert.That(unavailable["investingReasonCodes"]!.Select(c => (string)c), Is.EqualTo(new[] { "InvestingCandidatesNotReady" }));
            Assert.That(empty["orders"], Is.Empty);
            Assert.That(unavailable["orders"], Is.Empty);
            Assert.That(empty["portfolio"]!.Value<double>("budgetCoins"), Is.EqualTo(1_000_000));
        });
    }

    /// <summary>Estimated fills and unreadable orders are reported, never hidden.</summary>
    [Test]
    public async Task Plan_marks_estimated_open_orders()
    {
        using var h = new Harness();
        h.Orders.Orders = [BuyOrder("EST", 10, 0, 100, estimate: true)];

        var estimated = JObject.Parse(await (await h.Post(Url, "{}", "free-1")).Content.ReadAsStringAsync());

        h.Orders.Fail = true;
        var failed = JObject.Parse(await (await h.Post(Url, "{}", "free-1")).Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(estimated["accountState"]!.Value<bool>("openOrdersEstimated"), Is.True);
            Assert.That(estimated["warnings"], Is.Empty);
            Assert.That(failed["accountState"]!.Value<bool>("openOrdersEstimated"), Is.True);
            Assert.That(failed["warnings"]!.Select(w => (string)w), Is.EqualTo(new[] { "orders_unavailable" }));
            Assert.That(failed["accountState"]!["freeOrderSlots"]!.Type, Is.EqualTo(JTokenType.Null));
        });
    }

    /// <summary>Everything the user chose reaches the planner as a single preview trader.</summary>
    [Test]
    public async Task Plan_forwards_modes_horizon_budget_and_open_buy_tags_unchanged_with_preview_true()
    {
        using var h = new Harness { Purse = 50_000_000 };
        h.Orders.Orders =
        [
            BuyOrder("OPEN_BUY", 100, 40, 1000),
            BuyOrder("DONE_BUY", 10, 10, 50),
            SellOrder("SELLING", 20, 5, 70),
            BuyOrder("EXPIRED_BUY", 10, 0, 1000, expired: true)
        ];

        var response = await h.Post(Url,
            "{\"modes\":[\"active\",\"INVESTING\"],\"investingHorizonHours\":24,\"activeHorizonHours\":2,\"budgetCoins\":5000000,\"sellTaxRate\":0.0125}", "premium-3");

        var plan = JObject.Parse(await response.Content.ReadAsStringAsync());
        var request = h.Fleet.Requests.Single();
        var trader = request.Traders.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Preview, Is.True);
            Assert.That(request.IncludeUnqualifiedInvesting, Is.True);
            Assert.That(request.InstanceId, Is.EqualTo("preview:skyapi"));
            Assert.That(request.Mode, Is.EqualTo("Fast"));
            Assert.That(request.TargetHoldTimeHours, Is.EqualTo(1));
            Assert.That(request.CapacityShare, Is.EqualTo(1));
            Assert.That(Guid.TryParse(request.RequestId, out _), Is.True);
            Assert.That(trader.Id, Is.EqualTo("uuid-3"));
            Assert.That(trader.IsBot, Is.False);
            Assert.That(trader.Strategies, Is.EqualTo(new[] { "Active", "Investing" }));
            Assert.That(trader.InvestingHorizonHours, Is.EqualTo(24));
            Assert.That(trader.ActiveHorizonHours, Is.EqualTo(2));
            Assert.That(trader.SellTaxRate, Is.EqualTo(0.0125));
            Assert.That(trader.SellTaxSource, Is.EqualTo("AccountAttested"));
            // 5,000,000 minus the 60 units still open at 1,000 each, the caller owns the subtraction
            Assert.That(trader.BudgetCoins, Is.EqualTo(4_940_000));
            Assert.That(trader.MaxItems, Is.EqualTo(10));
            Assert.That(trader.AccountState.OpenBuyItemTags, Is.EqualTo(new[] { "OPEN_BUY" }));
            Assert.That(trader.AccountState.ReserveOwner, Is.EqualTo("Caller"));
            Assert.That(trader.AccountState.FreeOrderSlots, Is.EqualTo(21 - 3));
            Assert.That(trader.AccountState.HeldExposure.Select(x => (x.ItemTag, x.Units)).OrderBy(x => x.ItemTag),
                Is.EqualTo(new[] { ("DONE_BUY", 10d), ("OPEN_BUY", 40d), ("SELLING", 15d) }));
            Assert.That(plan.Value<double>("budgetCoins"), Is.EqualTo(5_000_000));
            Assert.That(plan.Value<double>("investingHorizonHours"), Is.EqualTo(24));
            Assert.That(plan["modes"]!.Select(m => (string)m), Is.EqualTo(new[] { "Active", "Investing" }));
            Assert.That(h.Orders.Players, Is.EqualTo(new[] { "Player3" }));
        });
    }

    /// <summary>Free users see three of the ranks 4 to 10 and how many are hidden, nothing else about the rest.</summary>
    [Test]
    public async Task Plan_free_tier_returns_three_samples_from_ranks_4_to_10_and_hidden_count()
    {
        using var h = new Harness();
        h.Fleet.Orders = _ => Enumerable.Range(1, 12).Select(i => Order("ITEM_" + i)).ToList();
        h.Fleet.Candidates = _ => [Order("CANDIDATE", "Investing")];

        var free = JObject.Parse(await (await h.Post(Url, "{}", "free-7")).Content.ReadAsStringAsync());
        var paid = JObject.Parse(await (await h.Post(Url, "{}", "premium-7")).Content.ReadAsStringAsync());

        var ranks = free["orders"]!.Select(o => int.Parse(o.Value<string>("itemTag")!["ITEM_".Length..])).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ranks, Has.Count.EqualTo(3));
            Assert.That(ranks, Is.All.InRange(4, 10));
            Assert.That(free.Value<int>("hiddenCount"), Is.EqualTo(9));
            Assert.That(free.Value<string>("premiumUrl"), Is.EqualTo("https://sky.coflnet.com/premium"));
            Assert.That(free["unqualifiedInvestingCandidates"], Is.Empty);
            Assert.That(free.ToString(), Does.Not.Contain("ITEM_1\"").And.Not.Contain("CANDIDATE"));
            Assert.That(paid["orders"], Has.Count.EqualTo(12));
            Assert.That(paid.Value<int>("hiddenCount"), Is.Zero);
            Assert.That(paid["premiumUrl"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(paid["unqualifiedInvestingCandidates"], Has.Count.EqualTo(1));
        });
    }

    /// <summary>The same user keeps the samples during a day, other users see other ones.</summary>
    [Test]
    public async Task Plan_free_tier_samples_are_stable_per_user_and_differ_between_users()
    {
        using var h = new Harness();
        h.Fleet.Orders = _ => Enumerable.Range(1, 12).Select(i => Order("ITEM_" + i)).ToList();
        var samples = new Dictionary<int, string>();

        for (var user = 1; user <= 12; user++)
        {
            var first = await Sample(h, user);
            Assert.That(await Sample(h, user), Is.EqualTo(first), "user " + user);
            samples[user] = first;
        }

        Assert.That(samples.Values.Distinct().Count(), Is.GreaterThan(1));
    }

    /// <summary>An unknown probability stays unknown on the wire.</summary>
    [Test]
    public async Task Plan_null_probability_stays_null()
    {
        using var h = new Harness();
        h.Fleet.Orders = _ => [Order("ITEM_A")];

        var body = await (await h.Post(Url, "{}", "premium-1")).Content.ReadAsStringAsync();

        var confidence = JObject.Parse(body)["orders"]![0]!["confidence"]!;
        Assert.Multiple(() =>
        {
            Assert.That(confidence["probability"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(confidence["lowerBound"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(confidence.Value<string>("status"), Is.EqualTo("InsufficientEvidence"));
        });
    }

    /// <summary>The planner rejecting the request is a 400 with its message, a failing planner a 503.</summary>
    [Test]
    public async Task Plan_maps_planner_rejection_to_400_and_outage_to_503()
    {
        using var h = new Harness();
        h.Fleet.Fail = new ApiException(400, "bad", File.ReadAllText(Harness.FixturePath("invalid-horizon-49.problem")));

        var rejected = await h.Post(Url, "{}", "free-1");
        var rejectedBody = JObject.Parse(await rejected.Content.ReadAsStringAsync());

        h.Fleet.Fail = new HttpRequestException("down");
        var unavailable = await h.Post(Url, "{}", "free-1");
        var unavailableBody = JObject.Parse(await unavailable.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(rejectedBody.Value<string>("slug"), Is.EqualTo("invalid_plan_request"));
            Assert.That(rejectedBody.Value<string>("message"), Does.Contain("traders[0].investingHorizonHours"));
            Assert.That(unavailable.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(unavailableBody.Value<string>("slug"), Is.EqualTo("plan_unavailable"));
        });
    }

    /// <summary>A quote nobody knows or that ran out cannot be tracked.</summary>
    [Test]
    public async Task Track_rejects_expired_quote_with_409()
    {
        using var h = new Harness();
        h.Fleet.Orders = _ => [Order("ITEM_A")];
        h.Fleet.QuoteLifetime = TimeSpan.FromSeconds(-5);
        var plan = JObject.Parse(await (await h.Post(Url, "{}", "premium-1")).Content.ReadAsStringAsync());

        var expired = await h.Post(Url + "/track", Track(plan.SelectToken("quote.quoteId")!.ToString(), "ITEM_A"), "premium-1");
        var unknown = await h.Post(Url + "/track", Track(Guid.NewGuid().ToString(), "ITEM_A"), "premium-1");

        Assert.Multiple(() =>
        {
            Assert.That(expired.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(h.Goals, Is.Empty);
        });
        Assert.That(JObject.Parse(await unknown.Content.ReadAsStringAsync()).Value<string>("slug"), Is.EqualTo("quote_expired"));
    }

    /// <summary>Tracking creates the guided flip, a second unfinished flip of the item is refused and quotes are per user.</summary>
    [Test]
    public async Task Track_creates_plan_goal_and_rejects_second_active_flip()
    {
        using var h = new Harness();
        h.Fleet.Orders = _ => [Order("ITEM_A", amount: 192, entry: 105.1, exit: 209.9)];
        var plan = JObject.Parse(await (await h.Post(Url, "{}", "premium-1")).Content.ReadAsStringAsync());
        var quoteId = plan.SelectToken("quote.quoteId")!.ToString();

        var foreign = await h.Post(Url + "/track", Track(quoteId, "ITEM_A"), "premium-2");
        var first = await h.Post(Url + "/track", Track(quoteId, "ITEM_A"), "premium-1");
        var second = await h.Post(Url + "/track", Track(quoteId, "ITEM_A"), "premium-1");

        Assert.Multiple(() =>
        {
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(h.Goals, Has.Count.EqualTo(1));
        });
        var goal = h.Goals.Single();
        Assert.Multiple(() =>
        {
            Assert.That(goal.BazaarFlip, Is.Not.Null);
            Assert.That(goal.Source, Is.EqualTo("website"));
            Assert.That(goal.Root.Kind, Is.EqualTo(StepKind.Sell));
            Assert.That(goal.Quantity, Is.EqualTo(192));
            Assert.That(goal.Plan.QuoteId, Is.EqualTo(quoteId));
            Assert.That(goal.Plan.EntryPrice, Is.EqualTo(105.1));
            Assert.That(goal.Plan.ExitPrice, Is.EqualTo(209.9));
            Assert.That(goal.Plan.Deadline, Is.Null);
            Assert.That(h.GoalList.FocusId, Is.EqualTo(goal.Id));
        });
        Assert.That(JObject.Parse(await second.Content.ReadAsStringAsync()).Value<string>("slug"), Is.EqualTo("bazaar_goal_active"));
    }

    /// <summary>The purse is already net of coins placed in BUY orders, they must not be subtracted again.</summary>
    [Test]
    public async Task Plan_purse_estimate_does_not_subtract_placed_buy_orders_twice()
    {
        using var h = new Harness { Purse = 6_000_000 };
        h.Orders.Orders = [BuyOrder("OPEN_BUY", 3000, 0, 1000)];

        var response = await h.Post(Url, "{}", "free-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(h.Fleet.Requests.Single().Traders.Single().BudgetCoins, Is.EqualTo(4_000_000));
    }

    /// <summary>An explicit budget is the total cap, coins in open buys count against it and the purse limits it.</summary>
    [Test]
    public async Task Plan_request_budget_is_a_total_cap_net_of_open_buys()
    {
        using var h = new Harness { Purse = 10_000_000 };
        h.Orders.Orders = [BuyOrder("OPEN_BUY", 3000, 0, 1000)];

        await h.Post(Url, "{\"budgetCoins\": 5000000}", "free-1");
        h.Purse = 1_500_000;
        await h.Post(Url, "{\"budgetCoins\": 5000000}", "free-1");
        h.Purse = null;
        await h.Post(Url, "{\"budgetCoins\": 5000000}", "free-1");
        h.Orders.Orders = [BuyOrder("OPEN_BUY", 9000, 0, 1000)];
        await h.Post(Url, "{\"budgetCoins\": 5000000}", "free-1");

        Assert.That(h.Fleet.Requests.Select(r => r.Traders.Single().BudgetCoins), Is.EqualTo(new[] { 2_000_000d, 1_500_000d, 2_000_000d, 0d }));
    }

    /// <summary>Free users must not learn about hidden orders from totals or the investing state.</summary>
    [Test]
    public async Task Plan_free_tier_portfolio_totals_reflect_only_visible_orders()
    {
        using var h = new Harness();
        h.Fleet.Orders = _=> Enumerable.Range(1, 12).Select(i => Order("ITEM_" + i, i <= 11 ? "Investing" : "Active")).ToList();

        var free = JObject.Parse(await (await h.Post(Url, "{}", "free-7")).Content.ReadAsStringAsync());
        var paid = JObject.Parse(await (await h.Post(Url, "{}", "premium-7")).Content.ReadAsStringAsync());

        var visible = free["orders"]!.Count();
        var portfolio = free["portfolio"]!;
        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.EqualTo(3));
            Assert.That(portfolio.Value<double>("spentCoins"), Is.EqualTo(visible * 100 * 100));
            Assert.That(portfolio.Value<double>("remainingBudgetCoins"), Is.EqualTo(portfolio.Value<double>("budgetCoins") - visible * 100 * 100));
            Assert.That(portfolio.Value<int>("freeSlotsAfter"), Is.EqualTo(21 - visible));
            Assert.That(free.Value<string>("investingStatus"), Is.EqualTo("Allocated"));
            Assert.That(free["investingReasonCodes"], Is.Empty);
            Assert.That(paid["portfolio"]!.Value<double>("spentCoins"), Is.EqualTo(12 * 100 * 100));
            Assert.That(paid["investingReasonCodes"], Is.Not.Empty);
        });
    }

    /// <summary>A tag the free user cannot see in the sample answers like an unknown one.</summary>
    [Test]
    public async Task Track_free_tier_hidden_tag_returns_404()
    {
        using var h = new Harness();
        h.Fleet.Orders = _ => Enumerable.Range(1, 12).Select(i => Order("ITEM_" + i)).ToList();
        var plan = JObject.Parse(await (await h.Post(Url, "{}", "free-7")).Content.ReadAsStringAsync());
        var shown = plan["orders"]!.Select(o => o.Value<string>("itemTag")).ToHashSet();
        var hidden = Enumerable.Range(1, 12).Select(i => "ITEM_" + i).First(t => !shown.Contains(t));

        var response = await h.Post(Url + "/track", Track(plan.SelectToken("quote.quoteId")!.ToString(), hidden), "free-7");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(h.Goals, Is.Empty);
    }

    /// <summary>Invalid numbers are rejected instead of silently replaced by defaults.</summary>
    [TestCase("{\"budgetCoins\": -5}", "invalid_budget")]
    [TestCase("{\"budgetCoins\": NaN}", "invalid_budget")]
    [TestCase("{\"budgetCoins\": Infinity}", "invalid_budget")]
    [TestCase("{\"sellTaxRate\": -0.01}", "invalid_tax")]
    [TestCase("{\"sellTaxRate\": 0.11}", "invalid_tax")]
    [TestCase("{\"sellTaxRate\": NaN}", "invalid_tax")]
    [TestCase("{\"activeHorizonHours\": 0}", "invalid_horizon")]
    [TestCase("{\"activeHorizonHours\": -2}", "invalid_horizon")]
    [TestCase("{\"activeHorizonHours\": Infinity}", "invalid_horizon")]
    public async Task Plan_rejects_invalid_numbers_with_400(string body, string slug)
    {
        using var h = new Harness();

        var response = await h.Post(Url, body, "free-1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
        Assert.That(JObject.Parse(await response.Content.ReadAsStringAsync()).Value<string>("slug"), Is.EqualTo(slug));
        Assert.That(h.Fleet.Requests, Is.Empty);
    }

    /// <summary>The item names are global data and downloaded once, not per request.</summary>
    [Test]
    public async Task Plan_caches_item_names_across_requests()
    {
        using var h = new Harness();
        h.Fleet.Orders = _ => [Order("ITEM_A")];

        var first = JObject.Parse(await (await h.Post(Url, "{}", "premium-1")).Content.ReadAsStringAsync());
        var afterFirst = h.Items.Calls;
        await h.Post(Url, "{}", "premium-2");

        Assert.That(h.Items.Calls, Is.EqualTo(afterFirst));
        Assert.That(first["orders"]![0]!.Value<string>("itemName"), Is.EqualTo("Name of ITEM_A"));
    }

    /// <summary>Quote expiry is compared in UTC whatever kind the planner timestamp has.</summary>
    [Test]
    public void AsUtc_normalizes_all_kinds()
    {
        var utc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Multiple(() =>
        {
            Assert.That(BazaarPlanService.AsUtc(utc), Is.EqualTo(utc));
            Assert.That(BazaarPlanService.AsUtc(utc.ToLocalTime()).Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(BazaarPlanService.AsUtc(utc.ToLocalTime()), Is.EqualTo(utc));
            Assert.That(BazaarPlanService.AsUtc(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified)), Is.EqualTo(utc));
        });
    }

    /// <summary>Unexpected failures of the lookups are an outage, not a server error.</summary>
    [Test]
    public async Task Plan_lookup_failures_return_503_plan_unavailable()
    {
        using var h = new Harness();
        h.Purse = null;
        h.PurseFails = true;
        var purseDown = await h.Post(Url, "{}", "free-1");
        h.PurseFails = false;
        h.ResolverFails = true;
        var accountDown = await h.Post(Url, "{}", "free-1");

        Assert.Multiple(() =>
        {
            Assert.That(purseDown.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(accountDown.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        });
        Assert.That(JObject.Parse(await accountDown.Content.ReadAsStringAsync()).Value<string>("slug"), Is.EqualTo("plan_unavailable"));
    }

    private static string Track(string quoteId, string tag) => JsonConvert.SerializeObject(new { quoteId, itemTag = tag });

    private static async Task<string> Sample(Harness h, int user)
    {
        var plan = JObject.Parse(await (await h.Post(Url, "{}", "free-" + user)).Content.ReadAsStringAsync());
        return string.Join(",", plan["orders"]!.Select(o => o.Value<string>("itemTag")));
    }

    private static OrderRecommendationDto Order(string tag, string mode = "Active", int amount = 100, double entry = 100, double exit = 150) =>
        new(itemTag: tag, isSell: false, pricePerUnit: entry, amount: amount, mode: mode, originalQuantity: amount, entryPrice: entry,
            exitPrice: exit, sellTaxRate: 0.0125, taxSource: "Assumption", estimatedNetProfit: 1000, capitalLockHorizonHours: 1,
            deadlineSemantics: "FixedFromFirstAcceptedBuy",
            confidence: new FillConfidenceDto(eventId: "full_buy_sell_original_quantity_by_deadline_v1", probability: null, lowerBound: null,
                confidenceLevel: 0.95, status: "InsufficientEvidence", reasonCodes: ["NoProspectiveEvidence"]));

    private static BazaarPlayerOrder BuyOrder(string tag, long amount, long filled, double price, bool estimate = false, bool expired = false) =>
        new() { ItemTag = tag, ItemName = tag, IsSell = false, Amount = amount, FilledAmount = filled, PricePerUnit = price, Created = DateTime.UtcNow.AddMinutes(-10), IsEstimate = estimate, IsExpired = expired };

    private static BazaarPlayerOrder SellOrder(string tag, long amount, long filled, double price) =>
        new() { ItemTag = tag, ItemName = tag, IsSell = true, Amount = amount, FilledAmount = filled, PricePerUnit = price, Created = DateTime.UtcNow.AddMinutes(-5) };

    private sealed class Harness : IDisposable
    {
        private readonly IHost host;
        private readonly HttpClient client;
        public RecordingFleet Fleet { get; }
        public FakeOrders Orders { get; } = new();
        public double? Purse { get => purse.Value; set => purse.Value = value; }
        public bool PurseFails { get => purse.Fail; set => purse.Fail = value; }
        public bool ResolverFails { get => FakeResolver.Fail; set => FakeResolver.Fail = value; }
        public CountingItems Items { get; }
        public GoalList GoalList { get; }
        public List<Goal> Goals => GoalList.Goals;
        private readonly FakePurse purse = new() { Value = 3_000_000 };

        public Harness()
        {
            FakeResolver.Fail = false;
            Items = (CountingItems)(object)DispatchProxy.Create<IItemsApi, CountingItems>();
            Fleet = (RecordingFleet)(object)DispatchProxy.Create<IFleetApi, RecordingFleet>();
            var (goalService, _, _, _, _, list) = GoalServiceTests.Create();
            GoalList = list;
            host = new HostBuilder()
                .ConfigureWebHost(builder => builder
                    .UseTestServer()
                    .UseEnvironment(Environments.Production)
                    .UseContentRoot(Directory.GetCurrentDirectory())
                    .UseSetting("REDIS_HOST", "localhost")
                    .UseSetting("PREMIUM_CLIENT_IDS", "bazaar-plan-test")
                    .UseStartup<Startup>()
                    .ConfigureTestServices(services =>
                    {
                        services.RemoveAll<IHostedService>();
                        services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
                        services.AddAuthentication("CustomScheme")
                            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("CustomScheme", _ => { });
                        services.AddSingleton<PremiumTierService>(new TestPremiumTierService());
                        services.AddSingleton(DispatchProxy.Create<IBazaarFlipperApi, FlipControllerCacheTests.EmptyAsyncProxy>());
                        services.AddSingleton<IItemsApi>((IItemsApi)(object)Items);
                        services.AddSingleton<IFleetApi>((IFleetApi)(object)Fleet);
                        services.AddSingleton(goalService);
                        services.AddScoped<IBazaarUserOrders>(_ => Orders);
                        services.AddScoped<IVerifiedAccountResolver, FakeResolver>();
                        services.AddSingleton<IPurseLoader>(purse);
                    }))
                .Start();
            client = host.GetTestClient();
        }

        public async Task<HttpResponseMessage> Post(string url, string json, string token)
        {
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-ClientId", "bazaar-plan-test");
            if (token != null)
                request.Headers.Add("GoogleToken", token);
            return await client.SendAsync(request);
        }

        public static string FixturePath(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Fixtures", "fleet", name + ".json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("fleet fixture " + name + " not found above " + AppContext.BaseDirectory);
        }

        public void Dispose()
        {
            client.Dispose();
            host.Dispose();
        }
    }

    /// <summary>Records every allocation request and answers per trader with a canned fixture or generated orders.</summary>
    public class RecordingFleet : DispatchProxy
    {
        /// <summary>Requests received so far.</summary>
        public List<FleetAllocateRequest> Requests { get; } = new();
        /// <summary>Fixture answered for a trader id.</summary>
        public Dictionary<string, string> Canned { get; } = new();
        /// <summary>Orders answered for a trader without a fixture.</summary>
        public Func<string, List<OrderRecommendationDto>> Orders { get; set; } = _ => new();
        /// <summary>Unqualified investing candidates answered for a trader without a fixture.</summary>
        public Func<string, List<OrderRecommendationDto>> Candidates { get; set; } = _ => new();
        /// <summary>Lifetime of handed out quotes.</summary>
        public TimeSpan QuoteLifetime { get; set; } = TimeSpan.FromMinutes(3);
        /// <summary>Exception thrown instead of an answer.</summary>
        public Exception Fail { get; set; }

        /// <inheritdoc/>
        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name != nameof(IFleetApi.FleetAllocatePostAsync))
                throw new NotSupportedException(targetMethod.Name);
            var request = (FleetAllocateRequest)args[0];
            Requests.Add(request);
            if (Fail != null)
                return Task.FromException<FleetAllocateResponse>(Fail);
            return Task.FromResult(Answer(request));
        }

        private FleetAllocateResponse Answer(FleetAllocateRequest request)
        {
            var id = request.Traders.Single().Id;
            FleetAllocateResponse response;
            if (Canned.TryGetValue(id, out var fixture))
            {
                response = JsonConvert.DeserializeObject<FleetAllocateResponse>(File.ReadAllText(Harness.FixturePath(fixture)));
                response.Assignments[0].TraderId = id;
            }
            else
            {
                var budget = request.Traders.Single().BudgetCoins;
                var orders = Orders(id);
                response = new FleetAllocateResponse(status: "Ok", quote: new FleetQuoteDto(), assignments:
                [
                    new TraderAllocationDto(traderId: id, orders: orders, modesServed: request.Traders.Single().Strategies,
                        unqualifiedInvestingCandidates: Candidates(id), investingStatus: orders.Any(o => o.Mode == "Investing") ? "Allocated" : "Unavailable",
                        investingReasonCodes: orders.Any(o => o.Mode == "Investing") ? ["HiddenReason"] : ["InvestingCandidatesNotReady"],
                        portfolio: new PortfolioSummaryDto(budgetCoins: budget, spentCoins: orders.Sum(o => o.PricePerUnit * o.Amount),
                            remainingBudgetCoins: budget - orders.Sum(o => o.PricePerUnit * o.Amount), freeSlotsBefore: 21,
                            freeSlotsAfter: 21 - orders.Count, reserveOwner: "Caller", suppressedItemTags: []))
                ]);
            }
            response.Quote ??= new FleetQuoteDto();
            response.Quote.QuoteId = Guid.NewGuid().ToString();
            response.Quote.RequestId = request.RequestId;
            response.Quote.GeneratedAt = DateTime.UtcNow;
            response.Quote.ExpiresAt = DateTime.UtcNow + QuoteLifetime;
            return response;
        }
    }

    /// <summary>Counts item name downloads and names every tag of the plan.</summary>
    public class CountingItems : DispatchProxy
    {
        /// <summary>Number of downloads.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc/>
        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            Calls++;
            var resultType = targetMethod.ReturnType.GenericTypeArguments[0];
            var list = (System.Collections.IList)Activator.CreateInstance(resultType)!;
            var elementType = resultType.GenericTypeArguments[0];
            foreach (var tag in new[] { "ITEM_A", "ITEM_B" })
            {
                var ctor = elementType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                var item = ctor.Invoke(ctor.GetParameters().Select(p => p.Name!.ToLowerInvariant() switch
                {
                    "tag" => (object)tag,
                    "name" => "Name of " + tag,
                    _ => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null
                }).ToArray());
                list.Add(item);
            }
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [list])!;
        }
    }

    private sealed class FakeOrders : IBazaarUserOrders
    {
        public List<BazaarPlayerOrder> Orders { get; set; } = new();
        public bool Fail { get; set; }
        public List<string> Players { get; } = new();

        public Task<List<BazaarPlayerOrder>> Get(string userId, string playerName)
        {
            Players.Add(playerName);
            if (Fail)
                throw new InvalidOperationException("orders down");
            return Task.FromResult(Orders);
        }
    }

    private sealed class FakePurse : IPurseLoader
    {
        public double? Value { get; set; }
        public bool Fail { get; set; }
        public Task<double?> LoadPurse(string playerName) => Fail ? throw new InvalidOperationException("purse down") : Task.FromResult(Value);
    }

    private sealed class FakeResolver : IVerifiedAccountResolver
    {
        public static bool Fail { get; set; }
        public Task<string> GetVerifiedUuid(GoogleUser user) => Fail ? throw new InvalidOperationException("accounts down") : Task.FromResult("uuid-" + user.Id);
        public Task<string> GetName(string uuid) => Task.FromResult("Player" + uuid["uuid-".Length..]);
    }

    private sealed class TestPremiumTierService : PremiumTierService
    {
        public TestPremiumTierService() : base(null, null) { }

        public override Task<(GoogleUser user, string tier)> GetUserAndTier(ControllerBase controllerInstance)
        {
            var token = controllerInstance.Request.Headers["GoogleToken"].ToString();
            if (string.IsNullOrEmpty(token))
                return Task.FromResult<(GoogleUser, string)>((null, "anonymous"));
            var parts = token.Split('-');
            var tier = parts[0] == "premium" ? "premium" : parts[0] == "paid" ? "starter_premium" : "logged_in";
            return Task.FromResult((new GoogleUser { Id = int.Parse(parts[1]) }, tier));
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = Request.Headers["GoogleToken"].ToString();
            if (string.IsNullOrEmpty(token))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, token)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

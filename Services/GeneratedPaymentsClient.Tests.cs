using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Client;
using Coflnet.Payments.Client.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

public class GeneratedPaymentsClientTests
{
    private WebApplication app;
    private UserApi users;
    private ITierSlotsApi slots;
    private string response, path, body, query;
    private int calls;
    private HttpStatusCode status;

    [SetUp]
    public async Task Setup()
    {
        response = "";
        calls = 0;
        status = HttpStatusCode.OK;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        app = builder.Build();
        app.Run(async context =>
        {
            calls++;
            path = context.Request.Path;
            query = context.Request.QueryString.Value;
            body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(response);
        });
        await app.StartAsync();
        var config = new Configuration { BasePath = app.Urls.Single(), Timeout = TimeSpan.FromSeconds(10) };
        users = new UserApi(config);
        slots = new TierSlotsApi(config);
    }

    [TearDown]
    public async Task Teardown() => await app.DisposeAsync();

    [Test]
    public async Task CombinedOwnershipRoundTripPreservesProvenanceInOneRequest()
    {
        response = """
            {"premium_plus":{"productSlug":"premium_plus","expiresAt":"2099-01-01T00:00:00Z",
            "ownerId":"payer","slotId":42,"canManage":false}}
            """;
        var access = await users.UserUserIdOwnsDetailsPostAsync("friend", requestBody: ["premium_plus", "premium"]);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(path, Is.EqualTo("/User/friend/owns/details"));
        Assert.That(access["premium_plus"].CanManage, Is.False);
        Assert.That(access["premium_plus"].OwnerId, Is.EqualTo("payer"));
        Assert.That(access["premium_plus"].SlotId, Is.EqualTo(42));
    }

    [Test]
    public async Task ExtensionSendsSlotIdsWithExistingDeclarationToPurchasersCheckout()
    {
        await users.UserUserIdServicePurchaseDeclaredProductSlugPostAsync("payer", "premium_plus-slots-3",
            new ServicePurchaseRequest(reference: "unique-purchase", requestId: "declaration-id", slotIds: [1, 2, 3],
                locale: "en", declarationVersion: "1",
                immediatePerformanceRequested: true, withdrawalConsequenceAcknowledged: true));
        var json = JObject.Parse(body);
        Assert.That(path, Is.EqualTo("/User/payer/service/purchase-declared/premium_plus-slots-3"));
        Assert.That(json["slotIds"].ToObject<long[]>(), Is.EqualTo(new long[] { 1, 2, 3 }));
        Assert.That(json["requestId"].Value<string>(), Is.EqualTo("declaration-id"));
        Assert.That(json["immediatePerformanceRequested"].Value<bool>(), Is.True);
    }

    [Test]
    public async Task EntriesSendMinecraftScopeAndTierListInOneRequest()
    {
        response = "[]";
        await users.UserUserIdOwnsEntriesPostAsync("friend", "123456781234123412341234567890ab", ["premium_plus"]);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(path, Is.EqualTo("/User/friend/owns/entries"));
        Assert.That(query, Does.Contain("minecraftUuid=123456781234123412341234567890ab"));
        Assert.That(JArray.Parse(body).ToObject<string[]>(), Is.EqualTo(new[] { "premium_plus" }));
    }

    [Test]
    public async Task ReleasePreservesOptimisticConcurrencyVersion()
    {
        await slots.ApiTierSlotsOwnerOwnerIdIdAssignmentPutAsync("payer", 42, new TierSlotAssignment(varVersion: 7));
        var json = JObject.Parse(body);
        Assert.That(path, Is.EqualTo("/api/tier-slots/owner/payer/42/assignment"));
        Assert.That(json["version"].Value<long>(), Is.EqualTo(7));
        Assert.That(json.Value<string>("userId"), Is.Null);
        Assert.That(json.Value<string>("minecraftUuid"), Is.Null);
    }

    [Test]
    public void UnavailablePaymentsCannotBecomeAnAccessGrant()
    {
        response = "unavailable";
        status = HttpStatusCode.ServiceUnavailable;
        var error = Assert.ThrowsAsync<ApiException>(() => users.UserUserIdOwnsDetailsPostAsync("friend", requestBody: ["premium_plus"]));
        Assert.That(error.ErrorCode, Is.EqualTo(503));
    }
}

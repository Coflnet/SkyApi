using Coflnet.Payments.Client.Client;
using Coflnet.Sky.Core;
using Moq;
using NUnit.Framework;
using StackExchange.Redis;
using System.Linq;
using System.Threading.Tasks;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class PremiumControllerTests
{
    [TestCase("linkvertise", "linkvertise")]
    [TestCase("LinkVertise", "linkvertise")]
    [TestCase(" lootlabs ", "lootlabs")]
    public void AdProvider_IsExplicitlyAllowlisted(string provider, string expected)
    {
        Assert.That(
            PremiumController.TryNormalizeAdProvider(provider, out var normalized),
            Is.True);
        Assert.That(normalized, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("other")]
    public void AdProvider_RejectsUnsupportedValues(string provider)
    {
        Assert.That(
            PremiumController.TryNormalizeAdProvider(provider, out _),
            Is.False);
    }

    [TestCase("TRUE", true)]
    [TestCase(" true\n", true)]
    [TestCase("FALSE", false)]
    [TestCase("not true", false)]
    [TestCase("{\"completed\":true}", false)]
    public void LinkvertiseResponse_RequiresExactTrue(string response, bool expected)
    {
        Assert.That(
            PremiumController.IsSuccessfulLinkvertiseResponse(response),
            Is.EqualTo(expected));
    }

    [Test]
    public void AdCompletionToken_Requires64Characters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                PremiumController.IsValidAdCompletionToken(new string('a', 64)),
                Is.True);
            Assert.That(
                PremiumController.IsValidAdCompletionToken(new string('a', 63)),
                Is.False);
            Assert.That(
                PremiumController.IsValidAdCompletionToken(new string('a', 65)),
                Is.False);
        });
    }

    [TestCase("compensation", "ad-hash", -49, true)]
    [TestCase("starter_premium-hour", "ad-hash", -49, true)]
    [TestCase("starter_premium-hour", "ap-hash", -49, true)]
    [TestCase("other", "ad-hash", -49, false)]
    [TestCase("starter_premium-hour", "other", -49, false)]
    [TestCase("starter_premium-hour", "ad-hash", -50, false)]
    public void RecentAdReward_CoversLegacyAndDirectGrants(
        string productId,
        string reference,
        int ageMinutes,
        bool expected)
    {
        var now = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.That(
            PremiumController.IsRecentAdReward(
                productId,
                reference,
                now.AddMinutes(ageMinutes),
                now),
            Is.EqualTo(expected));
    }

    [TestCase("linkvertise")]
    [TestCase("lootlabs")]
    public async Task UnconfirmedProviderCallbackNeverCredits(string provider)
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var grantCalls = 0;

        var completed = await PremiumController.TryCompleteAdSession(
            false,
            database.Object,
            provider,
            new string('a', 64),
            new string('b', 64),
            _ =>
            {
                grantCalls++;
                return Task.CompletedTask;
            });

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.False);
            Assert.That(grantCalls, Is.Zero);
        });
        database.VerifyNoOtherCalls();
    }

    [TestCase("linkvertise")]
    [TestCase("lootlabs")]
    public async Task ConfirmedCallbackCreditsOnlyUserStoredInSession(string provider)
    {
        const string sessionOwner = "session-owner";
        var state = new string('a', 64);
        var database = new Mock<IDatabase>();
        var transaction = new Mock<ITransaction>();
        database
            .Setup(d => d.StringGetAsync(
                PremiumController.GetAdStateKey(provider, state),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(sessionOwner);
        database
            .Setup(d => d.CreateTransaction(It.IsAny<object>()))
            .Returns(transaction.Object);
        transaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        string creditedUser = null;

        var completed = await PremiumController.TryCompleteAdSession(
            true,
            database.Object,
            provider,
            state,
            new string('b', 64),
            userId =>
            {
                creditedUser = userId;
                return Task.CompletedTask;
            });

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.True);
            Assert.That(creditedUser, Is.EqualTo(sessionOwner));
        });
    }

    [TestCase("linkvertise")]
    [TestCase("lootlabs")]
    public async Task ReplayedConfirmationCannotCreditTwice(string provider)
    {
        var state = new string('a', 64);
        var database = new Mock<IDatabase>();
        var transaction = new Mock<ITransaction>();
        database
            .Setup(d => d.StringGetAsync(
                PremiumController.GetAdStateKey(provider, state),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync("session-owner");
        database
            .Setup(d => d.CreateTransaction(It.IsAny<object>()))
            .Returns(transaction.Object);
        transaction
            .SetupSequence(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        var grantCalls = 0;

        for (var attempt = 0; attempt < 2; attempt++)
            await PremiumController.TryCompleteAdSession(
                true,
                database.Object,
                provider,
                state,
                new string('b', 64),
                _ =>
                {
                    grantCalls++;
                    return Task.CompletedTask;
                });

        Assert.That(grantCalls, Is.EqualTo(1));
    }

    [Test]
    public void AdCallbackDoesNotAcceptAnAccountIdentifier()
    {
        var parameters = typeof(PremiumController)
            .GetMethod(nameof(PremiumController.Linkvertise))
            .GetParameters();

        Assert.Multiple(() =>
        {
            Assert.That(parameters.Any(p => p.Name == "state"), Is.True);
            Assert.That(parameters.Any(p => p.Name is "email" or "userId"), Is.False);
        });
    }

    [Test]
    public void ForwardPaymentErrors_ForwardsUpstreamMessage()
    {
        var upstream = new ApiException(
            400,
            "Error calling TopUpStripePost",
            """{"Message":"We could not verify your country from your IP address. Stripe payments are unavailable."}""");

        var error = Assert.Throws<CoflnetException>(() => PremiumController.ForwardPaymentErrors(upstream));

        Assert.That(error.Slug, Is.EqualTo("payment_error"));
        Assert.That(error.Message, Is.EqualTo(
            "We could not verify your country from your IP address. Stripe payments are unavailable."));
    }
}
#pragma warning restore CS1591

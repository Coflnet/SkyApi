using Coflnet.Payments.Client.Client;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class PremiumControllerTests
{
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

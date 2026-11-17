using System;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Controller;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

public class SubscriptionCheckoutRouterTests
{
    [TestCase(null, "lemonsqueezy")]
    [TestCase("", "lemonsqueezy")]
    [TestCase("Stripe", "stripe")]
    [TestCase(" PAYPAL ", "paypal")]
    public void Normalize_DefaultsToLemonSqueezyForOlderClients(string input, string expected)
    {
        Assert.That(SubscriptionCheckoutRouter.TryNormalize(input, out var provider), Is.True);
        Assert.That(provider, Is.EqualTo(expected));
    }

    [Test]
    public void Normalize_RejectsUnknownProvider()
    {
        Assert.That(SubscriptionCheckoutRouter.TryNormalize("bitcoin", out _), Is.False);
    }

    [TestCase("lemonsqueezy", "lemon")]
    [TestCase("stripe", "stripe")]
    [TestCase("paypal", "paypal")]
    public async Task Subscribe_CallsOnlyTheMatchingEndpoint(string provider, string expectedCall)
    {
        var api = new Mock<ITopUpApiAsync>();
        var options = new TopUpOptions { DiscountCode = "CODE" };
        var response = new TopUpIdResponse { DirectLink = "https://pay" };
        api.Setup(a => a.TopUpLemonsqueezySubscribePostAsync("1", "l_premium", options, 0, default)).ReturnsAsync(response);
        api.Setup(a => a.TopUpStripeSubscribePostAsync("1", "l_premium", options, 0, default)).ReturnsAsync(response);
        api.Setup(a => a.TopUpPaypalSubscribePostAsync("1", "l_premium", options, 0, default)).ReturnsAsync(response);

        var result = await SubscriptionCheckoutRouter.Subscribe(api.Object, provider, "1", "l_premium", options);

        Assert.That(result, Is.SameAs(response));
        api.Verify(a => a.TopUpLemonsqueezySubscribePostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TopUpOptions>(), 0, default), expectedCall == "lemon" ? Times.Once() : Times.Never());
        api.Verify(a => a.TopUpStripeSubscribePostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TopUpOptions>(), 0, default), expectedCall == "stripe" ? Times.Once() : Times.Never());
        api.Verify(a => a.TopUpPaypalSubscribePostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TopUpOptions>(), 0, default), expectedCall == "paypal" ? Times.Once() : Times.Never());
    }

    [Test]
    public void Subscribe_ThrowsForUnnormalizedProvider()
    {
        Assert.ThrowsAsync<ArgumentException>(() => SubscriptionCheckoutRouter.Subscribe(new Mock<ITopUpApiAsync>().Object, "x", "1", "s", new TopUpOptions()));
    }

    [TestCase("de", "DE")]
    [TestCase(" us ", "US")]
    [TestCase("DEU", null)]
    [TestCase("1;", null)]
    [TestCase(null, null)]
    public void NormalizeCountry_KeepsOnlyTwoLetterCodes(string input, string expected)
    {
        Assert.That(PremiumController.NormalizeCountry(input), Is.EqualTo(expected));
    }
}

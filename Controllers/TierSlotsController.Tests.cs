using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

public class TierSlotsControllerTests
{
    [Test]
    public async Task CatalogIncludesPrepaidAndRecurringSlotsFromTheirSeparateStores()
    {
        var slots = new Mock<ITierSlotsApi>();
        slots.Setup(api => api.ApiTierSlotsProductsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PurchaseableProduct(slug: "premium_plus-slots-4", cost: 27000,
                ownershipSeconds: 2419200, slotCount: 4, slotTier: "premium_plus")]);
        var products = new Mock<IProductsApi>();
        products.Setup(api => api.ProductsTopupGetAsync(It.IsAny<int?>(), int.MaxValue, false, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TopUpProduct { Slug = "l_premium-slots-4", Title = "Premium bundle", ProviderSlug = "lemonsqueezy",
                    Cost = 7200, SlotCount = 4, SlotTier = "premium", OwnershipSeconds = 2419200 },
                new TopUpProduct { Slug = "l_premium", ProviderSlug = "lemonsqueezy", SlotCount = 0 },
                new TopUpProduct { Slug = "stripe-slots", ProviderSlug = "stripe", SlotCount = 4 }
            ]);
        var controller = new TierSlotsController(null, slots.Object, null, null, products.Object);

        var catalog = await controller.GetProducts();

        Assert.That(catalog.Select(p => p.Slug), Is.EqualTo(new[] { "l_premium-slots-4", "premium_plus-slots-4" }));
        Assert.That(catalog[0].SlotCount, Is.EqualTo(4));
        Assert.That(catalog[0].SlotTier, Is.EqualTo("premium"));
        Assert.That(catalog[0].OwnershipSeconds, Is.EqualTo(2419200));
        Assert.That(catalog[0].Cost, Is.EqualTo(7200));
        products.Verify(api => api.ProductsTopupGetAsync(It.IsAny<int?>(), int.MaxValue, false, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

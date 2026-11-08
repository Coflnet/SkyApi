using System.Threading.Tasks;
using System.Linq;
using Coflnet.Sky.Bazaar.Client.Model;
using NUnit.Framework;
using AwesomeAssertions;
using Moq;
using Coflnet.Sky.Bazaar.Client.Api;

namespace Coflnet.Sky.Api.Services.Description.Tests;

/// <summary>Contains bazaar price updater tests.</summary>
[TestFixture]
public class BazaarPriceUpdaterTests
{
    /// <summary>Parses top buy order should return correct price.</summary>
    [Test]
    public void ParseTopBuyOrder_ShouldReturnCorrectPrice()
    {
        var description = "§8Fine Flour\n\n§aTop Orders:\n§8- §64,362.4 coins §7each | §a475§7x §7in §f1 §7order\n§8- §64,362.3 coins §7each | §a58,703§7x §7in §f1 §7order\n\n§eClick to setup Buy Order!";
        
        var price = BazaarPriceUpdater.ParsePrice(description);
        
        price.Should().Be(4362.4);
    }

    /// <summary>Parses cheapest sell offer should return correct price.</summary>
    [Test]
    public void ParseCheapestSellOffer_ShouldReturnCorrectPrice()
    {
        var description = "§8Fine Flour\n\n§aTop Offers:\n§8- §64,500.0 coins §7each | §a100§7x §7in §f1 §7offer\n\n§eClick to setup Sell Offer!";
        
        var price = BazaarPriceUpdater.ParsePrice(description);
        
        price.Should().Be(4500.0);
    }

    [Test]
    public async Task DescriptionStartsDirectHttpUploadWithoutWaitingForKafka()
    {
        var api = new Mock<IOrderBookApi>();
        var pending = new TaskCompletionSource<bool>();
        api.Setup(a => a.UpdateOrderBookAsync(It.IsAny<OrderBookUpdate>(), 0, It.IsAny<System.Threading.CancellationToken>())).Returns(pending.Task);
        var time = DateTime.UtcNow;
        var upload = BazaarPriceUpdater.UploadOrderBook("FINE_FLOUR",
            "§8- §64,362.4 coins §7each | §a58,703§7x", "§8- §64,500.0 coins §7each | §a100§7x", time, api.Object);
        api.Verify(a => a.UpdateOrderBookAsync(It.Is<OrderBookUpdate>(u => u.ItemTag == "FINE_FLOUR"
            && u.Timestamp == time && u.BuyOrders.Single().Amount == 58703
            && u.BuyOrders.Single().PricePerUnit == 4362.4 && u.SellOrders.Single().Amount == 100), 0, It.Is<System.Threading.CancellationToken>(c => c.CanBeCanceled)), Times.Once);
        Assert.That(upload.IsCompleted, Is.False);
        pending.SetResult(true);
        await upload;
    }

    [Test]
    public void PriceWithoutQuantityCannotCreateAFakePriceLevel()
    {
        Assert.That(BazaarPriceUpdater.ParseOrders("§8- §64,362.4 coins"), Is.Empty);
    }

    [Test]
    public async Task ExpiredUploadNeverReachesTheOldOrNewServer()
    {
        var api = new Mock<IOrderBookApi>(MockBehavior.Strict);
        await BazaarPriceUpdater.UploadOrderBook("WHEAT", "§8- §610.0 coins §7each | §a64§7x", null,
            DateTime.UtcNow.AddSeconds(-11), api.Object);
        api.VerifyNoOtherCalls();
    }

}

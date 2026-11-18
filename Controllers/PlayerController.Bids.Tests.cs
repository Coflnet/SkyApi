using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class PlayerControllerBidsTests
{
    [Test]
    public async Task GetPlayerBidsWithoutQueryStringDoesNotThrow()
    {
        var options = new DbContextOptionsBuilder<HypixelContext>().UseInMemoryDatabase(nameof(PlayerControllerBidsTests) + Guid.NewGuid()).Options;
        using var context = new HypixelContext(options);
        var controller = new PlayerController(context, null, null, null, null, null, null);

        var bids = await controller.GetPlayerBids(new string('a', 32), 0, null);

        Assert.That(bids, Is.Empty);
    }
}

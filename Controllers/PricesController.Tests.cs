using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class PricesControllerTests
{
    [TestCase("569774104c85", "569774104c85")]
    [TestCase("3b49ada2-3f4a-4aea-874b-e308b5733897", "e308b5733897")]
    public void TryGetItemUid_ExtractsLastTwelveHexCharacters(string itemUuid, string expectedUid)
    {
        var auction = new SaveAuction { FlatenedNBT = new() { ["uuid"] = itemUuid } };

        var found = PricesController.TryGetItemUid(auction, out var uid);

        Assert.That(found, Is.True);
        Assert.That(uid, Is.EqualTo(NBT.UidToLong(expectedUid)));
    }

    [Test]
    public void TryGetItemUid_UsesParserContextFallback()
    {
        var auction = new SaveAuction { Context = new() { ["itemUuid"] = "3b49ada2-3f4a-4aea-874b-e308b5733897" } };

        var found = PricesController.TryGetItemUid(auction, out var uid);

        Assert.That(found, Is.True);
        Assert.That(uid, Is.EqualTo(NBT.UidToLong("e308b5733897")));
    }

    [TestCase(null)]
    [TestCase("too-short")]
    [TestCase("not-hex-value")]
    public void TryGetItemUid_RejectsInvalidValues(string itemUuid)
    {
        var auction = new SaveAuction { FlatenedNBT = new() { ["uid"] = itemUuid } };

        Assert.That(PricesController.TryGetItemUid(auction, out _), Is.False);
    }
}
#pragma warning restore CS1591

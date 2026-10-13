using AwesomeAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Mod.Tests;

/// <summary>Tests for <see cref="ModCommandAllowlist"/>.</summary>
[TestFixture]
public class ModCommandAllowlistTests
{
    /// <summary>Every allowed kind is accepted and returned in canonical form.</summary>
    [TestCase("/viewauction 0123456789ABCDEF0123456789abcdef", "/viewauction 0123456789abcdef0123456789abcdef", CommandKind.ViewAuction)]
    [TestCase("  /BZ   Enchanted  Diamond ", "/bz Enchanted Diamond", CommandKind.Bazaar)]
    [TestCase("/recipe Hyper Catalyst", "/recipe Hyper Catalyst", CommandKind.Recipe)]
    [TestCase("/ah", "/ah", CommandKind.AuctionHouse)]
    [TestCase("/ah Steve_1", "/ah Steve_1", CommandKind.AuctionHouse)]
    [TestCase("/cofl bazaarsearch Enchanted Diamond", "/cofl bazaarsearch Enchanted Diamond", CommandKind.BazaarSearch)]
    [TestCase("/cofl bzorder 64 Enchanted Diamond", "/cofl bzorder 64 Enchanted Diamond", CommandKind.BazaarOrder)]
    [TestCase("/cofl bzorder search 64 Enchanted Diamond", "/cofl bzorder search 64 Enchanted Diamond", CommandKind.BazaarOrder)]
    [TestCase("/cofl goal Set Fishing 5", "/cofl goal set Fishing 5", CommandKind.Goal)]
    [TestCase("/warp forge", "/warp forge", CommandKind.WarpForge)]
    public void Accepts(string raw, string expected, CommandKind expectedKind)
    {
        ModCommandAllowlist.TryNormalize(raw, out var command, out var kind).Should().BeTrue();
        command.Should().Be(expected);
        kind.Should().Be(expectedKind);
    }

    /// <summary>Anything outside the allowlist or with smuggled characters is rejected.</summary>
    [TestCase("/bz x; /pay")]
    [TestCase("/op")]
    [TestCase("/bz x\n/pay")]
    [TestCase("/bz §cx")]
    [TestCase("/viewauction xyz")]
    [TestCase("/viewauction 0123456789abcdef0123456789abcde")]
    [TestCase("")]
    [TestCase(null)]
    public void Rejects(string raw)
    {
        ModCommandAllowlist.TryNormalize(raw, out var command, out _).Should().BeFalse();
        command.Should().BeNull();
    }

    /// <summary>Overlong input is rejected.</summary>
    [Test]
    public void Rejects_TooLong()
    {
        ModCommandAllowlist.TryNormalize("/bz " + new string('a', 100), out _, out _).Should().BeFalse();
    }

    /// <summary>Bazaar item name is extracted from the canonical command.</summary>
    [Test]
    public void BazaarItemName_ReturnsName()
    {
        ModCommandAllowlist.BazaarItemName("/bz Enchanted Diamond").Should().Be("Enchanted Diamond");
    }
}

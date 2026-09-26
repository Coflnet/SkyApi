using NUnit.Framework;
using SkyApi.Services.Description;

namespace Coflnet.Sky.Api.Services.Description.Tests
{
    /// <summary>Contains currency value parsing tests.</summary>
    public class CurrencyValueDisplayTests
    {
        private class TestBits : BitsCoinValue
        {
            public bool Parse(string[] desc, out int bits, out int lineId) => HasValue(desc, out bits, out lineId);
        }

        /// <summary>Lines without a leading color code used to parse as 0 bits and display "Coins per bit: Infinity".</summary>
        [TestCase("500 Bits", 500)]
        [TestCase("§b500 Bits", 500)]
        [TestCase("§r§b1,350 Bits", 1350)]
        [TestCase("§b§l15,000 §bBits", 15000)]
        public void ParsesBitCost(string line, int expected)
        {
            var found = new TestBits().Parse(["§7Cost", line], out var bits, out var lineId);
            Assert.That(found, Is.True);
            Assert.That(bits, Is.EqualTo(expected));
            Assert.That(lineId, Is.EqualTo(2));
        }

        /// <summary>A zero cost must not be treated as a value (would divide by zero).</summary>
        [Test]
        public void IgnoresZeroCost()
        {
            Assert.That(new TestBits().Parse(["§b0 Bits"], out _, out _), Is.False);
        }
    }
}

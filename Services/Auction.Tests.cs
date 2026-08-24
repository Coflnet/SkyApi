using System;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services
{
    /// <summary>Contains auction parsing tests.</summary>
    public class AuctionTests
    {
        /// <summary>Parses enchant enum.</summary>
        [Test]
        public void ParseEnchantEnum()
        {
            Assert.That(Enum.Parse<Enchantment.EnchantmentType>("ultimate_duplex"),Is.EqualTo(Enum.Parse<Enchantment.EnchantmentType>("ultimate_reiterate")));
        }
    }
}

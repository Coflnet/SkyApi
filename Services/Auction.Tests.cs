using System;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services
{
    public class AuctionTests
    {
        [Test]
        public void ParseEnchantEnum()
        {
            Assert.That(Enum.Parse<Enchantment.EnchantmentType>("ultimate_duplex"),Is.EqualTo(Enum.Parse<Enchantment.EnchantmentType>("ultimate_reiterate")));
        }
    }
}
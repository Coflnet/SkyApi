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
            Assert.AreEqual(Enum.Parse<Enchantment.EnchantmentType>("ultimate_duplex"), Enum.Parse<Enchantment.EnchantmentType>("ultimate_reiterate"));
        }
    }
}
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Coflnet.Sky.Items.Client.Model;
using Coflnet.Sky.Api.Helper;

namespace Coflnet.Sky.Api.Services.Tests
{
    public class ItemDeserializationTests
    {
        [Test]
        public void NumericFlags12_MapTo_AUCTION_AND_FIRE_SALE()
        {
            var json = "{\"category\":\"PET_SKIN\",\"tier\":\"UNKNOWN\",\"flags\":12,\"id\":7561,\"tag\":\"PET_SKIN_CROW_SNOW\",\"name\":null,\"iconUrl\":null,\"minecraftType\":null,\"modifiers\":null,\"descriptions\":null,\"firstSeen\":\"2025-12-20T17:00:00\"}";

            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new FlagsEnumConverter() }
            };

            var item = JsonConvert.DeserializeObject<Item>(json, settings);

            Assert.That(item, Is.Not.Null);
            Assert.That(item.Flags.HasValue, Is.True);
            var flags = item.Flags.Value;

            // Expect both AUCTION and FIRE_SALE to be set
            Assert.That(flags.HasFlag(ItemFlags.AUCTION), Is.True, "AUCTION flag should be set");
            Assert.That(flags.HasFlag(ItemFlags.FIRESALE), Is.True, "FIRE_SALE flag should be set");

            // numeric value should be 68 (64 + 4)
            Assert.That((int)flags, Is.EqualTo(68));
        }
    }
}

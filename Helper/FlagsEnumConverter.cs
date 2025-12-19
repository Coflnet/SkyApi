using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using Coflnet.Sky.Items.Client.Model;

namespace Coflnet.Sky.Api.Helper
{
    /// <summary>
    /// Converts ItemFlags enum values, handling both numeric (bitmask) and string representations
    /// </summary>
    public class FlagsEnumConverter : JsonConverter
    {
        private readonly StringEnumConverter stringEnumConverter = new StringEnumConverter();

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ItemFlags) || objectType == typeof(ItemFlags?);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            // Handle numeric values (bitmask)
            if (reader.TokenType == JsonToken.Integer)
            {
                var value = Convert.ToInt64(reader.Value);
                // Map legacy flag bits to current values if necessary.
                // Historically a different enum layout used bit 8 for a flag that
                // is now represented by bit 64 (AUCTION). Remap that bit so
                // deserialization yields the correct combined flags (e.g. 12 -> 68).
                const long legacyBitForAuction = 8; // legacy bit set in some payloads
                const long currentBitForAuction = 64; // expected bit for AUCTION
                if ((value & legacyBitForAuction) != 0)
                {
                    value = (value & ~legacyBitForAuction) | currentBitForAuction;
                }
                return (ItemFlags)value;
            }

            // Handle string values
            if (reader.TokenType == JsonToken.String)
            {
                var stringValue = reader.Value.ToString();
                
                // Try to parse as integer string
                if (long.TryParse(stringValue, out var longValue))
                {
                    return (ItemFlags)longValue;
                }

                // Fall back to string enum converter for enum names
                return stringEnumConverter.ReadJson(reader, objectType, existingValue, serializer);
            }

            throw new JsonSerializationException($"Unexpected token type {reader.TokenType} when parsing ItemFlags");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                // Write as numeric value (bitmask)
                writer.WriteValue((int)(ItemFlags)value);
            }
        }
    }
}

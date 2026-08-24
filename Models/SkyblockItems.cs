using System;
using System.Runtime.Serialization;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Models
{
    /// <summary>Represents a skyblock item.</summary>
    public class SkyblockItem
    {
        //
        // Summary:
        //     For how much this item sells at npc
        //
        // Value:
        //     For how much this item sells at npc
        /// <summary>Gets or sets the npc sell price.</summary>
        [DataMember(Name = "npcSellPrice", EmitDefaultValue = false)]
        public float NpcSellPrice { get; set; }
        //
        // Summary:
        //     minecraft type name of item (`MATERIAL` in the hypixel api)
        //
        // Value:
        //     minecraft type name of item (`MATERIAL` in the hypixel api)
        /// <summary>Gets or sets the minecraft type.</summary>
        [DataMember(Name = "minecraftType", EmitDefaultValue = true)]
        public string MinecraftType { get; set; }
        //
        // Summary:
        //     Fallback icon url
        //
        // Value:
        //     Fallback icon url
        /// <summary>Gets or sets the icon url.</summary>
        [DataMember(Name = "iconUrl", EmitDefaultValue = true)]
        public string IconUrl { get; set; }
        //
        // Summary:
        //     Default name this item is known by
        //
        // Value:
        //     Default name this item is known by
        /// <summary>Gets or sets the name.</summary>
        [DataMember(Name = "name", EmitDefaultValue = true)]
        public string Name { get; set; }
        //
        // Summary:
        //     Gets or Sets Tag
        /// <summary>Gets or sets the tag.</summary>
        [DataMember(Name = "tag", EmitDefaultValue = true)]
        public string Tag { get; set; }
        //
        // Summary:
        //     Gets or Sets Flags
        /// <summary>Gets or sets the flags.</summary>
        [DataMember(Name = "flags", EmitDefaultValue = false)]
        [JsonConverter(typeof(ForceDefaultConverter))]
        public Items.Client.Model.ItemFlags? Flags { get; set; }
        //
        // Summary:
        //     Gets or Sets Tier
        /// <summary>Gets or sets the tier.</summary>
        [DataMember(Name = "tier", EmitDefaultValue = false)]
        public Tier? Tier { get; set; }
        //
        // Summary:
        //     Gets or Sets Category
        /// <summary>Gets or sets the category.</summary>
        [DataMember(Name = "category", EmitDefaultValue = false)]
        public Items.Client.Model.ItemCategory? Category { get; set; }

        /// <summary>Represents a force default converter.</summary>
        public class ForceDefaultConverter : JsonConverter
        {
            /// <inheritdoc/>
            public override bool CanRead => false;
            /// <inheritdoc/>
            public override bool CanWrite => false;
            /// <inheritdoc/>
            public override bool CanConvert(Type objectType)
            {
                throw new NotImplementedException();
            }
            /// <inheritdoc/>
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
            /// <inheritdoc/>
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotImplementedException();
            }
        }
    }
}

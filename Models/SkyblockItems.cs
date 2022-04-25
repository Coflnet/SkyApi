using System.Runtime.Serialization;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Models
{
    public class SkyblockItem
    {
        //
        // Summary:
        //     For how much this item sells at npc
        //
        // Value:
        //     For how much this item sells at npc
        [DataMember(Name = "npcSellPrice", EmitDefaultValue = false)]
        public float NpcSellPrice { get; set; }
        //
        // Summary:
        //     minecraft type name of item (`MATERIAL` in the hypixel api)
        //
        // Value:
        //     minecraft type name of item (`MATERIAL` in the hypixel api)
        [DataMember(Name = "minecraftType", EmitDefaultValue = true)]
        public string MinecraftType { get; set; }
        //
        // Summary:
        //     Fallback icon url
        //
        // Value:
        //     Fallback icon url
        [DataMember(Name = "iconUrl", EmitDefaultValue = true)]
        public string IconUrl { get; set; }
        //
        // Summary:
        //     Default name this item is known by
        //
        // Value:
        //     Default name this item is known by
        [DataMember(Name = "name", EmitDefaultValue = true)]
        public string Name { get; set; }
        //
        // Summary:
        //     Gets or Sets Tag
        [DataMember(Name = "tag", EmitDefaultValue = true)]
        public string Tag { get; set; }
        //
        // Summary:
        //     Gets or Sets Flags
        [DataMember(Name = "flags", EmitDefaultValue = false)]
        public Items.Client.Model.ItemFlags? Flags { get; set; }
        //
        // Summary:
        //     Gets or Sets Tier
        [DataMember(Name = "tier", EmitDefaultValue = false)]
        public Tier? Tier { get; set; }
        //
        // Summary:
        //     Gets or Sets Category
        [DataMember(Name = "category", EmitDefaultValue = false)]
        public Items.Client.Model.ItemCategory? Category { get; set; }
    }
}
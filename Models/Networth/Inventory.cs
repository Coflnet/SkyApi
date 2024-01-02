using System.Text.Json.Serialization;

namespace Coflnet.Sky.Api.Models.Netowrth;

public class NetworthBreakDown
{
    public long FullValue { get; set; }
    public Dictionary<string, MemberValue> Member { get; set; } = new ();
}

public class MemberValue
{
    public long FullValue { get; set; }
    public Dictionary<string, double> ValuePerCategory { get; set; } = new ();
}

public class Profile
{
    [JsonPropertyName("profile_id")]
    public string profile_id { get; set; }

    //  [JsonPropertyName("community_upgrades")]
    //  public CommunityUpgrades community_upgrades { get; set; }

    [JsonPropertyName("members")]
    public Dictionary<string, Member> members { get; set; }

    [JsonPropertyName("cute_name")]
    public string cute_name { get; set; }

    [JsonPropertyName("selected")]
    public bool selected { get; set; }

    [JsonPropertyName("game_mode")]
    public string game_mode { get; set; }

    [JsonPropertyName("banking")]
    public Banking banking { get; set; }

    [JsonPropertyName("created_at")]
    public long? created_at { get; set; }
}

public class Banking
{
    [JsonPropertyName("balance")]
    public double balance { get; set; }
}

public class Member
{
    [JsonPropertyName("rift")]
    public Rift rift { get; set; }
    [JsonPropertyName("pets_data")]
    public PetsData pets_data { get; set; }
    [JsonPropertyName("inventory")]
    public Inventory inventory { get; set; }
    [JsonPropertyName("currencies")]
    public Currencies currencies { get; set; }

}

public class Currencies
{
    [JsonPropertyName("coin_purse")]
    public double coin_purse { get; set; }

    [JsonPropertyName("motes_purse")]
    public double motes_purse { get; set; }

    [JsonPropertyName("essence")]
    public Dictionary<string, EssenceAmount> essence { get; set; }
}
public class EssenceAmount
{
    [JsonPropertyName("current")]
    public int current { get; set; }
}
public class Rift
{
    [JsonPropertyName("inventory")]
    public Inventory inventory { get; set; }

}

public class Inventory
{
    [JsonPropertyName("inv_contents")]
    public InventoryElem inv_contents { get; set; }

    [JsonPropertyName("inv_armor")]
    public InventoryElem inv_armor { get; set; }

    [JsonPropertyName("ender_chest_contents")]
    public InventoryElem ender_chest_contents { get; set; }

    [JsonPropertyName("ender_chest_page_icons")]
    public List<object> ender_chest_page_icons { get; set; }

    [JsonPropertyName("equipment_contents")]
    public InventoryElem equipment_contents { get; set; }

    [JsonPropertyName("bag_contents")]
    public BagContents bag_contents { get; set; }

    [JsonPropertyName("personal_vault_contents")]
    public InventoryElem personal_vault_contents { get; set; }

    [JsonPropertyName("wardrobe_equipped_slot")]
    public int wardrobe_equipped_slot { get; set; }

    [JsonPropertyName("sacks_counts")]
    public Dictionary<string, long> sacks_counts { get; set; }

    [JsonPropertyName("wardrobe_contents")]
    public InventoryElem wardrobe_contents { get; set; }
    /// <summary>
    /// The backpack elements
    /// </summary>
    [JsonPropertyName("backpack_icons")]
    public Dictionary<string, InventoryElem> backpack_icons { get; set; }

    [JsonPropertyName("backpack_contents")]
    public Dictionary<string, InventoryElem> backpack_contents { get; set; }
}
public class BagContents
{
    [JsonPropertyName("potion_bag")]
    public InventoryElem potion_bag { get; set; }

    [JsonPropertyName("talisman_bag")]
    public InventoryElem talisman_bag { get; set; }

    [JsonPropertyName("fishing_bag")]
    public InventoryElem fishing_bag { get; set; }

    [JsonPropertyName("quiver")]
    public InventoryElem quiver { get; set; }
}

public class InventoryElem
{
    [JsonPropertyName("type")]
    public int type { get; set; }

    [JsonPropertyName("data")]
    public string data { get; set; }
}

public class Pet
{
    [JsonPropertyName("uuid")]
    public string uuid { get; set; }

    [JsonPropertyName("uniqueId")]
    public string uniqueId { get; set; }

    [JsonPropertyName("type")]
    public string type { get; set; }

    [JsonPropertyName("exp")]
    public double exp { get; set; }

    [JsonPropertyName("active")]
    public bool active { get; set; }

    [JsonPropertyName("tier")]
    public string tier { get; set; }

    [JsonPropertyName("heldItem")]
    public string heldItem { get; set; }

    [JsonPropertyName("candyUsed")]
    public int candyUsed { get; set; }

    [JsonPropertyName("skin")]
    public string skin { get; set; }

    [JsonPropertyName("extra")]
    public Dictionary<string, object> extra { get; set; }

    [JsonPropertyName("milestone")]
    public Dictionary<string, double> milestone { get; set; }

    [JsonPropertyName("total_exp_gained")]
    public double total_exp_gained { get; set; }
}

public class PetsData
{
    [JsonPropertyName("autopet")]
    public Autopet autopet { get; set; }

    [JsonPropertyName("pets")]
    public List<Pet> pets { get; set; }
}

public class Autopet
{
    [JsonPropertyName("rules_limit")]
    public int rules_limit { get; set; }
}

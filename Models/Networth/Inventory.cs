using System.Text.Json.Serialization;

namespace Coflnet.Sky.Api.Models.Netowrth;

/// <summary>Represents a net worth breakdown.</summary>
public class NetworthBreakDown
{
    /// <summary>Gets or sets the full value.</summary>
    public long FullValue { get; set; }
    /// <summary>Gets or sets the member.</summary>
    public Dictionary<string, MemberValue> Member { get; set; } = new ();
}

/// <summary>Represents a member value.</summary>
public class MemberValue
{
    /// <summary>Gets or sets the full value.</summary>
    public long FullValue { get; set; }
    /// <summary>Gets or sets the value per category.</summary>
    public Dictionary<string, double> ValuePerCategory { get; set; } = new ();
}

/// <summary>Represents a profile.</summary>
public class Profile
{
    /// <summary>Gets or sets the profile id.</summary>
    [JsonPropertyName("profile_id")]
    public string profile_id { get; set; }

    //  [JsonPropertyName("community_upgrades")]
    //  public CommunityUpgrades community_upgrades { get; set; }

    /// <summary>Gets or sets the members.</summary>
    [JsonPropertyName("members")]
    public Dictionary<string, Member> members { get; set; }

    /// <summary>Gets or sets the cute name.</summary>
    [JsonPropertyName("cute_name")]
    public string cute_name { get; set; }

    /// <summary>Gets or sets the selected.</summary>
    [JsonPropertyName("selected")]
    public bool selected { get; set; }

    /// <summary>Gets or sets the game mode.</summary>
    [JsonPropertyName("game_mode")]
    public string game_mode { get; set; }

    /// <summary>Gets or sets the banking.</summary>
    [JsonPropertyName("banking")]
    public Banking banking { get; set; }

    /// <summary>Gets or sets the created at.</summary>
    [JsonPropertyName("created_at")]
    public long? created_at { get; set; }
}

/// <summary>Represents banking data.</summary>
public class Banking
{
    /// <summary>Gets or sets the balance.</summary>
    [JsonPropertyName("balance")]
    public double balance { get; set; }
}

/// <summary>Represents a member.</summary>
public class Member
{
    /// <summary>Gets or sets the rift.</summary>
    [JsonPropertyName("rift")]
    public Rift rift { get; set; }
    /// <summary>Gets or sets the pets data.</summary>
    [JsonPropertyName("pets_data")]
    public PetsData pets_data { get; set; }
    /// <summary>Gets or sets the inventory.</summary>
    [JsonPropertyName("inventory")]
    public Inventory inventory { get; set; }
    /// <summary>Gets or sets the currencies.</summary>
    [JsonPropertyName("currencies")]
    public Currencies currencies { get; set; }

}

/// <summary>Represents currency balances.</summary>
public class Currencies
{
    /// <summary>Gets or sets the coin purse.</summary>
    [JsonPropertyName("coin_purse")]
    public double coin_purse { get; set; }

    /// <summary>Gets or sets the motes purse.</summary>
    [JsonPropertyName("motes_purse")]
    public double motes_purse { get; set; }

    /// <summary>Gets or sets the essence.</summary>
    [JsonPropertyName("essence")]
    public Dictionary<string, EssenceAmount> essence { get; set; }
}
/// <summary>Represents an essence amount.</summary>
public class EssenceAmount
{
    /// <summary>Gets or sets the current.</summary>
    [JsonPropertyName("current")]
    public int current { get; set; }
}
/// <summary>Represents a rift.</summary>
public class Rift
{
    /// <summary>Gets or sets the inventory.</summary>
    [JsonPropertyName("inventory")]
    public Inventory inventory { get; set; }

}

/// <summary>Represents an inventory.</summary>
public class Inventory
{
    /// <summary>Gets or sets the inv contents.</summary>
    [JsonPropertyName("inv_contents")]
    public InventoryElem inv_contents { get; set; }

    /// <summary>Gets or sets the inv armor.</summary>
    [JsonPropertyName("inv_armor")]
    public InventoryElem inv_armor { get; set; }

    /// <summary>Gets or sets the ender chest contents.</summary>
    [JsonPropertyName("ender_chest_contents")]
    public InventoryElem ender_chest_contents { get; set; }

    /// <summary>Gets or sets the ender chest page icons.</summary>
    [JsonPropertyName("ender_chest_page_icons")]
    public List<object> ender_chest_page_icons { get; set; }

    /// <summary>Gets or sets the equipment contents.</summary>
    [JsonPropertyName("equipment_contents")]
    public InventoryElem equipment_contents { get; set; }

    /// <summary>Gets or sets the bag contents.</summary>
    [JsonPropertyName("bag_contents")]
    public BagContents bag_contents { get; set; }

    /// <summary>Gets or sets the personal vault contents.</summary>
    [JsonPropertyName("personal_vault_contents")]
    public InventoryElem personal_vault_contents { get; set; }

    /// <summary>Gets or sets the wardrobe equipped slot.</summary>
    [JsonPropertyName("wardrobe_equipped_slot")]
    public int wardrobe_equipped_slot { get; set; }

    /// <summary>Gets or sets the sacks counts.</summary>
    [JsonPropertyName("sacks_counts")]
    public Dictionary<string, long> sacks_counts { get; set; }

    /// <summary>Gets or sets the wardrobe contents.</summary>
    [JsonPropertyName("wardrobe_contents")]
    public InventoryElem wardrobe_contents { get; set; }
    /// <summary>
    /// The backpack elements
    /// </summary>
    [JsonPropertyName("backpack_icons")]
    public Dictionary<string, InventoryElem> backpack_icons { get; set; }

    /// <summary>Gets or sets the backpack contents.</summary>
    [JsonPropertyName("backpack_contents")]
    public Dictionary<string, InventoryElem> backpack_contents { get; set; }
}
/// <summary>Represents a bag contents.</summary>
public class BagContents
{
    /// <summary>Gets or sets the potion bag.</summary>
    [JsonPropertyName("potion_bag")]
    public InventoryElem potion_bag { get; set; }

    /// <summary>Gets or sets the talisman bag.</summary>
    [JsonPropertyName("talisman_bag")]
    public InventoryElem talisman_bag { get; set; }

    /// <summary>Gets or sets the fishing bag.</summary>
    [JsonPropertyName("fishing_bag")]
    public InventoryElem fishing_bag { get; set; }

    /// <summary>Gets or sets the quiver.</summary>
    [JsonPropertyName("quiver")]
    public InventoryElem quiver { get; set; }
}

/// <summary>Represents an inventory elem.</summary>
public class InventoryElem
{
    /// <summary>Gets or sets the type.</summary>
    [JsonPropertyName("type")]
    public int type { get; set; }

    /// <summary>Gets or sets the data.</summary>
    [JsonPropertyName("data")]
    public string data { get; set; }
}

/// <summary>Represents a pet.</summary>
public class Pet
{
    /// <summary>Gets or sets the uuid.</summary>
    [JsonPropertyName("uuid")]
    public string uuid { get; set; }

    /// <summary>Gets or sets the unique id.</summary>
    [JsonPropertyName("uniqueId")]
    public string uniqueId { get; set; }

    /// <summary>Gets or sets the type.</summary>
    [JsonPropertyName("type")]
    public string type { get; set; }

    /// <summary>Gets or sets the exp.</summary>
    [JsonPropertyName("exp")]
    public double exp { get; set; }

    /// <summary>Gets or sets the active.</summary>
    [JsonPropertyName("active")]
    public bool active { get; set; }

    /// <summary>Gets or sets the tier.</summary>
    [JsonPropertyName("tier")]
    public string tier { get; set; }

    /// <summary>Gets or sets the held item.</summary>
    [JsonPropertyName("heldItem")]
    public string heldItem { get; set; }

    /// <summary>Gets or sets the candy used.</summary>
    [JsonPropertyName("candyUsed")]
    public int candyUsed { get; set; }

    /// <summary>Gets or sets the skin.</summary>
    [JsonPropertyName("skin")]
    public string skin { get; set; }

    /// <summary>Gets or sets the extra.</summary>
    [JsonPropertyName("extra")]
    public Dictionary<string, object> extra { get; set; }

    /// <summary>Gets or sets the milestone.</summary>
    [JsonPropertyName("milestone")]
    public Dictionary<string, double> milestone { get; set; }

    /// <summary>Gets or sets the total exp gained.</summary>
    [JsonPropertyName("total_exp_gained")]
    public double total_exp_gained { get; set; }
}

/// <summary>Represents a pets data.</summary>
public class PetsData
{
    /// <summary>Gets or sets the autopet.</summary>
    [JsonPropertyName("autopet")]
    public Autopet autopet { get; set; }

    /// <summary>Gets or sets the pets.</summary>
    [JsonPropertyName("pets")]
    public List<Pet> pets { get; set; }
}

/// <summary>Represents an autopet.</summary>
public class Autopet
{
    /// <summary>Gets or sets the rules limit.</summary>
    [JsonPropertyName("rules_limit")]
    public int rules_limit { get; set; }
}

using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Represents a profitable attribute-based crafting opportunity.
/// </summary>
public class AttributeFlip
{
    /// <summary>Item tag of the base item.</summary>
    public string Tag { get; set; }

    /// <summary>Display name resolved for the item.</summary>
    public string ItemName { get; set; }

    /// <summary>Identifier of the auction to purchase as the base.</summary>
    public string AuctionToBuy { get; set; }

    /// <summary>Coins required to buy the base auction.</summary>
    public long AuctionPrice { get; set; }

    /// <summary>Additional materials required to craft the upgrade.</summary>
    public List<AttributeFlipIngredient> Ingredients { get; set; } = new();

    /// <summary>Current state of the item before crafting.</summary>
    public AttributeFlipAuctionKey StartingKey { get; set; }

    /// <summary>Desired state of the item after applying upgrades.</summary>
    public AttributeFlipAuctionKey EndingKey { get; set; }

    /// <summary>Expected sale price after applying the upgrades.</summary>
    public long Target { get; set; }

    /// <summary>Estimated total crafting cost for the upgrades.</summary>
    public long EstimatedCraftingCost { get; set; }

    /// <summary>Timestamp when the flip was identified.</summary>
    public DateTime FoundAt { get; set; }

    /// <summary>Observed volume of relevant trades.</summary>
    public float Volume { get; set; }
}

/// <summary>
/// Represents an ingredient required for an attribute flip.
/// </summary>
public class AttributeFlipIngredient
{
    /// <summary>Item identifier for the ingredient.</summary>
    public string ItemId { get; set; }

    /// <summary>Attribute or component name consumed.</summary>
    public string AttributeName { get; set; }

    /// <summary>Quantity of the ingredient needed.</summary>
    public int Amount { get; set; }

    /// <summary>Estimated market price contribution for the ingredient.</summary>
    public double Price { get; set; }
}

/// <summary>
/// Represents an enchantment applied during an attribute flip.
/// </summary>
public class AttributeFlipEnchant
{
    /// <summary>Enchantment identifier.</summary>
    public string Type { get; set; }

    /// <summary>Enchantment level.</summary>
    public int Lvl { get; set; }
}

/// <summary>
/// Represents the state of an auction before or after crafting.
/// </summary>
public class AttributeFlipAuctionKey
{
    /// <summary>Name of the reforge applied to the item.</summary>
    public string Reforge { get; set; }

    /// <summary>Rarity tier of the item.</summary>
    public string Tier { get; set; }

    /// <summary>List of enchantments present on the item.</summary>
    public List<AttributeFlipEnchant> Enchants { get; set; } = new();

    /// <summary>Additional attribute modifiers applied to the item.</summary>
    public List<AttributeFlipModifier> Modifiers { get; set; } = new();

    /// <summary>Stack size of the item.</summary>
    public int Count { get; set; }
}

/// <summary>
/// Represents an attribute modifier key/value pair on the item.
/// </summary>
public class AttributeFlipModifier
{
    /// <summary>Name of the modifier.</summary>
    public string Key { get; set; }

    /// <summary>Value stored for the modifier.</summary>
    public string Value { get; set; }
}

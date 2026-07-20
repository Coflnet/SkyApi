using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Mirrors <see cref="Coflnet.Sky.Crafts.Client.Model.ProfitableCraft"/> field for field (same JSON property names)
/// but carries the enriched <see cref="CraftIngredientDto"/> ingredient list instead, so the frontend can
/// show per-ingredient craft savings without any breaking change to the existing response shape.
/// </summary>
[DataContract(Name = "ProfitableCraft")]
public class ProfitableCraftDto
{
    /// <summary>
    /// Gets or Sets ItemId
    /// </summary>
    [DataMember(Name = "itemId", EmitDefaultValue = true)]
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or Sets ItemName
    /// </summary>
    [DataMember(Name = "itemName", EmitDefaultValue = true)]
    public string ItemName { get; set; }

    /// <summary>
    /// Gets or Sets SellPrice
    /// </summary>
    [DataMember(Name = "sellPrice", EmitDefaultValue = false)]
    public double SellPrice { get; set; }

    /// <summary>
    /// Gets or Sets CraftCost
    /// </summary>
    [DataMember(Name = "craftCost", EmitDefaultValue = false)]
    public double CraftCost { get; set; }

    /// <summary>
    /// Gets or Sets BuyOrderCraftCost
    /// </summary>
    [DataMember(Name = "buyOrderCraftCost", EmitDefaultValue = false)]
    public double BuyOrderCraftCost { get; set; }

    /// <summary>
    /// Gets or Sets Ingredients, enriched with per-ingredient craft savings.
    /// </summary>
    [DataMember(Name = "ingredients", EmitDefaultValue = true)]
    public List<CraftIngredientDto> Ingredients { get; set; }

    /// <summary>
    /// Gets or Sets ReqCollection
    /// </summary>
    [DataMember(Name = "reqCollection", EmitDefaultValue = false)]
    public RequiredCollection ReqCollection { get; set; }

    /// <summary>
    /// Gets or Sets ReqSlayer
    /// </summary>
    [DataMember(Name = "reqSlayer", EmitDefaultValue = false)]
    public RequiredCollection ReqSlayer { get; set; }

    /// <summary>
    /// Gets or Sets ReqSkill
    /// </summary>
    [DataMember(Name = "reqSkill", EmitDefaultValue = false)]
    public RequiredSkill ReqSkill { get; set; }

    /// <summary>
    /// Gets or Sets Type
    /// </summary>
    [DataMember(Name = "type", EmitDefaultValue = true)]
    public string Type { get; set; }

    /// <summary>
    /// Gets or Sets Volume
    /// </summary>
    [DataMember(Name = "volume", EmitDefaultValue = false)]
    public double Volume { get; set; }

    /// <summary>
    /// Gets or Sets Median
    /// </summary>
    [DataMember(Name = "median", EmitDefaultValue = false)]
    public float Median { get; set; }

    /// <summary>
    /// Gets or Sets LastUpdated
    /// </summary>
    [DataMember(Name = "lastUpdated", EmitDefaultValue = false)]
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Maps a client <see cref="ProfitableCraft"/> to the enriched DTO, running every ingredient through
    /// <see cref="Commands.Shared.CraftSavingsCalculator"/>. All other fields are copied through verbatim.
    /// </summary>
    public static ProfitableCraftDto FromProfitableCraft(ProfitableCraft craft)
    {
        if (craft == null)
            return null;
        return new ProfitableCraftDto
        {
            ItemId = craft.ItemId,
            ItemName = craft.ItemName,
            SellPrice = craft.SellPrice,
            CraftCost = craft.CraftCost,
            BuyOrderCraftCost = craft.BuyOrderCraftCost,
            Ingredients = craft.Ingredients?.Select(CraftIngredientDto.FromIngredient).ToList(),
            ReqCollection = craft.ReqCollection,
            ReqSlayer = craft.ReqSlayer,
            ReqSkill = craft.ReqSkill,
            Type = craft.Type,
            Volume = craft.Volume,
            Median = craft.Median,
            LastUpdated = craft.LastUpdated
        };
    }
}

using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Mirrors <see cref="Coflnet.Sky.Crafts.Client.Model.Ingredient"/> field for field (same JSON property
/// names, so existing consumers keep working unchanged) and adds the backend-computed "craft savings"
/// signal that tells the frontend whether this ingredient should be subcrafted and how much that saves.
/// </summary>
[DataContract(Name = "Ingredient")]
public class CraftIngredientDto
{
    /// <summary>
    /// Gets or Sets ItemId
    /// </summary>
    [DataMember(Name = "itemId", EmitDefaultValue = true)]
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or Sets Count
    /// </summary>
    [DataMember(Name = "count", EmitDefaultValue = false)]
    public long Count { get; set; }

    /// <summary>
    /// Gets or Sets Cost
    /// </summary>
    [DataMember(Name = "cost", EmitDefaultValue = false)]
    public double Cost { get; set; }

    /// <summary>
    /// Gets or Sets BuyOrderCost
    /// </summary>
    [DataMember(Name = "buyOrderCost", EmitDefaultValue = false)]
    public double BuyOrderCost { get; set; }

    /// <summary>
    /// Gets or Sets CraftCost
    /// </summary>
    [DataMember(Name = "craftCost", EmitDefaultValue = false)]
    public double CraftCost { get; set; }

    /// <summary>
    /// Gets or Sets Type
    /// </summary>
    [DataMember(Name = "type", EmitDefaultValue = true)]
    public string Type { get; set; }

    /// <summary>
    /// How many coins are saved by crafting this ingredient yourself instead of buying it outright.
    /// 0 when the ingredient wasn't crafted or crafting isn't actually cheaper.
    /// </summary>
    [DataMember(Name = "craftSavings", EmitDefaultValue = false)]
    public double CraftSavings { get; set; }

    /// <summary>
    /// <see cref="CraftSavings"/> expressed as a percentage of the buy-order cost.
    /// </summary>
    [DataMember(Name = "craftSavingsPercent", EmitDefaultValue = false)]
    public double CraftSavingsPercent { get; set; }

    /// <summary>
    /// True if the crafting engine chose to craft this ingredient instead of buying it
    /// (mirrors <c>Type == "craft"</c>).
    /// </summary>
    [DataMember(Name = "isSubcraft", EmitDefaultValue = false)]
    public bool IsSubcraft { get; set; }

    /// <summary>
    /// Maps a client ingredient to the enriched DTO, computing the craft savings signal via
    /// <see cref="Commands.Shared.CraftSavingsCalculator"/>.
    /// </summary>
    public static CraftIngredientDto FromIngredient(Crafts.Client.Model.Ingredient ingredient)
    {
        var savings = Commands.Shared.CraftSavingsCalculator.Calculate(ingredient);
        return new CraftIngredientDto
        {
            ItemId = ingredient.ItemId,
            Count = ingredient.Count,
            Cost = ingredient.Cost,
            BuyOrderCost = ingredient.BuyOrderCost,
            CraftCost = ingredient.CraftCost,
            Type = ingredient.Type,
            CraftSavings = savings.CraftSavings,
            CraftSavingsPercent = savings.CraftSavingsPercent,
            IsSubcraft = savings.IsSubcraft
        };
    }
}

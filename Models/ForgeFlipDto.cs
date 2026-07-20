using System.Collections.Generic;
using System.Runtime.Serialization;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Mirrors <see cref="Coflnet.Sky.Crafts.Client.Model.ForgeFlip"/> field for field (same JSON property
/// names) but carries the enriched <see cref="ProfitableCraftDto"/> so the frontend can show per-ingredient
/// craft savings for forge flips too.
/// </summary>
[DataContract(Name = "ForgeFlip")]
public class ForgeFlipDto
{
    /// <summary>
    /// Gets or Sets CraftData
    /// </summary>
    [DataMember(Name = "craftData", EmitDefaultValue = false)]
    public ProfitableCraftDto CraftData { get; set; }

    /// <summary>
    /// Gets or Sets Duration
    /// </summary>
    [DataMember(Name = "duration", EmitDefaultValue = false)]
    public int Duration { get; set; }

    /// <summary>
    /// Gets or Sets RequiredHotMLevel
    /// </summary>
    [DataMember(Name = "requiredHotMLevel", EmitDefaultValue = false)]
    public int RequiredHotMLevel { get; set; }

    /// <summary>
    /// Gets or Sets ProfitPerHour
    /// </summary>
    [DataMember(Name = "profitPerHour", EmitDefaultValue = false)]
    public double ProfitPerHour { get; set; }

    /// <summary>
    /// Gets or Sets Requirements
    /// </summary>
    [DataMember(Name = "requirements", EmitDefaultValue = true)]
    public Dictionary<string, int> Requirements { get; set; }

    /// <summary>
    /// Maps a client <see cref="ForgeFlip"/> to the enriched DTO. All fields other than
    /// <see cref="CraftData"/> are copied through verbatim.
    /// </summary>
    public static ForgeFlipDto FromForgeFlip(ForgeFlip flip)
    {
        if (flip == null)
            return null;
        return new ForgeFlipDto
        {
            CraftData = ProfitableCraftDto.FromProfitableCraft(flip.CraftData),
            Duration = flip.Duration,
            RequiredHotMLevel = flip.RequiredHotMLevel,
            ProfitPerHour = flip.ProfitPerHour,
            Requirements = flip.Requirements
        };
    }
}

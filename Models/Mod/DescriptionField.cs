using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Coflnet.Sky.Api.Models.Mod;

/// <summary>
/// List of available fields
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum DescriptionField
{
    /// <summary>
    /// Display nothing (global)
    /// </summary>
    NONE,
    /// <summary>
    /// Display the lowest bin
    /// </summary>
    LBIN,
    /// <summary>
    /// Display the key used to get the lowest bin
    /// </summary>
    LBIN_KEY,
    /// <summary>
    /// Display the median price
    /// </summary>
    MEDIAN,
    /// <summary>
    /// Display the key used to get the median price
    /// </summary>
    MEDIAN_KEY,
    /// <summary>
    /// Display the volume
    /// </summary>
    VOLUME,
    /// <summary>
    /// Display the item tag
    /// </summary>
    TAG,
    /// <summary>
    /// Display the craft cost
    /// </summary>
    CRAFT_COST
}

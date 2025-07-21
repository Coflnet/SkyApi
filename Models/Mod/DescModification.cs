using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Coflnet.Sky.Api.Models.Mod;

/// <summary>
/// Response object instructing minecraft mods how to modify the description
/// </summary>
public class DescModification
{
    /// <summary>
    /// What type of modification to make
    /// </summary>
    /// <value></value>
    [JsonConverter(typeof(StringEnumConverter))]
    public ModType Type { get; set; }
    /// <summary>
    /// Extra field containing index to insert (int), or value to replace (string)
    /// </summary>
    /// <value></value>
    public int Line { get; set; }
    /// <summary>
    /// New value to add,insert, or replace something with
    /// </summary>
    /// <value></value>
    public string Value { get; set; }

    /// <summary>
    /// Defines the type of modification
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModType
    {
        /// <summary>
        /// No modification
        /// </summary>
        NONE,
        /// <summary>
        /// Insert <see cref="Value"/> at <see cref="Line"/>
        /// </summary>
        INSERT,
        /// <summary>
        /// Replace <see cref="Line"/> with <see cref="Value"/> 
        /// </summary>
        REPLACE,
        /// <summary>
        /// Append <see cref="Value"/> to the end
        /// </summary>
        APPEND,
        /// <summary>
        /// Delete line <see cref="Line"/>
        /// </summary>
        DELETE,
        /// <summary>
        /// Highlight line <see cref="Line"/> with RGB color <see cref="Value"/>
        /// </summary>
        HIGHLIGHT,
        /// <summary>
        /// Suggest a value to the user, which would be copied to the next open sign
        /// It follows the syntax `Last line text: value`
        /// </summary>
        SUGGEST
    }

    /// <summary>
    /// Creates a new instance of <see cref="DescModification"/>
    /// </summary>
    /// <param name="type"></param>
    /// <param name="target"></param>
    /// <param name="value"></param>
    public DescModification(ModType type, int target, string value)
    {
        Type = type;
        Line = target;
        Value = value;
    }
    /// <summary>
    /// Creates a new instance of <see cref="DescModification"/>
    /// </summary>
    /// <param name="value"></param>
    public DescModification(string value) : this(ModType.APPEND, 0, value)
    { }
    /// <summary>
    /// Creates a new instance of <see cref="DescModification"/>
    /// </summary>
    public DescModification()
    {
    }
}

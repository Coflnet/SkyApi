using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Coflnet.Sky.Api.Models.Mod;

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


    [JsonConverter(typeof(StringEnumConverter))]
    public enum ModType
    {
        NONE,
        INSERT,
        REPLACE,
        APPEND,
        DELETE
    }

    public DescModification(ModType type, int target, string value)
    {
        Type = type;
        Line = target;
        Value = value;
    }
    public DescModification(string value) : this(ModType.APPEND, 0, value)
    { }
    public DescModification()
    {
    }
}

#nullable enable
namespace Coflnet.Sky.Core;

/// <summary>Provides string extension methods.</summary>
public static class StringExtensions
{
    /// <summary>Converts the value to camel case.</summary>
    public static string ToCamelCase(this string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
            return str;
        
        return char.ToLowerInvariant(str[0]) + str.Substring(1);
    }
}
#nullable restore

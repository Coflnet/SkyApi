#nullable enable
namespace Coflnet.Sky.Core;

public static class StringExtensions
{
    public static string ToCamelCase(this string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
            return str;
        
        return char.ToLowerInvariant(str[0]) + str.Substring(1);
    }
}
#nullable restore
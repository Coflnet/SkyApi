namespace SkyApi.Services.Description;
/// <summary>
/// Wrapper for BitsCoinValue display
/// </summary>
public class BitsCoinValue : CurrencyValueDisplay
{
    /// <summary>
    /// Display suffix to search for to parse
    protected override string ValueSuffix => "Bits";
    /// <summary>
    /// Per unit name
    /// </summary>
    protected override string currencyName => "bit";
}
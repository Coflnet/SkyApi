namespace SkyApi.Services.Description;
/// <summary>Represents a generic currency display.</summary>
public class GenericCurrencyDisplay : CurrencyValueDisplay
{

    /// <summary>Gets the value suffix.</summary>
    protected override string ValueSuffix { get; }

    /// <summary>Gets the currency name.</summary>
    protected override string currencyName { get; }

    /// <summary>Initializes a new instance of the <see cref="GenericCurrencyDisplay"/> class.</summary>
    public GenericCurrencyDisplay(string suffix, string currencyName)
    {
        ValueSuffix = suffix;
        this.currencyName = currencyName;
    }
}

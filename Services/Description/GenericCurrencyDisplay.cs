namespace SkyApi.Services.Description;
public class GenericCurrencyDisplay : CurrencyValueDisplay
{

    protected override string ValueSuffix { get; }

    protected override string currencyName { get; }

    public GenericCurrencyDisplay(string suffix, string currencyName)
    {
        ValueSuffix = suffix;
        this.currencyName = currencyName;
    }
}

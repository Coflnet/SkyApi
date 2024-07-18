namespace SkyApi.Services.Description;
public class GenericCurrencyDisplay : CurrencyValueDisplay
{

    protected override string Value { get; }

    protected override string currencyName { get; }

    public GenericCurrencyDisplay(string value, string currencyName)
    {
        Value = value;
        this.currencyName = currencyName;
    }
}

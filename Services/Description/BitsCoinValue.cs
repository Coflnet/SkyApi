namespace SkyApi.Services.Description
{

    public class BitsCoinValue : CurrencyValueDisplay
    {

        protected override string ValueSuffix => "Bits";

        protected override string currencyName => "bit";
    }
}

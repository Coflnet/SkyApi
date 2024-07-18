using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Commands.MC;
using System.Linq;

namespace SkyApi.Services.Description;
public abstract class CurrencyValueDisplay : CustomModifier
{
    protected abstract string Value { get; }

    protected abstract string currencyName { get; }

    public void Apply(DataContainer data)
    {
        for (int i = 0; i < data.auctionRepresent.Count; i++)
        {
            var desc = data.auctionRepresent[i].desc;
            var price = data.PriceEst?[i];
            if (desc == null || price == null)
            {
                continue;
            }
            if (desc.Count() == 0)
            {
                continue;
            }
            if (price != null && price.Median != 0 && HasValue(desc, out int bits, out int lineId))
            {
                var prefix = price.ItemKey == price.MedianKey ? "" : "~";
                string text = $" {McColorCodes.GRAY}Coins per {currencyName}: {McColorCodes.AQUA}{prefix}{data.modService.FormatNumber((float)price.Median / bits)} ";
                var currentValue = desc.ElementAt(lineId - 1);
                data.mods[i].Insert(0, new DescModification(DescModification.ModType.REPLACE, lineId, currentValue + text));
            }
        }
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        return;
    }

    private bool HasValue(IEnumerable<string> description, out int bits, out int lineId)
    {
        bits = 1;
        lineId = 0;
        foreach (string descLine in description)
        {
            lineId++;
            if (!descLine.EndsWith(Value))
            {
                continue;
            }
            string commaSanitizedMatch = descLine.Substring(2, descLine.Length - 7).Replace(",", "");
            if (int.TryParse(commaSanitizedMatch, out bits))
            {
                return true;
            }
        }
        return false;
    }
}

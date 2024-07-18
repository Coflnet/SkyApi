using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Sniper.Client.Model;
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
            ProcessLine(data, i, desc, price);
        }
    }

    protected virtual void ProcessLine(DataContainer data, int i, string[] desc, PriceEstimate price)
    {
        if (price != null && price.Median != 0 && HasValue(desc, out int bits, out int lineId))
        {
            var prefix = price.ItemKey == price.MedianKey ? "" : "~";
            var formattedPrice = $"{McColorCodes.AQUA}{prefix}{data.modService.FormatNumber((float)price.Median / bits)}";
            ReplaceLine(data, i, lineId, formattedPrice);
        }
    }

    protected virtual void ReplaceLine(DataContainer data, int i, int lineId, string formattedPrice)
    {
        var desc = data.auctionRepresent[i].desc;
        string text = $"{desc.ElementAt(lineId - 1)} {McColorCodes.GRAY}Coins per {currencyName}: {formattedPrice} ";
        data.mods[i].Insert(0, new DescModification(DescModification.ModType.REPLACE, lineId, text));
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        return;
    }

    protected bool HasValue(IEnumerable<string> description, out int bits, out int lineId)
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
            string commaSanitizedMatch = descLine.Substring(2, descLine.Length - Value.Length - 3).Replace(",", "").Trim();
            if (int.TryParse(commaSanitizedMatch, out bits))
            {
                return true;
            }
        }
        return false;
    }
}

using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class AuctionValueSummary : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var sum = 0L;
        for (int i = 0; i < 9 * 4; i++)
        {
            var item = data.Items.ElementAtOrDefault(i);
            var price = data.PriceEst.ElementAtOrDefault(i);
            if (item?.Description == null)
                continue;
            if (item.ItemName.EndsWith("Go Back"))
            {
                break; // stop at "Go Back" item
            }
            var regexParsedPrice = Regex.Match(item.Description, @"Buy it now: §\d([\d,]+) coins");
            if (!regexParsedPrice.Success)
            {
                var isSold = Regex.IsMatch(item.Description, @"Sold for: §\d([\d,]+) coins");
                if (isSold)
                    data.mods[i].Add(new DescModification(DescModification.ModType.HIGHLIGHT, 1, "00ff00"));
                continue;
            }
            var value = long.Parse(regexParsedPrice.Groups[1].Value, System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture);
            if (value > 1_000_000)
                sum += value * 99 / 100;
            else
                sum += value;

            // highlight item based on lbin or not
            var lbin = price.Lbin.Price;
            if (lbin >= value || lbin == 0)
            {
                data.mods[i].Add(new DescModification(DescModification.ModType.HIGHLIGHT, 0, "50af50")); // green for lbin
                data.mods[i].Add(new DescModification(DescModification.ModType.INSERT, 1, $"{McColorCodes.DARK_GREEN}Is Lbin" + (price.SLbin.Price == 0 ? " (only offer)" : $" ({data.modService.FormatNumber(price.SLbin.Price - value)} lower than slbin)")));
            }
            else if (lbin < value)
            {
                data.mods[i].Add(new DescModification(DescModification.ModType.HIGHLIGHT, 0, "ff0000")); // red for not lbin
                data.mods[i].Add(new DescModification(DescModification.ModType.INSERT, 1, $"{McColorCodes.RED}Not lbin ({data.modService.FormatNumber(value - lbin)} higher than lbin)"));
            }
        }
        data.mods.Last().Insert(0, new DescModification(DescModification.ModType.REPLACE, 0, $"Auctions value: §6{data.modService.FormatNumber(sum)}"));
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        return;
    }
}

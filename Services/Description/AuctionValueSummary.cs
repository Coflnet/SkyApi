using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.Api.Services.Description;

public class AuctionValueSummary : CustomModifier
{
    public void Apply(DataContainer data)
    {
        var sum = 0L;
        foreach (var item in data.Items.Take(27))
        {
            if (item?.Description == null)
                continue;
            var regexParsedPrice = Regex.Match(item.Description, @"Buy it now: §\d([\d,]+) coins");
            if (!regexParsedPrice.Success)
                continue;
            var value = long.Parse(regexParsedPrice.Groups[1].Value);
            if (value > 1_000_000)
                sum += value * 99 / 100;
            else
                sum += value;
        }
        data.mods.Last().Insert(0, new DescModification(DescModification.ModType.REPLACE, 0, $"Auctions value: §6{data.modService.FormatNumber(sum)}"));
    }
}
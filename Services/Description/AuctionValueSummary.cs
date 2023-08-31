using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.Api.Services.Description;

public class AuctionValueSummary : CustomModifier
{
    public void Apply(DataContainer data)
    {
        var sum = 0L;
        foreach (var item in data.Items)
        {
            var regexParsedPrice = Regex.Match(item.ItemName, @"([\d,]+) coins").Groups[1];
            sum += long.Parse(regexParsedPrice.Value) * 98 / 100;
        }
        data.mods.Last().Insert(0, new DescModification($"Auctions value: {data.modService.FormatNumber(sum)}"));
    }
}
using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class FlipOnNextPage : CustomModifier
{
    public void Apply(DataContainer data)
    {
        var bestFlip = data.auctionRepresent.Zip(data.res).Take(9 * 6).Select(i =>
        {

            var price = i.First.desc.Where(x => x.StartsWith(McColorCodes.GRAY + "Buy it now: §"))
                .Select(x => long.Parse(x.Substring("xxBuy it now: §a".Length).Replace(" coins", "").Replace(",", ""), System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
            if (price == 0)
                return ((null, null), 0, 0);
            var profit = i.Second.Median - price;
            var lbinProfit = i.Second.Lbin.Price - price;
            if (i.Second.LbinKey != i.Second.ItemKey)
                lbinProfit = 0;
            if (i.Second.MedianKey != i.Second.ItemKey)
                profit = 0;
            return (i.First, profit, lbinProfit);
        }).OrderByDescending(a => a.profit + a.lbinProfit * 3).Where(a => a.profit > 0).FirstOrDefault();
        var index = 9 * 6 - 1;
        var targetItem = data.mods[index];
        var originalDesc = data.Items[index].Description;
        // move next page counter up
        targetItem.Insert(0, new DescModification(DescModification.ModType.REPLACE, 0, $"{McColorCodes.GREEN}Next page: {originalDesc.Split('\n').First()}"));
        if (bestFlip == default)
        {
            targetItem.Insert(0, new DescModification(DescModification.ModType.REPLACE, 1, $"No flips found, based on Coflnet data"));
            return;
        }
        Console.WriteLine(JsonConvert.SerializeObject(data.Items[index]));
        targetItem.Insert(0, new DescModification(DescModification.ModType.INSERT, 1, $"Best flip on page:"));
        targetItem.Insert(0, new DescModification(DescModification.ModType.INSERT, 1, bestFlip.First.auction?.ItemName));
        targetItem.Insert(1, new DescModification(DescModification.ModType.INSERT, 2, $"Lbin profit: {(bestFlip.lbinProfit == 0 ? "" : McColorCodes.GOLD)}{data.modService.FormatNumber(bestFlip.lbinProfit)}"));
        targetItem.Insert(1, new DescModification(DescModification.ModType.REPLACE, 2, $"Med profit: {McColorCodes.GOLD}{data.modService.FormatNumber(bestFlip.profit)}"));
    }
}
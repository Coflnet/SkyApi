using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class FlipOnNextPage : CustomModifier
{
    public void Apply(DataContainer data)
    {
        var bestFlip = data.auctionRepresent.Zip(data.res).Take(9 * 6).Select((i, index) =>
        {

            var price = i.First.desc.Where(x => x.StartsWith(McColorCodes.GRAY + "Buy it now: §"))
                .Select(x => long.Parse(x["xxBuy it now: §a".Length..].Replace(" coins", "").Replace(",", ""), System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault();
            if (price == 0)
                return ((null, null), 0, 0, index, null);
            var profit = FlipInstance.ProfitAfterFees(i.Second.Median, price);
            var lbinProfit = FlipInstance.ProfitAfterFees(i.Second.SLbin.Price, price);
            if (i.Second.LbinKey != i.Second.ItemKey)
                lbinProfit = 0;
            if (i.Second.MedianKey != i.Second.ItemKey)
                profit = 0;
            var seller = i.First.desc.Where(x => x.StartsWith(McColorCodes.GRAY + "Seller:")).FirstOrDefault();
            return (i.First, profit, lbinProfit, index, seller);
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

        targetItem.Add(new DescModification(DescModification.ModType.INSERT, 1, $"Best flip on page:"));
        targetItem.Add(new DescModification(DescModification.ModType.INSERT, 2, bestFlip.First.auction?.ItemName));
        targetItem.Add(new DescModification(DescModification.ModType.REPLACE, 3, $"Med profit: {McColorCodes.GOLD}{data.modService.FormatNumber(bestFlip.profit)}"));
        targetItem.Add(new DescModification(DescModification.ModType.INSERT, 4, bestFlip.seller));
        if (bestFlip.lbinProfit > 0)
            targetItem.Add(new DescModification(DescModification.ModType.INSERT, 3, $"Lbin profit: {(bestFlip.lbinProfit == 0 ? "" : McColorCodes.GOLD)}{data.modService.FormatNumber(bestFlip.lbinProfit)}"));

        // add highlight to item
        var item = data.mods[bestFlip.index];
        item.Insert(0, new DescModification(DescModification.ModType.INSERT, 1, $"{McColorCodes.DARK_GREEN}{McColorCodes.BOLD}BEST FLIP ON PAGE"));
    }
}
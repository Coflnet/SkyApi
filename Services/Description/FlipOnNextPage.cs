using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class FlipOnNextPage : CustomModifier
{
    public virtual void Apply(DataContainer data)
    {
        var bestFlip = GetFlipAble(data).OrderByDescending(a => a.profit + a.lbinProfit * 3).Where(a => a.profit > 0).FirstOrDefault();
        AddDescriptionTo(data, bestFlip, 9 * 6 - 1);
        AddDescriptionTo(data, bestFlip, 9 * 5 + 1);

        if (bestFlip == default)
            return;
        // add highlight to item
        var item = data.mods[bestFlip.index];
        item.Add(new DescModification($"{McColorCodes.DARK_GREEN}{McColorCodes.BOLD}BEST FLIP ON PAGE"));
        item.Add(new DescModification($"Med profit: {McColorCodes.GOLD}{data.modService.FormatNumber(bestFlip.profit)}"));
        if (bestFlip.lbinProfit > 0)
            item.Add(new DescModification($"Lbin profit: {(bestFlip.lbinProfit == 0 ? "" : McColorCodes.GOLD)}{data.modService.FormatNumber(bestFlip.lbinProfit)}"));
        Highlight(item);
    }

    protected void Highlight(List<DescModification> item)
    {
        item.Add(new DescModification(DescModification.ModType.HIGHLIGHT, 1, "009600"));
    }

    protected IEnumerable<((SaveAuction auction, string[] desc) First, long profit, long lbinProfit, int index, string seller)> GetFlipAble(DataContainer data)
    {
        return data.auctionRepresent.Zip(data.res).Take(9 * 6).Select((i, index) =>
        {
            long price = data.modService.GetAuctionPrice(i.First.desc);
            if (price == 0)
                return ((null, null), 0, 0, index, null);
            var profit = FlipInstance.ProfitAfterFees(i.Second.Median, price);
            var lbinProfit = FlipInstance.ProfitAfterFees(i.Second.SLbin?.Price ?? 0, price);
            if (i.Second.LbinKey != i.Second.ItemKey)
                lbinProfit = 0;
            if (i.Second.MedianKey != i.Second.ItemKey)
                profit = 0;
            var seller = i.First.desc.Where(x => x.StartsWith(McColorCodes.GRAY + "Seller:")).FirstOrDefault();
            return (i.First, profit, lbinProfit, index, seller);
        });
    }

    private static void AddDescriptionTo(DataContainer data, ((SaveAuction auction, IEnumerable<string> desc) First, long profit, long lbinProfit, int index, string seller) bestFlip, int index)
    {
        var targetItem = data.mods[index];
        var originalDesc = data.Items[index].Description;
        var itemName = data.Items[index].ItemName;
        // move next page counter up
        targetItem.Insert(0, new DescModification(DescModification.ModType.REPLACE, 0, $"{itemName}: {originalDesc?.Split('\n').First()}"));
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
    }
}
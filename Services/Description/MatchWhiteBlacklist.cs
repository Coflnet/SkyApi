using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class AuctionHouseHighlighting : CustomModifier
{
    private ConcurrentDictionary<string, SelfUpdatingValue<FlipSettings>> settingsCache = new();
    public virtual void Apply(DataContainer data)
    {
        if (!data.inventory.Settings.HighlightFilterMatch)
            return;

        var flipsRepresent = data.auctionRepresent.Zip(data.PriceEst).Take(9 * 6).Select((i, index) =>
        {
            return (NewMethod(data, i), index, i.Second);
        });
        foreach (var item in flipsRepresent)
        {
            var user = item.Item1.FirstOrDefault();
            if(user == null)
                continue;
            var estimate = item.Item3;
            if (estimate.Median * 5 < user.Auction.StartingBid && estimate.Lbin.Price * 5 < user.Auction.StartingBid)
            {
                Highlight(data, item, $"{McColorCodes.RED}Overpriced", "000000");
            }
        }
        if (data.accountInfo.ExpiresAt <= DateTime.UtcNow)
        {
            data.mods.Last().Add(new DescModification($"{McColorCodes.RED}Premium required for filter highlighting"));
            data.modService.logger.LogInformation(JsonConvert.SerializeObject(data.accountInfo));
            return;
        }
        var settings = settingsCache.GetOrAdd(data.accountInfo.UserId, (a) =>
        {
            Task.Run(async () =>
            {
                try
                {
                    var settings = await SelfUpdatingValue<FlipSettings>.Create(data.accountInfo.UserId, "flipSettings", () => new() { WhiteList = new() });
                    settingsCache[data.accountInfo.UserId] = settings;
                    data.modService.logger.LogInformation("Loaded flip settings for {userId}", data.accountInfo.UserId);
                }
                catch (System.Exception e)
                {
                    data.modService.logger.LogError(e, "Failed to load flip settings");
                }
            });

            data.mods.Last().Add(new DescModification($"{McColorCodes.YELLOW}(re-)Loading filter settings"));
            data.modService.logger.LogInformation("Loading flip settings for {userId}", data.accountInfo.UserId);
            return SelfUpdatingValue<FlipSettings>.CreateNoUpdate(() => new() { WhiteList = new() }).Result;
        });
        foreach (var flipSlot in flipsRepresent)
        {

            foreach (var flip in flipSlot.Item1)
            {
                if (flip.Auction != null && flip.Auction.NBTLookup == null)
                    flip.Auction.NBTLookup = NBT.CreateLookup(flip.Auction);
                var isMatch = settings.Value.MatchesSettings(flip);
                if (isMatch.Item1 && isMatch.Item2.StartsWith("white"))
                    Highlight(data, flipSlot, $"{McColorCodes.DARK_GREEN}{McColorCodes.BOLD}Matches whitelist", "009600");
                else if (isMatch.Item1)
                    Highlight(data, flipSlot, $"{McColorCodes.GREEN}Matches Coflnet filters", "00e600");
                else if (isMatch.Item2.StartsWith("black"))
                    Highlight(data, flipSlot, $"{McColorCodes.DARK_RED}Matches blacklist", "b60000");
                else if (isMatch.Item2.StartsWith("forced black"))
                    Highlight(data, flipSlot, $"{McColorCodes.DARK_RED}{McColorCodes.BOLD}Matches force blacklist", "960000");
            }
        }
    }
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        return;
    }

    private static void Highlight(DataContainer data, (IEnumerable<FlipInstance>, int index, Sniper.Client.Model.PriceEstimate est) flipSlot, string text, string color)
    {
        data.mods[flipSlot.index].Add(new DescModification(DescModification.ModType.HIGHLIGHT, 1, color));
        data.mods[flipSlot.index].Add(new DescModification(text));
    }

    private static IEnumerable<FlipInstance> NewMethod(DataContainer data, ((SaveAuction auction, string[] desc) First, Sniper.Client.Model.PriceEstimate Second) i)
    {
        if (i.First.auction == null)
            yield break;
        long price = data.modService.GetAuctionPrice(i.First.desc);
        i.First.auction.StartingBid = price;
        i.First.auction.Bin = price > 0;
        var user = FlipperService.LowPriceToFlip(new LowPricedAuction()
        {
            Auction = i.First.auction,
            Finder = LowPricedAuction.FinderType.USER,

        });
        user.SellerName = data.inventory.ChestName.Replace("You", "").Trim();
        yield return user;

        if (price == 0)
            yield break; // na base price
        var profit = FlipInstance.ProfitAfterFees(i.Second.Median, price);
        var lbinProfit = FlipInstance.ProfitAfterFees(i.Second.SLbin?.Price ?? 0, price);
        var seller = i.First.desc.Where(x => x.StartsWith(McColorCodes.GRAY + "Seller:")).FirstOrDefault();
        if (i.Second.LbinKey == i.Second.ItemKey && lbinProfit > 0)
            yield return FlipperService.LowPriceToFlip(new LowPricedAuction()
            {
                Auction = i.First.auction,
                Finder = LowPricedAuction.FinderType.SNIPER,
                TargetPrice = i.Second.SLbin.Price,
                DailyVolume = i.Second.Volume
            });
        if (i.Second.MedianKey != i.Second.ItemKey && profit > 0)
            yield return FlipperService.LowPriceToFlip(new LowPricedAuction()
            {
                Auction = i.First.auction,
                Finder = LowPricedAuction.FinderType.SNIPER_MEDIAN,
                TargetPrice = i.Second.Median,
                DailyVolume = i.Second.Volume
            });
        else if (profit > 0)
            yield return FlipperService.LowPriceToFlip(new LowPricedAuction()
            {
                Auction = i.First.auction,
                Finder = LowPricedAuction.FinderType.STONKS,
                TargetPrice = i.Second.Median,
                DailyVolume = i.Second.Volume
            });
    }
}
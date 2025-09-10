using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Client.Api;
using Coflnet.Sky.Sniper.Client.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class ListPriceRecommend : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var currentListPrice = data.Items[31]?.ItemName;
        var currentPrice = 0L;
        if (!string.IsNullOrEmpty(currentListPrice))
        {
            // Example input: "§fItem price: §65,000,000,000 coins"
            // Remove Minecraft color codes (e.g. "§f") then extract the first numeric group (with commas)
            var cleaned = System.Text.RegularExpressions.Regex.Replace(currentListPrice, "§.", "");
            var m = System.Text.RegularExpressions.Regex.Match(cleaned, @"([\d,]+)");
            if (m.Success)
            {
                var digits = m.Groups[1].Value.Replace(",", "");
                if (!long.TryParse(digits, out currentPrice))
                {
                    currentPrice = 0L;
                }
            }
        }

        string text = GetRecommendText(data.PriceEst[13], data.modService);
        data.mods[31].Insert(0, new DescModification(DescModification.ModType.INSERT, 1, text));


        var priceEst = data.PriceEst[13];
        if (priceEst == null || priceEst.Median == 0)
        {
            return;
        }
        var suggestedPrice = priceEst.Median;
        var priceSource = "median";
        var priceInfo = JsonConvert.DeserializeObject<PriceInfo>(data.Loaded[nameof(ListPriceRecommend)].Result);

        if ((priceEst == null || priceEst.Volume <= 0.5) && priceInfo.Recommended.Value <= 0)
        {
            data.mods.Add([
                new DescModification("Looks like this is not sold often"),
                new DescModification("SkyCofl won't fill in a price"),
                new DescModification($"{McColorCodes.GRAY}Estimated value: {McColorCodes.WHITE}" + ModDescriptionService.FormatPriceShort(priceEst.Median)),
            ]);
            return;
        }
        if (priceEst.MedianKey.Replace("&comb", "") != priceEst.ItemKey && priceInfo.Recommended.Value <= 0)
        {
            data.mods.Add([
                new DescModification("This item has little simlar sells,"),
                new DescModification("so we can't give you a price suggestion."),
                new DescModification($"{McColorCodes.GRAY}Estimated value: {McColorCodes.WHITE}" + ModDescriptionService.FormatPriceShort(priceEst.Median)),
            ]);
            return;
        }

        if (currentPrice / 10 > suggestedPrice)
        {
            data.mods.Add([
                new ($"{McColorCodes.RED}The current list price is"),
                new ($"{McColorCodes.RED}way above the estimated value{McColorCodes.OBFUSCATED}!{McColorCodes.RESET}"),
                new ("Double check to don't overpay fees!"),
                new ($"{McColorCodes.GRAY}SkyCofl value estimate is: {McColorCodes.WHITE}" + ModDescriptionService.FormatPriceShort(suggestedPrice)),
            ]);
            return;
        }

        if (data.inventory.Settings.PreferLbinInSuggestions && priceEst.Lbin.Price > 0)
        {
            priceSource = "lbin (as configured)";
            suggestedPrice = priceEst.Lbin.Price - 1;
        }
        if (priceInfo.Recommended != null && priceInfo.Recommended > 0 && !priceInfo.WasListedBefore && !priceInfo.WasChanged)
        {
            suggestedPrice = priceInfo.Recommended.Value;
            priceSource = "Flip estimate";
        }
        else if (priceInfo.LastListings.Count > 0)
        {
            suggestedPrice = priceInfo.LastListings.First();
            priceSource = McColorCodes.GREEN + "last listings of item";
        }
        else if (priceEst.Lbin.Price > priceEst.Median && priceEst.LbinKey == priceEst.ItemKey
            && priceEst.Volume > 3 && (priceEst.Volatility < 10 || priceEst.Volatility < 30 && priceEst.Volume > 15))
        {
            suggestedPrice = (long)(priceEst.Lbin.Price * 0.995);
            priceSource = "matching lbin";
        }

        var generalSellTime = TimeSpan.FromMinutes(priceEst.AvgSellTime);
        if (suggestedPrice > priceEst.Median)
            generalSellTime = generalSellTime.Multiply(2) + TimeSpan.FromMinutes(20);
        else if (suggestedPrice < priceEst.Median)
            generalSellTime = generalSellTime.Divide(1.2);
        var list = new List<DescModification>
        {
            new(McColorCodes.GREEN + "For this item, SkyCofl has a price" + McColorCodes.RESET),
            new($"{McColorCodes.GRAY}Est. time to sell: " + ModDescriptionService.FormatTime(generalSellTime)),
        };
        if (data.inventory.Settings.DisableSuggestions)
        {
            list.Add(new DescModification("Suggested price: " + ModDescriptionService.FormatPriceShort(suggestedPrice)));
            list.Add(new DescModification(McColorCodes.DARK_GRAY + "Enable automatic filling with"));
            list.Add(new DescModification("/cofl set noSuggest false"));
        }
        else
        {
            list.Add(new("We will fill in the price"));
            list.Add(new($"Based on: {McColorCodes.WHITE}{priceSource}{McColorCodes.GRAY}"));
            list.Add(
                new(DescModification.ModType.SUGGEST, 0, "starting bid: " + (priceSource.Contains("lbin") ? suggestedPrice - 1 : ModDescriptionService.FormatPriceShort(suggestedPrice - 1).ToLower())));
            list.Add(new DescModification(McColorCodes.DARK_GRAY + "Disable suggestions with"));
            list.Add(new DescModification("/cofl s noSuggest true"));
        }
        var hasAnyGems = data.auctionRepresent[13].auction.FlatenedNBT.Any(n => n.Value == "PERFECT" || n.Value == "FLAWLESS");
        if (hasAnyGems)
        {
            list.Add(new DescModification(McColorCodes.RED + "You should remove gems before selling!"));
            list.Add(new DescModification("People underpay for applied gems"));
        }
        DiHandler.GetService<ILogger<ListPriceRecommend>>().LogInformation("For {itemUuid} sending suggestion text {text}",
            data.auctionRepresent[13].auction.FlatenedNBT.GetValueOrDefault("uuid"), string.Join(", ", list.Select(l => l.Value)));
        data.mods.Add(list);
    }

    public static string GetRecommendText(PriceEstimate pricing, ModDescriptionService modService)
    {
        if (pricing == null || pricing.Median <= 4_000_000 || pricing.Volume == 0)
        {
            return $"No recommended instasell from Coflnet";
        }
        var isGuess = pricing.MedianKey != pricing.ItemKey && pricing.LbinKey != pricing.ItemKey;
        (double target, bool fromMedian) = SniperClient.InstaSellPrice(pricing);

        var formattedPrice = modService.FormatNumber(target);
        return $"{McColorCodes.GREEN}Instasell: {(isGuess ? $"{McColorCodes.GRAY}~" : "")}{McColorCodes.DARK_GREEN}{formattedPrice} {McColorCodes.WHITE}based on Coflnet {(fromMedian ? "median" : "lbin")}{(isGuess ? $" {McColorCodes.RED}(guess)" : "")}";
    }
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        var task = Task.Run(async () =>
        {
            var result = new PriceInfo();
            var targetAuction = preRequest.auctionRepresent[13].auction;
            var itemUuid = targetAuction?.FlatenedNBT.GetValueOrDefault("uuid");
            var playerUuid = await DiHandler.GetService<PlayerName.PlayerNameService>().GetUuid(preRequest.mcName);
            var lastListingsTask = LoadLastListings(targetAuction, playerUuid);
            await AddFlipRecommend(result, itemUuid, playerUuid);
            var recentListingsOfItem = await lastListingsTask;
            var sameItem = recentListingsOfItem.Where(o => itemUuid != null && o.FlatenedNBT.GetValueOrDefault("uuid") == itemUuid).ToList();
            var likelyPurchase = sameItem.FirstOrDefault(o => o.AuctioneerId != targetAuction.AuctioneerId);// newest auction of same item from different player
            if (likelyPurchase != null && ComparisonKey(likelyPurchase) != ComparisonKey(targetAuction))
            {
                result.WasChanged = true;
            }
            var sameitemListings = recentListingsOfItem.Where(o => o.AuctioneerId == targetAuction.AuctioneerId).ToList();

            result.LastListings = sameitemListings.Where(o => o.Start > DateTime.UtcNow.AddMinutes(-10) && ComparisonKey(o) == ComparisonKey(targetAuction)).Select(a => a.StartingBid).ToList();
            var itemUid = ModDescriptionService.GetUidFromString(targetAuction.FlatenedNBT?.GetValueOrDefault("uid"));
            result.WasListedBefore = sameitemListings.Any(o => o.FlatenedNBT.GetValueOrDefault("uuid") == itemUuid);

            return JsonConvert.SerializeObject(result);
        });
        preRequest.ToLoad.Add(nameof(ListPriceRecommend), task);
        return;
    }

    private string ComparisonKey(Core.SaveAuction likelyPurchase)
    {
        return string.Join(";", likelyPurchase.FlatenedNBT.OrderBy(f => f.Key).Select(kv => $"{kv.Key}:{kv.Value}")) + likelyPurchase.Enchantments.Count;
    }

    private async Task AddFlipRecommend(PriceInfo result, string itemUuid, string playerUuid)
    {
        if (itemUuid == null)
        {
            return;
        }
        var flipSent = await DiHandler.GetService<IFlipApi>().FlipForPlayerUuidItemItemUuidGetAsync(Guid.Parse(playerUuid), Guid.Parse(itemUuid));
        if (flipSent.TryOk(out var priceRecommend))
            result.Recommended = priceRecommend;
    }

    /// <summary>
    /// Holds information about pricing for an item.
    /// </summary>
    public class PriceInfo
    {
        public List<long> LastListings { get; set; } = new();
        public long? Recommended { get; set; }
        public bool WasChanged { get; set; }
        public bool WasListedBefore { get; set; }
    }

    private async Task<List<Core.SaveAuction>> LoadLastListings(Core.SaveAuction targetAuction, string playerUuid)
    {
        if (targetAuction == null || targetAuction.ItemName == null)
        {
            return new();
        }
        var itemId = DiHandler.GetService<ItemDetails>().GetItemIdForTag(targetAuction.Tag);
        using var scope = DiHandler.GetService<IServiceScopeFactory>().CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<HypixelContext>();

        var key = DiHandler.GetService<NBT>().GetKeyId("uid");
        var uidLong = ModDescriptionService.GetUidFromString(targetAuction.FlatenedNBT?.GetValueOrDefault("uuid"));
        var query = context.Auctions
            .Where(a => a.ItemId == itemId && a.End > DateTime.UtcNow.AddDays(-14)
                && (a.SellerId == context.Players.Where(p => p.UuId == playerUuid).Select(p => p.Id).FirstOrDefault()
                    || a.NBTLookup.Any(n => n.KeyId == key && n.Value == uidLong)))
            .AsSplitQuery().AsNoTracking()
            .Include(a => a.Enchantments)
            .Include(a => a.NbtData)
            .Take(10)
            .ToListAsync();
        return (await query).OrderByDescending(o => o.Start).ToList();
    }
}
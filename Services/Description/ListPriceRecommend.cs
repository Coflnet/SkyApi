using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Sniper.Client.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class ListPriceRecommend : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        string text = GetRecommendText(data.PriceEst[13], data.modService);
        data.mods[31].Insert(0, new DescModification(DescModification.ModType.INSERT, 1, text));

        var priceEst = data.PriceEst[13];
        if (priceEst == null || priceEst.Median == 0)
        {
            return;
        }
        if (priceEst == null || priceEst.Volume <= 1 || priceEst.MedianKey != priceEst.ItemKey)
        {
            data.mods.Add([
                new DescModification("Looks like this is not sold often"),
                new DescModification("SkyCofl won't fill in a price"),
                new DescModification($"{McColorCodes.GRAY}Estimated value: {McColorCodes.WHITE}" + ModDescriptionService.FormatPriceShort(priceEst.Median)),
            ]);
            return;
        }
        var list = new List<DescModification>
        {
            new(McColorCodes.GREEN + "For this item, SkyCofl has a price" + McColorCodes.RESET),
            new("We will fill in the price"),
            new("when you open the sign"),
            new($"{McColorCodes.GRAY}Est. time to sell: " +  ModDescriptionService.FormatTime(TimeSpan.FromMinutes(priceEst.AvgSellTime))),
        };
        if (data.inventory.Settings.DisableSuggestions)
        {
            list.Add(new DescModification("Suggested price: " + ModDescriptionService.FormatPriceShort(priceEst.Median)));
            list.Add(new DescModification("Enable automatic filling with"));
            list.Add(new DescModification("/cofl set loredisableSuggestions false"));
        }
        else
        {
            list.Add(
                new(DescModification.ModType.SUGGEST, 0, "starting bid: " + ModDescriptionService.FormatPriceShort(priceEst.Median - 1).ToLower()));
            list.Add(new DescModification(McColorCodes.DARK_GRAY + "Disable automatic suggestions with"));
            list.Add(new DescModification("/cofl set loreDisableSuggestions false"));
        }
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
        return;
    }
}
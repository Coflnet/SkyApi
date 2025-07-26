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
        if(priceEst == null || priceEst.Median == 0)
        {
            return;
        }
        if (priceEst == null || priceEst.Volume <= 4)
        {
            data.mods.Add([
                new DescModification("Looks like this is not sold often"),
                new DescModification("SkyCofl won't fill in a price")
            ]);
            return;
        }
        var list = new List<DescModification>
        {
            new(McColorCodes.GREEN + "For this item, SkyCofl has a price"),
            new(McColorCodes.RESET + "We will fill in the price"),
            new("when you open the sign"),
            new(DescModification.ModType.SUGGEST, 0, "starting bid: " + ModDescriptionService.FormatPriceShort(priceEst.Median -1))
        };
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
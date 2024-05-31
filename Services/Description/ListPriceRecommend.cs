using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Sniper.Client.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class ListPriceRecommend : CustomModifier
{
    public void Apply(DataContainer data)
    {
        string text = GetRecommendText(data.PriceEst[13], data.modService);
        data.mods[31].Insert(0, new DescModification(DescModification.ModType.INSERT, 1, text));
    }

    public static string GetRecommendText(PriceEstimate pricing, ModDescriptionService modService)
    {
        if (pricing == null || pricing.Median <= 4_000_000 || pricing.Volume == 0)
        {
            return $"No recommended instasell from Coflnet";
        }
        (double target, bool fromMedian) = SniperClient.InstaSellPrice(pricing);

        var formattedPrice = modService.FormatNumber(target);
        return $"{McColorCodes.GREEN}Instasell: {McColorCodes.DARK_GREEN}{formattedPrice} {McColorCodes.WHITE}based on Coflnet {(fromMedian ? "median" : "lbin")}";
    }
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        return;
    }
}
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Sniper.Client.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class ListPriceRecommend : CustomModifier
{
    public void Apply(DataContainer data)
    {
        string text = GetRecommendText(data.res[13], data.modService);
        data.mods[31].Insert(0, new DescModification(DescModification.ModType.INSERT, 1, text));
    }

    public static string GetRecommendText(PriceEstimate pricing, ModDescriptionService modService)
    {
        if (pricing.Median <= 4_000_000)
        {
            return $"No recommended instasell from Coflnet";
        }
        try
        {

            var deduct = 0.12;
            if (pricing.Median < 15_000_000)
                deduct = 0.18;
            if (pricing.Median > 150_000_000)
                deduct = 0.10;
            var fromMed = pricing.Median * (1 - deduct);
            var target = Math.Max(fromMed, Math.Min(pricing.Lbin.Price * (1 - deduct - 0.08), fromMed * 1.2));
            if (pricing.ItemKey != pricing.LbinKey)
                target = fromMed;

            var formattedPrice = modService.FormatNumber(target);
            return $"{McColorCodes.GREEN}Instasell: {McColorCodes.DARK_GREEN}{formattedPrice} {McColorCodes.WHITE}based on Coflnet data";
        }
        catch (Exception e)
        {
            modService.logger.LogError(e, "Error while calculating price {pricing}", JsonConvert.SerializeObject(pricing));
            return $"No recommended instasell from Coflnet";
        }
    }
}
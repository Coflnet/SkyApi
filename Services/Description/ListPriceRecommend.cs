using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class ListPriceRecommend : CustomModifier
{
    public void Apply(DataContainer data)
    {
        var text = $"No recommended instasell from Coflnet";
        if (data?.res[13]?.Median > 5_000_000)
        {
            var pricing = data.res[13];
            var deduct = 0.12;
            if (pricing.Median < 15_000_000)
                deduct = 0.18;
            if (pricing.Median > 150_000_000)
                deduct = 0.10;
            var fromMed = pricing.Median * (1 - deduct);
            var target = Math.Max(fromMed, Math.Min(pricing.Lbin.Price * (1 - deduct - 0.08), fromMed * 1.2));
            if (pricing.ItemKey != pricing.LbinKey)
                target = fromMed;

            var formattedPrice = data.modService.FormatNumber(target);
            text = $"{McColorCodes.GREEN}Instasell: {McColorCodes.DARK_GREEN}{formattedPrice} {McColorCodes.WHITE}based on Coflnet data";
        }
        data.mods[31].Insert(0, new DescModification(DescModification.ModType.INSERT, 1, text));
    }
}
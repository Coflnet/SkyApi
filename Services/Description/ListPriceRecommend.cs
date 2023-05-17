using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class ListPriceRecommend : CustomModifier
{
    public void Apply(DataContainer data)
    {
        var text = $"No recommended instasell from Coflnet";
        if (data?.res[13]?.Median > 0)
        {
            var formattedPrice = data.modService.FormatNumber(data.res[13].Median * 0.85);
            text = $"{McColorCodes.GREEN}Instasell: {McColorCodes.DARK_GREEN}{formattedPrice} {McColorCodes.WHITE}based on Coflnet data";
        }
        data.mods[31].Insert(0, new DescModification(DescModification.ModType.INSERT, 1, text));
    }
}
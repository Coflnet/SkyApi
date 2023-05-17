using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class ListPriceRecommend : CustomModifier
{
    public void Apply(DataContainer data)
    {
        data.mods[31].Insert(0, new DescModification(DescModification.ModType.INSERT, 1, 
            $"{McColorCodes.GREEN}Instasell: {McColorCodes.DARK_GREEN}{data.modService.FormatNumber(data.res[13].Median * 0.85)} {McColorCodes.WHITE}based on Coflnet data"));
    }
}
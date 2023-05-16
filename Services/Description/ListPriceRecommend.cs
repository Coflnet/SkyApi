using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class ListPriceRecommend : CustomModifier
{
    public void Apply(DataContainer data)
    {
        Console.WriteLine(data.auctionRepresent[41].auction.ItemName);
        data.mods[41].Insert(0, new DescModification(DescModification.ModType.INSERT, 1, 
            $"{McColorCodes.GREEN}Instasell: {McColorCodes.DARK_GREEN}{McColorCodes.BOLD}{data.modService.FormatNumber(data.res[13].Median * 0.85)} {McColorCodes.WHITE}according to Coflnet data"));
    }
}
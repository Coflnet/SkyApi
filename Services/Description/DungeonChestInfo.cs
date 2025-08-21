using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Api.Services.Description;

public class DungeonChestInfo : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var medValues = data.PriceEst.Take(5 * 9).Sum(x => x?.Median ?? data.GetItemprice(x?.ItemKey));
        var target = data.auctionRepresent;
        int coins = GetCostFromDungeonChest(target);

        var desc = new List<Models.Mod.DescModification>()
        {
            new(McColorCodes.GRAY + "This chest contains items worth " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(medValues)),
            new(McColorCodes.GRAY + "It costs " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(coins)),
            new(McColorCodes.GRAY + $"It would profit you {McColorCodes.WHITE}" + ModDescriptionService.FormatPriceShort(FlipInstance.ProfitAfterFees(medValues, coins))),
            new(McColorCodes.GRAY + "Please let us know what you think"),
            new(McColorCodes.GRAY + "about the estimate on SkyCofl discord!"),
        };
        data.mods.Add(desc);
    }

    public static int GetCostFromDungeonChest(List<(Core.SaveAuction auction, string[] desc)> target)
    {
        var costSlot = target[31].desc;
        int.TryParse(costSlot.First(c => c.EndsWith(" Coins")).Substring(2).Replace(",", "").Replace(" Coins", ""), out var coins);
        return coins;
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // none
    }
}

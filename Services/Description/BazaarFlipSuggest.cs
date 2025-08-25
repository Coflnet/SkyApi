using System.Globalization;
using System.Linq;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.Api.Services.Description;

public class BazaarFlipSuggest : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var modifications = new List<DescModification>();
        // {"Id":null,"ItemName":"§aFlip Order","Tag":null,"ExtraAttributes":null,"Enchantments":null,"Color":null,"Description":"§7Directly create a new sell offer for\n§7§a2§7x §7products.\n\n§7Current unit price: §6539.2 coins\n\n§6Top Offers:\n§8- §6559.0 coins §7each | §a4,330§7x §7from §f6 §7offers\n§8- §6560.5 coins §7each | §a24§7x §7from §f1 §7offer\n§8- §6560.7 coins §7each | §a151§7x §7from §f5 §7offers\n§8- §6560.8 coins §7each | §a47§7x §7from §f1 §7offer\n§8- §6561.1 coins §7each | §a34§7x §7from §f1 §7offer\n§8- §6561.3 coins §7each | §a342§7x §7from §f1 §7offer\n§8- §6561.4 coins §7each | §a125§7x §7from §f1 §7offer\n\n§7§eClick to pick new price!","Count":1}

        var flipItem = data.auctionRepresent[15].desc;
        var topPriceLine = flipItem.FirstOrDefault(l => l.StartsWith("§8- §6"));
        var topPriceText = topPriceLine?.Split(' ')[1].Replace("§6", "").Replace("§7", "");
        var previousPriceLine = flipItem.FirstOrDefault(l => l.StartsWith("§7Current unit price: §6"));
        var previousPrice = previousPriceLine?.Split(' ')[3].Replace("§6", "").Replace("§7", "");
        Console.WriteLine($"Bazaar flip suggest for {string.Join('\n', data.auctionRepresent[15].desc)} previous {previousPrice} top {topPriceText}");
        if (topPriceText != null && double.TryParse(topPriceText, CultureInfo.InvariantCulture, out var topPrice))
        {
            modifications.Add(new DescModification("§6Bazaar Flip Suggestion"));
            modifications.Add(new DescModification("For placing top sell order we"));
            modifications.Add(new DescModification(DescModification.ModType.SUGGEST, 0, $"{previousPrice}/u: {(topPrice - 0.1).ToString("F1", CultureInfo.InvariantCulture)}"));
            data.mods.Add(modifications);
        }
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {

    }
}
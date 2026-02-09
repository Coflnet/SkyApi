using System.Globalization;
using System.Linq;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.Api.Services.Description;

public class BazaarFlipSuggest : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var modifications = new List<DescModification>();
        var flipItem = data.auctionRepresent[15].desc;
        var topPriceLine = flipItem.FirstOrDefault(l => l.StartsWith("§8- §6"));
        var topPriceText = topPriceLine?.Split(' ')[1].Replace("§6", "").Replace("§7", "");
        var previousPriceLine = flipItem.FirstOrDefault(l => l.StartsWith("§7Current unit price: §6"));
        var previousPrice = previousPriceLine?.Split(' ')[3].Replace("§6", "").Replace("§7", "");
        // the decimal point is removed so 10,025.2 becomes 10,025, we need to fix that
        if (previousPrice != null && double.TryParse(previousPrice, CultureInfo.InvariantCulture, out var previousPriceValue) && previousPriceValue > 1_000)
            previousPrice = ((int)previousPriceValue).ToString("N0", CultureInfo.InvariantCulture);
        Console.WriteLine($"Bazaar flip suggest for {string.Join('\n', data.auctionRepresent[15].desc)} previous {previousPrice} top {topPriceText}");
        if (topPriceText != null && double.TryParse(topPriceText, CultureInfo.InvariantCulture, out var topPrice))
        {
            modifications.Add(new DescModification("§6Bazaar Flip Suggestion"));
            modifications.Add(new DescModification("For placing top sell order we"));
            var amount = (topPrice - 0.1).ToString("F1", CultureInfo.InvariantCulture);
            if (data.inventory.Settings.DisableSuggestions)
            {
                modifications.Add(new DescModification("Would suggest: " + amount));
            }
            else
                modifications.Add(new DescModification(DescModification.ModType.SUGGEST, 0, $"{previousPrice}/u: {amount}"));
            data.mods.Add(modifications);
        }
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {

    }
}
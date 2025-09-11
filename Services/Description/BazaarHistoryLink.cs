using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Api.Services;

namespace Coflnet.Sky.Api.Services.Description;

public class BazaarHistoryLink : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        // Check if the chest name contains ➜ symbol to filter for bazaar item screens
        if (data.inventory.ChestName == null || !data.inventory.ChestName.Contains("➜"))
            return;

        // Check if slot 11 (0-indexed) exists and contains "Buy Instantly" to confirm it's a bazaar item screen
        if (data.Items.Count <= 14 || data.Items[10] == null)
            return;

        var slot11Item = data.Items[10];
        if (slot11Item.ItemName == null || !slot11Item.ItemName.Contains("Buy Instantly"))
            return;

        // Check if slot 14 (0-indexed) exists to get the item tag
        if (data.Items.Count <= 13 || data.Items[13] == null || string.IsNullOrEmpty(data.Items[13].Tag))
            return;

        var slot13Item = data.Items[13];
        var itemTag = slot13Item.Tag;

        // Create a clickable link to open SkyCofl history for this item
        var loreBuilder = new LoreBuilder()
            .AddText("§7[§bopen on SkyCofl website§7]", 
                     "Click to view price history", 
                     $"https://sky.coflnet.com/item/{itemTag}");

        var display = new List<DescModification>
        {
            new(loreBuilder.Build())
        };

        data.mods.Add(display);
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // No pre-request modifications needed for this modifier
    }
}

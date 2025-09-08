using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Core;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Api.Services.Description;

public class BazaarInfo : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        if(data.inventory.Settings.DisableInfoIn?.Contains("Bazaar") ?? false)
            return;
        if (data.inventory.Version < 3)
                return; // not supported
        var bazaarItems = data.bazaarPrices.Keys.ToHashSet();
        var skip = data.accountInfo.Tier >= Commands.Shared.AccountTier.STARTER_PREMIUM && data.accountInfo.ExpiresAt > DateTime.UtcNow ? 0 : 3;
        var topCrafts = data.allCrafts.Values
                .Where(c => c.CraftCost > 0 && bazaarItems.Contains(c.ItemId) && c.Type == "crafting")
                .Select(c => new
                {
                    Craft = c,
                    SellPrice = data.GetItemprice(c.ItemId),
                    Profit = (long)(c.SellPrice - c.CraftCost)
                })
                .Where(c => c.Profit > 0)
                .OrderByDescending(c => c.Profit)
                .Skip(skip)
                .Take(3)
                .ToList();

        var display = new List<DescModification>();
        data.mods.Add(display);
        if (topCrafts.Count > 0)
            display.Add(new($"{McColorCodes.GOLD}SkyC{McColorCodes.AQUA}ofl {McColorCodes.GRAY}● §7Top Bazaar Crafts:"));
        foreach (var craft in topCrafts)
        {
            var line = new StringBuilder();
            line.Append("§a● §6");
            line.Append(craft.Craft.ItemName);
            line.Append(" " + McColorCodes.RED);
            line.Append(FormatCoins((long)craft.Craft.CraftCost));
            line.Append("§7 -> §6");
            line.Append(FormatCoins(craft.SellPrice));
            var builder = new LoreBuilder()
                .AddText(line.ToString(), $"Click to view craft\nestimated profit {FormatCoins(craft.Profit)}", $"/recipe {craft.Craft.ItemName}");
            display.Add(new(builder.Build()));
        }
        display.Add(new(new LoreBuilder().AddText("_","You can also drag this text by holding right click", "/cofl bazaarsearch obsidian").Build()));

        var bazaarFlips = data.Loaded[nameof(BazaarInfo)].Result;
        var deserializedFlips = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BazaarFlip>>(bazaarFlips);
        Console.WriteLine($"Got {deserializedFlips.Count} bazaar flips from bazaarflipper {bazaarFlips.Truncate(20)}");
        var biggestSpreads = deserializedFlips
        .OrderByDescending(b => b.ProfitPerHour)
        .Skip(skip)
        .Take(3).ToList();
        if (biggestSpreads.Count > 0)
            display.Add(new($"{McColorCodes.GOLD}SkyC{McColorCodes.AQUA}ofl {McColorCodes.GRAY}● §7Best flips on avg:"));
        foreach (var spread in biggestSpreads)
        {
            var name = data.itemTagToName.GetValueOrDefault(spread.ItemTag) ?? spread.ItemTag;
            var line = new StringBuilder();
            line.Append("§a● §6");
            line.Append(name);
            line.Append(" " +McColorCodes.RED);
            line.Append(FormatCoins((long)spread.SellPrice));
            line.Append("§7 -> " +McColorCodes.GREEN);
            line.Append(FormatCoins((long)spread.BuyPrice));;
            var builder = new LoreBuilder()
                .AddText(line.ToString(), $"Click to view {McColorCodes.AQUA}{name}", $"/bz {name}");
            display.Add(new(builder.Build()));
        }
    }

    private string FormatCoins(long coins)
    {
        if (coins >= 1_000_000_000)
            return $"{coins / 1_000_000_000.0:F1}B";
        if (coins >= 1_000_000)
            return $"{coins / 1_000_000.0:F1}M";
        if (coins >= 1_000)
            return $"{coins / 1_000.0:F1}K";
        return coins.ToString();
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        preRequest.ToLoad[nameof(BazaarInfo)] = Task.Run(async () =>
        {
            var bazaarFlipper = DiHandler.GetService<IBazaarFlipperApi>();
            var data = await bazaarFlipper.FlipsGetWithHttpInfoAsync();
            return data.RawContent;
        });
    }
}

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

/// <summary>Represents a bazaar info.</summary>
public class BazaarInfo : ICustomModifier
{
    // Kept as the documented key existing users already have in loreDisableInfoIn (from clicking the
    // old disable "x") instead of the auto-generated type name. ModDescriptionService handles the
    // skip + disable-button stamping centrally from this name.
    /// <summary>Gets the disable info name.</summary>
    public string DisableInfoName => "bazaar";

    private const string PurseLoadKey = "bazaarInfoPurse";

    /// <summary>A craft candidate ranked by profit, carrying the adjusted sell price used for display.</summary>
    internal record CraftCandidate(ProfitableCraft Craft, double SellPrice, long Profit);

    /// <summary>
    /// Selects the top affordable crafts ranked by profit. Filters out crafts the player's purse
    /// cannot cover (when <paramref name="purse"/> is given) before applying the tier skip/take, so
    /// both premium and free tiers still see up to 3 rows out of the entries they can actually afford.
    /// </summary>
    internal static List<CraftCandidate> SelectCrafts(IEnumerable<ProfitableCraft> crafts, ISet<string> bazaarItems, long? purse, int skip)
    {
        return crafts
                .Where(c => c.CraftCost > 0 && bazaarItems.Contains(c.ItemId) && c.Type == "crafting")
                .Select(c => new CraftCandidate(c, c.SellPrice - 1, (long)(c.SellPrice * 0.99 - c.CraftCost - 1)))
                .Where(c => c.Profit > 0)
                .Where(c => purse == null || c.Craft.CraftCost <= purse)
                .OrderByDescending(c => c.Profit)
                .Skip(skip)
                .Take(3)
                .ToList();
    }

    /// <summary>
    /// Selects the top affordable bazaar flips ranked by profit per hour. A flip is only affordable if
    /// the player can afford at least one unit at the buy-order price (<see cref="BazaarFlip.SellPrice"/>).
    /// </summary>
    internal static List<BazaarFlip> SelectFlips(IEnumerable<BazaarFlip> flips, long? purse, int skip)
    {
        return flips
            .Where(f => purse == null || f.SellPrice <= purse)
            .OrderByDescending(f => f.ProfitPerHour)
            .Skip(skip)
            .Take(3)
            .ToList();
    }

    /// <summary>
    /// The tier rule shared by the crafts and flips lists: premium tiers with a non-expired
    /// subscription see the top 3 entries (skip 0), everyone else sees entries ranked 4-6 (skip 3).
    /// </summary>
    internal static int TierSkip(AccountInfo accountInfo)
        => accountInfo.Tier >= AccountTier.STARTER_PREMIUM && accountInfo.ExpiresAt > DateTime.UtcNow ? 0 : 3;

    /// <inheritdoc/>
    /// <remarks>Both the crafts and flips lists are filtered to what the player's purse can afford
    /// (via <see cref="PurseLoadKey"/>) before the tier skip/take is applied.</remarks>
    public void Apply(DataContainer data)
    {
        if (data.inventory.Version < 3)
            return; // not supported
        var bazaarItems = data.bazaarPrices.Keys.ToHashSet();
        var skip = TierSkip(data.accountInfo);
        long? purse = null;
        if (data.Loaded != null && data.Loaded.TryGetValue(PurseLoadKey, out var purseTask)
            && long.TryParse(purseTask.Result, out var parsedPurse) && parsedPurse > 0)
            purse = parsedPurse;
        var topCrafts = SelectCrafts(data.allCrafts.Values, bazaarItems, purse, skip);

        var display = new List<DescModification>();
        data.mods.Add(display);
        if (topCrafts.Count > 0)
            display.Add(new LoreBuilder().AddText($"{McColorCodes.GOLD}SkyC{McColorCodes.AQUA}ofl {McColorCodes.GRAY}● §7Top Bazaar Crafts:  ").BuildLine());
        foreach (var craft in topCrafts)
        {
            var line = new StringBuilder();
            line.Append("§a● §6");
            line.Append(craft.Craft.ItemName.Truncate(22));
            line.Append(" " + McColorCodes.RED);
            line.Append(FormatCoins((long)craft.Craft.CraftCost));
            line.Append(craft.Craft.ItemName.Length > 22 ? "§7>§6" : "§7 -> §6");
            line.Append(FormatCoins((long)craft.SellPrice));
            var builder = CreateCraftLore(line.ToString(), craft.Craft.ItemName, craft.Profit, $"/recipe {craft.Craft.ItemName}", craft.SellPrice);
            display.Add(new(builder.Build()));
        }
        display.Add(new(new LoreBuilder().AddText("_", "You can also drag this text by holding right click", "/cofl bazaarsearch obsidian").Build()));

        var bazaarFlips = data.Loaded[nameof(BazaarInfo)].Result;
        var deserializedFlips = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BazaarFlip>>(bazaarFlips);
        Console.WriteLine($"Got {deserializedFlips.Count} bazaar flips from bazaarflipper {bazaarFlips.Truncate(20)}");
        var biggestSpreads = SelectFlips(deserializedFlips, purse, skip);
        if (biggestSpreads.Count > 0)
            display.Add(new($"{McColorCodes.GOLD}SkyC{McColorCodes.AQUA}ofl {McColorCodes.GRAY}● §7Best flips on avg:"));
        foreach (var spread in biggestSpreads)
        {
            var name = BazaarUtils.GetSearchValue(spread.ItemTag, data.itemTagToName.GetValueOrDefault(spread.ItemTag) ?? spread.ItemTag);
            var line = new StringBuilder();
            line.Append("§a● §6");
            line.Append(name.Truncate(22));
            line.Append(" " + McColorCodes.RED);
            line.Append(FormatCoins((long)spread.SellPrice));
            line.Append(name.Length > 22 ? "§7>§6" : "§7 -> §6");
            line.Append(FormatCoins((long)spread.BuyPrice));
            var command = data.inventory.Settings.NoCookie ? $"/cofl bazaarsearch {name}" : $"/bz {name}";
            var builder = new LoreBuilder()
                .AddText(line.ToString(), $"Click to view {McColorCodes.AQUA}{name}", command);
            display.Add(new(builder.Build()));
        }

        var bookmarks = data.inventory.Settings.BazaarBookmarks;
        if (bookmarks != null && bookmarks.Count > 0)
        {
            display.Add(new($"{McColorCodes.GOLD}SkyC{McColorCodes.AQUA}ofl {McColorCodes.GRAY}● §7Bookmarked items:"));
            foreach (var tag in bookmarks)
            {
                var name = BazaarUtils.GetSearchValue(tag, data.itemTagToName.GetValueOrDefault(tag) ?? tag);
                var line = new StringBuilder();
                line.Append("§a● §6");
                line.Append(name.Truncate(22));
                if (data.bazaarPrices.TryGetValue(tag, out var price))
                {
                    line.Append(" " + McColorCodes.RED);
                    line.Append(FormatCoins((long)price.SellPrice));
                    line.Append(name.Length > 22 ? "§7>§6" : "§7 -> §6");
                    line.Append(FormatCoins((long)price.BuyPrice));
                }
                var command = data.inventory.Settings.NoCookie ? $"/cofl bazaarsearch {name}" : $"/bz {name}";
                var builder = new LoreBuilder()
                    .AddText(line.ToString(), $"Click to view {McColorCodes.AQUA}{name}\n{McColorCodes.GRAY}Manage bookmarks on the bazaar item page", command);
                display.Add(new(builder.Build()));
            }
        }
    }

    // add these helper methods to the class
    private LoreBuilder CreateCraftLore(string rawLine, string itemName, long profit, string command, double sellPrice)
    {
        var hover = $"Click to view craft of {itemName}\nestimated profit {FormatCoins(profit)}{McColorCodes.GRAY}, sellorder at {McColorCodes.GOLD}{FormatCoins((long)(sellPrice - 0.1))}";
        return new LoreBuilder().AddText(rawLine, hover, command);
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

    /// <inheritdoc/>
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        if (!string.IsNullOrWhiteSpace(preRequest.mcName))
            preRequest.ToLoad[PurseLoadKey] = InstantBuyMaxAmount.LoadPurse(preRequest.mcName);
        preRequest.ToLoad[nameof(BazaarInfo)] = Task.Run(async () =>
        {
            try
            {
                var bazaarFlipper = DiHandler.GetService<IBazaarFlipperApi>();
                var data = await bazaarFlipper.FlipsGetWithHttpInfoAsync();
                return data.RawContent;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error fetching bazaar flips: " + ex);
                return "[]";
            }
        });
    }
}

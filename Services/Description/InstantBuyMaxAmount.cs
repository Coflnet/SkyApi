using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// On the "&lt;item&gt; ➜ Instant Buy" screen this suggests the maximum amount the player can
/// afford. The affordable amount is derived from the players purse (loaded from player state)
/// and the instant buy price, then capped by the amount the Custom Amount button currently
/// allows. The result is offered as a sign suggestion so right-clicking the Custom Amount
/// button pre-fills it, and shown as an info line on the button.
/// </summary>
public class InstantBuyMaxAmount : ICustomModifier
{
    private const string PurseLoadKey = "instantBuyPurse";

    /// <inheritdoc/>
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        if (!IsInstantBuy(preRequest.inventory?.ChestName) || string.IsNullOrWhiteSpace(preRequest.mcName))
            return;
        // load the purse in the background so it is ready by the time Apply runs
        preRequest.ToLoad[PurseLoadKey] = LoadPurse(preRequest.mcName);
    }

    private static async Task<string> LoadPurse(string name)
    {
        try
        {
            var api = DiHandler.GetService<PlayerState.Client.Api.IPlayerStateApi>();
            var currencies = await api.PlayerStatePlayerIdCurrenciesGetAsync(name);
            return currencies?.Purse.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch (Exception)
        {
            // never let a failed purse load break the whole description computation
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public void Apply(DataContainer data)
    {
        if (!IsInstantBuy(data.inventory?.ChestName))
            return;
        if (data.Loaded == null || !data.Loaded.TryGetValue(PurseLoadKey, out var purseTask))
            return;
        if (!long.TryParse(purseTask.Result, out var purse) || purse <= 0)
            return;

        var customSlot = FindCustomAmountSlot(data);
        if (customSlot < 0)
            return;
        var itemTag = FindBazaarItemTag(data);
        if (itemTag == null)
            return;
        // instant buy pays the lowest sell offer (the sell price in this codebase)
        var pricePerUnit = data.GetItemprice(itemTag, useBuyOrderPrices: false);
        if (pricePerUnit <= 0)
            return;

        var affordable = purse / pricePerUnit; // integer division -> floor
        var maxFromItem = ParseMaxAmount(data.Items[customSlot]);
        var suggested = CapAmount(affordable, maxFromItem);
        if (suggested <= 0)
            return;

        // make sure a mod list exists for the custom amount slot
        while (data.mods.Count <= customSlot)
            data.mods.Add(new());

        var hover = $"You can afford {McColorCodes.YELLOW}{affordable:N0}{McColorCodes.GRAY} with your purse of {McColorCodes.GOLD}{purse:N0}{McColorCodes.GRAY} coins";
        if (maxFromItem.HasValue && affordable > maxFromItem.Value)
            hover += $"\n{McColorCodes.GRAY}capped at the {McColorCodes.YELLOW}{maxFromItem.Value:N0}{McColorCodes.GRAY} this menu allows";
        hover += $"\n{McColorCodes.AQUA}Right-Click the Custom Amount to fill it in";

        var loreBuilder = new LoreBuilder().AddText(
            $"{McColorCodes.GRAY}[{McColorCodes.GREEN}buy max {McColorCodes.YELLOW}{suggested:N0}{McColorCodes.GRAY}]",
            hover);
        data.mods[customSlot].Add(loreBuilder.BuildLine());

        // suggest the amount so it is typed into the sign when the player opens it.
        // The mod matches the text before ": " against the sign's 4th line, which reads "to order".
        data.mods[customSlot].Add(new DescModification(DescModification.ModType.SUGGEST, 0, $"to order: {suggested}"));
    }

    internal static long CapAmount(long affordable, long? maxFromItem)
        => maxFromItem.HasValue ? Math.Min(affordable, maxFromItem.Value) : affordable;

    internal static bool IsInstantBuy(string chestName)
        => chestName != null && chestName.Contains('➜') && chestName.TrimEnd().EndsWith("Instant Buy");

    private static int FindCustomAmountSlot(DataContainer data)
    {
        // slot 16 is the typical location for the Custom Amount button
        if (data.Items.Count > 16 && IsCustomAmount(data.Items[16]))
            return 16;
        for (int i = 0; i < data.Items.Count; i++)
            if (IsCustomAmount(data.Items[i]))
                return i;
        return -1;
    }

    private static bool IsCustomAmount(Item item)
    {
        var name = item?.ItemName;
        if (name == null)
            return false;
        var lower = name.ToLowerInvariant();
        return lower.Contains("custom") && lower.Contains("amount");
    }

    private static string FindBazaarItemTag(DataContainer data)
    {
        // the buy option items carry the actual bazaar product tag; take the first we have a price for
        foreach (var (auction, _) in data.auctionRepresent)
        {
            var tag = auction?.Tag;
            if (!string.IsNullOrEmpty(tag) && data.bazaarPrices.ContainsKey(tag))
                return tag;
        }
        return null;
    }

    private static long? ParseMaxAmount(Item customItem)
        => ParseMaxAmountFromText((customItem?.Description ?? string.Empty) + "\n" + (customItem?.ItemName ?? string.Empty));

    // the Custom Amount sign shows the currently selected amount, e.g. "Your amount: §a333x"
    internal static long? ParseMaxAmountFromText(string text)
    {
        var cleaned = Regex.Replace(text ?? string.Empty, "§.", string.Empty);
        // prefer the explicit "amount: N" line
        var amountMatch = Regex.Match(cleaned, @"amount:\s*([\d,]+)", RegexOptions.IgnoreCase);
        if (amountMatch.Success && long.TryParse(amountMatch.Groups[1].Value.Replace(",", ""), out var byLabel))
            return byLabel;
        // otherwise take the largest "Nx" occurrence
        long? max = null;
        foreach (Match m in Regex.Matches(cleaned, @"([\d,]+)\s*x"))
            if (long.TryParse(m.Groups[1].Value.Replace(",", ""), out var value) && (max == null || value > max))
                max = value;
        return max;
    }
}

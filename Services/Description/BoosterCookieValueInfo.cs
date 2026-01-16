using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Api.Models.Mod;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Displays the coin value of a Booster Cookie based on bits earned and current bit-to-coin conversion rates.
/// Shows how many coins the cookie will yield and compares it to the price of BOOSTER_COOKIE.
/// </summary>
public class BoosterCookieValueInfo : ICustomModifier
{
    private const string BitsKey = "cookiebits";

    /// <summary>
    /// Applies the booster cookie value modification to the item description.
    /// Parses the bits value and calculates coin yield, then adds it to the description.
    /// </summary>
    public void Apply(DataContainer data)
    {
        // Find the BOOSTER_COOKIE item (always at index 11 based on user's sample)
        var boosterCookieIndex = 11;

        if (boosterCookieIndex >= data.auctionRepresent.Count)
            return;

        var item = data.Items?[boosterCookieIndex];
        if (item?.Tag != "BOOSTER_COOKIE")
            return;

        var desc = data.auctionRepresent[boosterCookieIndex].desc;
        if (desc == null || !data.Loaded.TryGetValue(BitsKey, out var bitsTask))
            return;

        try
        {
            // Parse bits from the description
            if (!TryParseBitsFromLore(desc, out int bits) || bits <= 0)
                return;

            var bestOption = JsonConvert.DeserializeObject<BitService.Option>(bitsTask.Result.ToString());
            if (bestOption == null || bestOption.CoinsPerBit <= 0)
                return;

            var totalCoins = (long)(bits * bestOption.CoinsPerBit);
            var cookiePrice = data.GetItemprice("BOOSTER_COOKIE");

            // Create the display information
            var coinsDisplay = $"{McColorCodes.GOLD}{ModDescriptionService.FormatPriceShort(totalCoins)}";
            var profit = totalCoins - cookiePrice;
            var profitDisplay = profit >= 0
                ? $"{McColorCodes.GREEN}+{ModDescriptionService.FormatPriceShort(profit)}"
                : $"{McColorCodes.RED}{ModDescriptionService.FormatPriceShort(profit)}";

            DescModification[] line = [new LoreBuilder().AddText($"{McColorCodes.GRAY}Bits from cookie value: {coinsDisplay} ",
             $"{McColorCodes.GRAY}Based on buying {McColorCodes.AQUA}{bestOption.Name}\n{McColorCodes.GRAY}and selling it at market price").BuildLine(),
            new($"{McColorCodes.GRAY}(Profit: {profitDisplay}{McColorCodes.GRAY})")];

            // Add this info as a new line
            data.mods[boosterCookieIndex].Add(line.Last());
            data.mods.Add(new(line));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing booster cookie: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads the best bit-to-coin conversion rate asynchronously.
    /// </summary>
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        preRequest.ToLoad[BitsKey] = Task.Run(async () =>
        {
            try
            {

                var bitsService = DiHandler.GetService<BitService>();
                var conversion = await bitsService.GetOptions();
                var best = conversion.OrderByDescending(c => c.CoinsPerBit).FirstOrDefault();
                return JsonConvert.SerializeObject(best);
            }
            catch (System.Exception e)
            {
                Console.WriteLine($"Error loading bit options: {e.Message}");
                return "{}";
            }
        });
    }

    /// <summary>
    /// Parses the bits value from the booster cookie lore.
    /// Handles formats where the line break may occur between the amount and "Bits":
    /// - "§7You will be able to gain §b5,333\n§bBits §7from this cookie."
    /// - "§7You will be able to gain §b5,333 §bBits §7from this cookie."
    /// </summary>
    private static bool TryParseBitsFromLore(string[] description, out int bits)
    {
        bits = 0;

        // Combine all description lines with newlines to handle multi-line patterns
        var combinedDesc = string.Join("\n", description);

        // Remove color codes from the combined description
        var cleaned = Regex.Replace(combinedDesc, "§.", "");

        // Match pattern: "will be able to gain" -> amount -> "Bits"
        // The amount can have commas (e.g., "5,333" or just digits)
        // Accounts for possible newlines and whitespace between components
        var match = Regex.Match(cleaned, @"will be able to gain\s*([\d,]+)\s*Bits", RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var numberStr = match.Groups[1].Value.Replace(",", "");
            if (int.TryParse(numberStr, out int parsedBits))
            {
                bits = parsedBits;
                return true;
            }
        }

        return false;
    }
}

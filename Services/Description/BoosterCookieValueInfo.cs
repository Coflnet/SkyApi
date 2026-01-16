using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.MC;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Displays the coin value of a Booster Cookie based on bits earned and current bit-to-coin conversion rates.
/// Shows how many coins the cookie will yield and compares it to the price of BOOSTER_COOKIE.
/// </summary>
public class BoosterCookieValueInfo : ICustomModifier
{
    private const string BitsKey = "cookiebits";

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
            var coinsDisplay = $"{McColorCodes.GOLD}{FormatCoins(totalCoins)}";
            var profit = totalCoins - cookiePrice;
            var profitDisplay = profit >= 0 
                ? $"{McColorCodes.GREEN}+{FormatCoins(profit)}" 
                : $"{McColorCodes.RED}{FormatCoins(profit)}";
            
            var line = $"{McColorCodes.GRAY}Cookie value: {coinsDisplay} {McColorCodes.GRAY}(Profit: {profitDisplay}{McColorCodes.GRAY})";

            // Add this info as a new line
            data.mods[boosterCookieIndex].Add(new(line));
            data.mods.Add(new(){new(line)});
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing booster cookie: {ex.Message}");
        }
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        preRequest.ToLoad[BitsKey] = Task.Run(async () =>
        {
            var bitsService = DiHandler.GetService<BitService>();
            var conversion = await bitsService.GetOptions();
            var best = conversion.OrderByDescending(c => c.CoinsPerBit).FirstOrDefault();
            return JsonConvert.SerializeObject(best);
        });
    }

    /// <summary>
    /// Parses the bits value from the booster cookie lore.
    /// Expected format: "§7You will be able to gain §b{bits}\n§bBits §7from this cookie."
    /// </summary>
    private static bool TryParseBitsFromLore(string[] description, out int bits)
    {
        bits = 0;
        
        foreach (var line in description)
        {
            // Look for line containing "will be able to gain" and "Bits"
            if (line.Contains("will be able to gain") && line.Contains("Bits"))
            {
                // Extract the number from the line
                // Remove color codes and extract the number
                var cleaned = Regex.Replace(line, "§.", "").Trim();
                
                // Try to find a number pattern like "5,333" or "5333"
                var match = Regex.Match(cleaned, @"(\d{1,3}(?:,\d{3})*|\d+)");
                if (match.Success)
                {
                    var numberStr = match.Groups[1].Value.Replace(",", "");
                    if (int.TryParse(numberStr, out int parsedBits))
                    {
                        bits = parsedBits;
                        return true;
                    }
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// Formats a coin amount for display (e.g., 1,500,000 -> "1.5M")
    /// </summary>
    private static string FormatCoins(long coins)
    {
        if (coins >= 1_000_000_000)
            return $"{coins / 1_000_000_000.0:F1}B";
        if (coins >= 1_000_000)
            return $"{coins / 1_000_000.0:F1}M";
        if (coins >= 1_000)
            return $"{coins / 1_000.0:F1}K";
        return coins.ToString(CultureInfo.InvariantCulture);
    }
}

using System.Globalization;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Compares value of receiving and sending in trade menu and suggests lowball prices
/// </summary>
public class TradeInfoDisplay : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var index = 0;
        long sendSum = 0L;
        long receiveSum = 0L;
        long receiveCount = 0L;
        var likelyLowballing = true;
        var lowballPrice = 0L;
        foreach (var sniperPrice in data.PriceEst)
        {
            var i = index++;
            if (i >= 36)
                break;
            var column = i % 9;
            long value = 0;
            var item = data.Items[i];
            if (item.ItemName?.EndsWith(" coins") ?? false)
            {
                var name = item.ItemName;
                value = ParseCoinAmount(name.Substring(2, name.Length - 8));
            }
            else if (sniperPrice != null && sniperPrice.Median != 0)
            {
                value = sniperPrice.Median;
            }
            else if (item?.Tag != null && (data.bazaarPrices?.TryGetValue(item.Tag, out var price) ?? false))
            {
                value = (long)price.SellPrice * item.Count;
            }
            if (column < 4)
            {
                sendSum += value;
                if (sniperPrice?.Median > 0)
                    likelyLowballing = false;
            }
            else if (column > 4)
            {
                receiveSum += value;
                if (value > 0)
                    receiveCount++;
                long medianVal = GetAdjustedValue(data.inventory.Settings.LowballMedUndercut, sniperPrice?.Median ?? 0, sniperPrice?.Volume ?? 0);
                long lbinVal = GetAdjustedValue(data.inventory.Settings.LowballLbinUndercut, sniperPrice?.Lbin?.Price ?? 0, sniperPrice?.Volume ?? 0);
                if (lbinVal < medianVal && lbinVal != 0)
                    lowballPrice += lbinVal;
                else
                    lowballPrice += medianVal;
            }
        }
        Console.WriteLine($"trade warning send: {sendSum} receive: {receiveSum}");
        Console.WriteLine(JsonConvert.SerializeObject(data.Items));
        data.mods[39].Add(new($"Send value: {data.modService.FormatNumber(sendSum)}"));
        data.mods[39].Add(new($"Receive value: {data.modService.FormatNumber(receiveSum)}"));
        data.mods[39].Add(new($"CoflMod estimate, please report issues"));
        if (receiveSum < sendSum / 2)
        {
            data.mods[39].Insert(0, new(DescModification.ModType.REPLACE, 0, $"{McColorCodes.RED}You are sending way more coins"));
            data.mods[39].Insert(0, new(DescModification.ModType.INSERT, 1, $"{McColorCodes.RED}than you are receiving! {McColorCodes.OBFUSCATED}A"));
        }
        if (receiveSum == 0 || !likelyLowballing)
            return;
        var extraInfo = new List<DescModification>()
        {
            new ("Looks like you are lowballing")
        };
        data.mods.Add(extraInfo);
        if (data.accountInfo.ExpiresAt < DateTime.UtcNow || data.accountInfo.Tier < AccountTier.PREMIUM)
        {
            extraInfo.Add(new($"{McColorCodes.GRAY}With premium we will suggest"));
            extraInfo.Add(new($"{McColorCodes.GRAY}a lowball price automatically"));
            extraInfo.Add(new($"{McColorCodes.GRAY}looks like you don't currently"));
            extraInfo.Add(new($"{McColorCodes.GRAY}have SkyCofl premium :("));
            return;
        }
        extraInfo.Add(new($"{McColorCodes.GREEN}For lowballing these {receiveCount} items we"));
        extraInfo.Add(new(DescModification.ModType.SUGGEST, 0, $"----------------: " + ModDescriptionService.FormatPriceShort(lowballPrice)));
        extraInfo.Add(new($"{McColorCodes.GRAY}SkyCofl recommended"));
        if (data.inventory.Settings.LowballMedUndercut == 0)
        {
            extraInfo.Add(new($"{McColorCodes.GRAY}Adjust median and lbin undercut"));
            extraInfo.Add(new($"{McColorCodes.GRAY}percentage with these settings:"));
            extraInfo.Add(new($"{McColorCodes.GRAY}/cofl set medUndercut 10"));
            extraInfo.Add(new($"{McColorCodes.GRAY}/cofl set lbinUndercut 10"));
        }

    }

    private static long GetAdjustedValue(short underCutPercentage, long medLowballValue, float volume)
    {
        if (medLowballValue < 10_000_000)
        {
            underCutPercentage += 2;
        }
        else if (medLowballValue > 100_000_000)
        {
            underCutPercentage -= 2;
        }
        if (medLowballValue > 1_000_000_000)
        {
            underCutPercentage -= 3;
        }
        if (volume <= 1)
        {
            underCutPercentage += 3;
        }
        var total = (long)(medLowballValue * (100 - underCutPercentage) / 100.0);
        return total;
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        return;
    }

    private static long ParseCoinAmount(string stringAmount)
    {
        var parsed = 0d;
        if (stringAmount.EndsWith("B"))
            parsed = double.Parse(stringAmount.Trim('B'), CultureInfo.InvariantCulture) * 1_000_000_000;
        else if (stringAmount.EndsWith("M"))
            parsed = double.Parse(stringAmount.Trim('M'), CultureInfo.InvariantCulture) * 1_000_000;
        else if (stringAmount.EndsWith("k"))
            parsed = double.Parse(stringAmount.Trim('k'), CultureInfo.InvariantCulture) * 1_000;
        else
            parsed = double.Parse(stringAmount, CultureInfo.InvariantCulture);

        return (long)(parsed);
    }
}

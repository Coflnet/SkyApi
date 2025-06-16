using System.Globalization;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;
public class TradeWarning : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        var index = 0;
        long sendSum = 0L;
        long sendCraftSum = 0L;
        long receiveSum = 0L;
        long receiveCraftSum = 0L;
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
                sendCraftSum += value;
            }
            else if (column > 4)
            {
                receiveSum += value;
                receiveCraftSum += value;
            }
        }
        Console.WriteLine($"trade warning send: {sendSum} receive: {receiveSum}");
        Console.WriteLine(JsonConvert.SerializeObject(data.Items[48]));
        data.mods[39].Add(new($"Send value: {data.modService.FormatNumber(sendSum)}"));
        data.mods[39].Add(new($"{McColorCodes.GRAY}Craft value: {data.modService.FormatNumber(sendCraftSum)}"));
        data.mods[39].Add(new($"Receive value: {data.modService.FormatNumber(receiveSum)}"));
        data.mods[39].Add(new($"{McColorCodes.GRAY}Craft value: {data.modService.FormatNumber(receiveCraftSum)}"));
        data.mods[39].Add(new($"CoflMod estimate, please report issues"));
        if (receiveSum < sendSum / 2)
        {
            data.mods[39].Insert(0, new(DescModification.ModType.REPLACE, 0, $"{McColorCodes.RED}You are sending way more coins"));
            data.mods[39].Insert(0, new(DescModification.ModType.INSERT, 1, $"{McColorCodes.RED}than you are receiving! {McColorCodes.OBFUSCATED}A"));
        }
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

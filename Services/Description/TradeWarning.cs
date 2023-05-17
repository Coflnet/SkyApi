using System.Globalization;
using Coflnet.Sky.Api.Models.Mod;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;
public class TradeWarning : CustomModifier
{
    public void Apply(DataContainer data)
    {
        var index = 0;
        long sendSum = 0L;
        long receiveSum = 0L;
        foreach (var item in data.res)
        {
            var i = index++;
            if (i >= 36)
                break;
            var column = i % 9;
            long value = 0;
            if (data.Items[i].ItemName?.EndsWith(" coins") ?? false)
            {
                var name = data.Items[i].ItemName;
                Console.WriteLine("value string: " + name);
                value = ParseCoinAmount(name.Substring(2, name.Length - 8));
                Console.WriteLine("value parsed: " + value);
            }
            else if (item != null)
            {
                value = item.Median;
            }
            if (column < 4)
                sendSum += value;
            else if (column > 4)
                receiveSum += value;
        }
        Console.WriteLine($"trade warning send: {sendSum} receive: {receiveSum}");
        Console.WriteLine(JsonConvert.SerializeObject(data.Items[48]));
        data.mods[39].Insert(0, new DescModification($"Send value: {data.modService.FormatNumber(sendSum)}"));
        data.mods[39].Insert(0, new DescModification($"Receive value: {data.modService.FormatNumber(receiveSum)}"));
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

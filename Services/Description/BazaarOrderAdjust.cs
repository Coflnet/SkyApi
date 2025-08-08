using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.MC;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class BazaarOrderAdjust : ICustomModifier
{
    private BazaarApi bazaarApi;

    public BazaarOrderAdjust(BazaarApi bazaarApi)
    {
        this.bazaarApi = bazaarApi;
    }

    public void Apply(DataContainer data)
    {
        var loaded = data.Loaded[nameof(BazaarOrderAdjust)].Result;
        var result = JsonConvert.DeserializeObject<StorageQuickStatus[]>(loaded).ToDictionary(x => x.ProductId);
        var slotCount = data.auctionRepresent.TakeWhile(x => (x.auction?.Tag ?? "false") != "GO_BACK").Count();
        var offerLookup = data.auctionRepresent.Take(slotCount).Where(x => x.auction != null).ToLookup(x => (x.auction.ItemName.Contains("BUY"), x.auction.Tag));
        var buySum = 0L;
        var sellSum = 0L;
        for (int i = 0; i < slotCount; i++)
        {
            var auction = data.auctionRepresent[i].auction;
            var description = data.auctionRepresent[i].desc;
            if (auction == null)
                continue;
            var bazaar = result.GetValueOrDefault(auction.Tag);
            if (bazaar != null && (bazaar.SellPrice != 0 || bazaar.BuyPrice != 0))
            {
                var isBuy = auction.ItemName.Contains("BUY");
                var allPrices = offerLookup[(isBuy, auction.Tag)].Select(x => x.desc).Where(x => x != null)
                    .Select(x => x.Where(p => p.StartsWith("§7Price per unit: §6")).First().Split("§7Price per unit: §6").Last().Split(" coins").First())
                    .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

                var price = isBuy ? allPrices.Max() : allPrices.Min();
                if (data.inventory.Version >= 2)
                {
                    price = description.Where(l => l.StartsWith("§7Price per unit: §6"))
                        .FirstOrDefault()?.Split("§7Price per unit: §6").Last().Split(" coins").First() is string priceStr
                        && double.TryParse(priceStr, System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice) ? parsedPrice : price;
                }
                var isTop = isBuy ? bazaar.SellPrice <= price : bazaar.BuyPrice >= price;
                var isOnlyOne = offerLookup[(isBuy, auction.Tag)].Count() == 1 || data.inventory.Version >= 2;
                if (isTop)
                {
                    var color = isOnlyOne ? "50af50" : "508f50";
                    data.mods[i].Add(new(Models.Mod.DescModification.ModType.HIGHLIGHT, 0, color));
                    data.mods[i].Add(new(Models.Mod.DescModification.ModType.INSERT, 2, $"{McColorCodes.DARK_GREEN}You have best offer ({ModDescriptionService.FormatPriceShort(price)})"));
                }
                else
                {
                    var color = isOnlyOne ? "ff0000" : "af5050";
                    data.mods[i].Add(new(Models.Mod.DescModification.ModType.HIGHLIGHT, 0, color));
                    var list = isBuy ? bazaar.SellOrders.Select(s => (s.Amount, s.PricePerUnit)).TakeWhile(s => s.PricePerUnit > price) :
                        bazaar.BuyOrders.Select(s => (s.Amount, s.PricePerUnit)).TakeWhile(s => s.PricePerUnit < price);
                    var ahead = list.Sum(o => o.Amount);
                    data.mods[i].Add(new(Models.Mod.DescModification.ModType.INSERT, 2, $"{McColorCodes.RED}{ModDescriptionService.FormatPriceShort(ahead)}{McColorCodes.GRAY} available for better price "));
                }
                if (description.Any(l => l.EndsWith("100%!")))
                {
                    data.mods[i].Where(x => x.Type == Models.Mod.DescModification.ModType.HIGHLIGHT).First().Value = "00ff00";
                }
                if (!isOnlyOne)
                {
                    data.mods[i].Add(new($"{McColorCodes.RED}Multiple offers, best used ({ModDescriptionService.FormatPriceShort(price)})"));
                }
                var amount = Regex.Match(description.Where(l => l.Contains("amount: §")).FirstOrDefault("amount: §a1"), @"amount: §a([\d,]+)").Groups[1].Value;
                var amountParsed = int.Parse(amount, System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture);
                if (isBuy)
                {
                    buySum += (long)(price * amountParsed);
                }
                else
                {
                    sellSum += (long)(price * amountParsed);
                }
            }
        }
        var extra = new List<Models.Mod.DescModification>();
        data.mods.Add(extra);
        if (buySum > 0)
            extra.Add(new(Models.Mod.DescModification.ModType.APPEND, 0, $"{McColorCodes.GRAY}Total buy: §6{ModDescriptionService.FormatPriceShort(buySum)}"));
        if (sellSum > 0)
            extra.Add(new(Models.Mod.DescModification.ModType.APPEND, 0, $"{McColorCodes.GRAY}Total sell: §6{ModDescriptionService.FormatPriceShort(sellSum)}"));

        if (Random.Shared.NextDouble() < 0.1)
        {
            extra.Add(new($"{McColorCodes.GREEN}Also checkout"));
            extra.Add(new($"{McColorCodes.GOLD}/cofl bazaar"));
        }
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        var task = Task.Run(async () =>
        {
            var slotCount = preRequest.auctionRepresent.TakeWhile(x => (x.auction?.Tag ?? "false") != "GO_BACK").Count();
            var ids = preRequest.auctionRepresent.Take(slotCount).Where(x => x.auction != null)
                    .Select(x => x.auction.Tag).Distinct().ToArray();
            var requests = ids.Select(x => bazaarApi.GetClosestToAsync(x));
            var result = await Task.WhenAll(requests);
            return JsonConvert.SerializeObject(result);
        });
        preRequest.ToLoad.Add(nameof(BazaarOrderAdjust), task);
    }
}
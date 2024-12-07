using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.MC;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class BazaarOrderAdjust : CustomModifier
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
        var offerLookup = data.auctionRepresent.Take(36).Where(x => x.auction != null).ToLookup(x => x.auction.Tag);
        for (int i = 0; i < 36; i++)
        {
            var auction = data.auctionRepresent[i].auction;
            var description = data.auctionRepresent[i].desc;
            if (auction == null)
                continue;
            var bazaar = result.GetValueOrDefault(auction.Tag);
            if (bazaar != null && bazaar.SellPrice != 0)
            {
                var allPrices = offerLookup[auction.Tag].Select(x => x.desc).Where(x => x != null)
                    .Select(x => x.Where(p => p.StartsWith("§7Price per unit: §6")).First().Split("§7Price per unit: §6").Last().Split(" coins").First())
                    .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

                var isBuy = auction.ItemName.Contains("BUY");
                var price = isBuy ? allPrices.Max() : allPrices.Min();
                var isTop = isBuy ? bazaar.SellPrice <= price : bazaar.BuyPrice >= price;
                var isOnlyOne = offerLookup[auction.Tag].Count() == 1;
                if (isTop)
                {
                    var color = isOnlyOne ? "00ff00" : "50af50";
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
                if (!isOnlyOne)
                {
                    data.mods[i].Add(new($"{McColorCodes.RED}Multiple offers, best used ({ModDescriptionService.FormatPriceShort(price)})"));
                }

            }
        }
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        var task = Task.Run(async () =>
        {
            var ids = preRequest.auctionRepresent.Take(36).Where(x => x.auction != null)
                    .Select(x => x.auction.Tag).Distinct().ToArray();
            var requests = ids.Select(x => bazaarApi.ApiBazaarItemIdSnapshotGetAsync(x));
            var result = await Task.WhenAll(requests);
            return JsonConvert.SerializeObject(result);
        });
        preRequest.ToLoad.Add(nameof(BazaarOrderAdjust), task);
    }
}
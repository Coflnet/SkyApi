using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Sniper.Client.Model;

namespace SkyApi.Services.Description;

public class GemsCookiesValue : CurrencyValueDisplay
{
    protected override string Value => "SkyBlock Gems";
    protected override string currencyName => "Gem";

    protected override void ProcessLine(DataContainer data, int i, string[] desc, PriceEstimate price)
    {
        data.bazaarPrices["BOOSTER_COOKIE"] = new Coflnet.Sky.Bazaar.Client.Model.ItemPrice() { SellPrice = 2_000_000 };
        var auction = data.auctionRepresent[i].auction;
        var bazaar = data.bazaarPrices.GetValueOrDefault(auction.Tag);
        if (bazaar != default && bazaar.SellPrice != 0 && HasValue(desc, out int bits, out int lineId))
        {
            var formattedPrice = $"{McColorCodes.AQUA}{data.modService.FormatNumber((float)bazaar.SellPrice / bits * auction.Count)}";
            ReplaceLine(data, i, lineId, formattedPrice);
        }
    }
}

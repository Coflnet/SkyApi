using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Sniper.Client.Model;

namespace SkyApi.Services.Description;

/// <summary>
/// A <see cref="CurrencyValueDisplay"/> for SkyBlock Gems
/// </summary>
public class SkyblockGemsValue : CurrencyValueDisplay
{
    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override string ValueSuffix => "SkyBlock Gems";
    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override string currencyName => "Gem";

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    protected override void ProcessLine(DataContainer data, int i, string[] desc, PriceEstimate price)
    {
        var auction = data.auctionRepresent[i].auction;
        var bazaar = data.bazaarPrices.GetValueOrDefault(auction.Tag);
        var selectedPrice = bazaar != default ? BazaarPriceSelector.Select(bazaar, data.UseBuyOrderPrices) : 0;
        if (selectedPrice != 0 && HasValue(desc, out int bits, out int lineId))
        {
            var formattedPrice = $"{McColorCodes.AQUA}{data.modService.FormatNumber((float)selectedPrice / bits * auction.Count)}";
            ReplaceLine(data, i, lineId, formattedPrice);
        }
    }
}

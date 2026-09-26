using Coflnet.Sky.Bazaar.Client.Model;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Central place implementing the rule for picking which side of a bazaar price values/costs an
/// item: <see cref="ItemPrice.BuyPrice"/> (insta buy, the lowest sell offer) by default, or
/// <see cref="ItemPrice.SellPrice"/> (insta sell, roughly what a buy order pays) when the user has
/// enabled buy-order pricing (the mod's "BuyOrderPrices" description setting).
/// Falls back to the other side when the chosen side has no orders (reports 0); everything that
/// values or costs a bazaar item should go through this instead of duplicating the ternary.
/// </summary>
public static class BazaarPriceSelector
{
    /// <summary>
    /// Selects the bazaar price to use for valuing/costing an item.
    /// </summary>
    /// <param name="price">The bazaar item price snapshot. Null returns 0.</param>
    /// <param name="useBuyOrderPrices">The user's buy-order-prices setting.</param>
    /// <returns>
    /// <see cref="ItemPrice.SellPrice"/> when <paramref name="useBuyOrderPrices"/> is true, otherwise
    /// <see cref="ItemPrice.BuyPrice"/>; falls back to the other side if the selected one is &lt;= 0
    /// (no orders on that side). 0 if both sides are 0 or <paramref name="price"/> is null.
    /// </returns>
    public static double Select(ItemPrice price, bool useBuyOrderPrices)
    {
        if (price == null)
            return 0;
        var selected = useBuyOrderPrices ? price.SellPrice : price.BuyPrice;
        if (selected > 0)
            return selected;
        return useBuyOrderPrices ? price.BuyPrice : price.SellPrice;
    }
}

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.FlipTracker.Client.Model;

namespace Coflnet.Sky.Api.Services.Description;
/// <summary>Represents a data container.</summary>
public class DataContainer
{
    /// <summary>Stores the inventory.</summary>
    public InventoryDataWithSettings inventory;
    /// <summary>Stores the auction represent.</summary>
    public List<(Core.SaveAuction auction, string[] desc)> auctionRepresent;
    /// <summary>Stores the price est.</summary>
    public List<Sniper.Client.Model.PriceEstimate> PriceEst;
    /// <summary>Stores the bazaar prices.</summary>
    public ImmutableDictionary<string, ItemPrice> bazaarPrices;
    /// <summary>Stores the npc sell prices.</summary>
    public Dictionary<string, float> NpcSellPrices;
    /// <summary>Stores the mods.</summary>
    public List<List<DescModification>> mods;
    /// <summary>Stores the prices paid.</summary>
    public Dictionary<string, (long, DateTime, string)> pricesPaid;
    internal ModDescriptionService modService;
    /// <summary>Stores the item listings.</summary>
    public ILookup<string, ListingSum> itemListings;
    internal Dictionary<(string, Core.Tier), KatUpgradeCost> katUpgradeCost;
    internal Dictionary<string, long> itemPrices = new();
    internal Dictionary<string, ProfitableCraft> allCrafts;
    /// <summary>Stores the item tag to name.</summary>
    public Dictionary<string,string> itemTagToName;
    internal AccountInfo accountInfo;
    internal ILookup<long, Flip> flips;
    /// <summary>
    /// Minecraft name of the player the description is computed for, used to look up player state.
    /// </summary>
    internal string mcName;

    /// <summary>Gets or sets the items.</summary>
    public List<Item> Items { get; internal set; }
    /// <summary>Gets or sets the loaded.</summary>
    public Dictionary<string, Task<string>> Loaded { get; set; }

    /// <summary>
    /// Whether the user enabled buy-order pricing (<see cref="DescriptionSetting.BuyOrderPrices"/>).
    /// When true, bazaar values/costs should prefer <see cref="ItemPrice.SellPrice"/> over
    /// <see cref="ItemPrice.BuyPrice"/> (see <see cref="BazaarPriceSelector"/>).
    /// </summary>
    public bool UseBuyOrderPrices => inventory?.Settings?.BuyOrderPrices ?? false;

    /// <summary>
    /// Gets the item price, applying the bazaar price selection rule (<see cref="BazaarPriceSelector"/>):
    /// <see cref="ItemPrice.BuyPrice"/> by default, <see cref="ItemPrice.SellPrice"/> when
    /// <see cref="UseBuyOrderPrices"/> is set, falling back to the other side if the selected one has
    /// no orders. The <see cref="itemPrices"/> dictionary (exact known prices) still takes precedence.
    /// </summary>
    public long GetItemprice(string itemKey)
    {
        if (itemKey == null)
            return 0;
        if (itemPrices.TryGetValue(itemKey, out var price))
            return price;
        if (bazaarPrices.TryGetValue(itemKey, out var bazaarPrice))
            return (long)BazaarPriceSelector.Select(bazaarPrice, UseBuyOrderPrices);
        return 0;
    }

    /// <summary>
    /// Gets the raw, explicit price of an item without applying the bazaar price selection rule or
    /// its fallback. Used where a specific side of the spread is literally what is being paid, e.g.
    /// <see cref="InstantBuyMaxAmount"/>'s instant-buy cost (always <see cref="ItemPrice.BuyPrice"/>).
    /// Everything that should honor the user's buy-order-prices setting should use
    /// <see cref="GetItemprice(string)"/> instead.
    /// </summary>
    /// <param name="itemKey">The item key/tag</param>
    /// <param name="instaBuy">If true, uses <see cref="ItemPrice.BuyPrice"/> (what insta buying costs); if false, uses <see cref="ItemPrice.SellPrice"/> (insta sell, roughly what a buy order pays)</param>
    /// <returns>The item price in coins</returns>
    public long GetItemprice(string itemKey, bool instaBuy)
    {
        if (itemKey == null)
            return 0;
        if (itemPrices.TryGetValue(itemKey, out var price))
            return price;
        if (bazaarPrices.TryGetValue(itemKey, out var bazaarPrice))
            return instaBuy ? (long)bazaarPrice.BuyPrice : (long)bazaarPrice.SellPrice;
        return 0;
    }
}

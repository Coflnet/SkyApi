using System.Text.RegularExpressions;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Source-generated (compile-time) regexes matching chest names, used to pick which
/// <see cref="ICustomModifier"/> applies to an opened inventory. Keeping them as
/// <see cref="GeneratedRegexAttribute"/> partial properties avoids recompiling the same
/// patterns on every <c>ComputeDescriptions</c> call.
/// </summary>
internal static partial class ChestNamePatterns
{
    /// <summary>Trade window ("^You    ").</summary>
    [GeneratedRegex("^You    ")]
    internal static partial Regex TradeWindow { get; }

    /// <summary>Create BIN auction screen.</summary>
    [GeneratedRegex("^Create BIN")]
    internal static partial Regex CreateBin { get; }

    /// <summary>Create (non-BIN) auction screen.</summary>
    [GeneratedRegex("^Create Auction")]
    internal static partial Regex CreateAuction { get; }

    /// <summary>Manage auctions overview.</summary>
    [GeneratedRegex("^Manage Auctions")]
    internal static partial Regex ManageAuctions { get; }

    /// <summary>Auctions browser or auction house list.</summary>
    [GeneratedRegex("^(Auctions Browser|Auctions:)")]
    internal static partial Regex AuctionBrowser { get; }

    /// <summary>Auctions browser, BIN auction view, or a player's auctions list (for started/ends-in times).</summary>
    [GeneratedRegex("^(Auctions Browser|BIN Auction View|Auctions:)|s Auctions$")]
    internal static partial Regex AuctionListingTimes { get; }

    /// <summary>Community shop or bits shop.</summary>
    [GeneratedRegex("^(Community Shop|Bits Shop)")]
    internal static partial Regex CommunityOrBitsShop { get; }

    /// <summary>Bingo shop.</summary>
    [GeneratedRegex("^Bingo Shop")]
    internal static partial Regex BingoShop { get; }

    /// <summary>Community shop (skyblock gems).</summary>
    [GeneratedRegex("^Community Shop")]
    internal static partial Regex CommunityShop { get; }

    /// <summary>Previous fire sales.</summary>
    [GeneratedRegex("^Previous Fire Sales")]
    internal static partial Regex PreviousFireSales { get; }

    /// <summary>Seasonal bundles, SkyMart barn skins, or Taylor's collection (Gems currency).</summary>
    [GeneratedRegex("^(Seasonal Bundles|SkyMart Barn Skins|Taylor's Collection)")]
    internal static partial Regex GemBundles { get; }

    /// <summary>SkyMart (Copper currency).</summary>
    [GeneratedRegex("^SkyMart")]
    internal static partial Regex SkyMart { get; }

    /// <summary>A player's auctions list.</summary>
    [GeneratedRegex("s Auctions$")]
    internal static partial Regex PlayerAuctions { get; }

    /// <summary>Auctions browser, auction house list, or the trade window (double space).</summary>
    [GeneratedRegex("^(Auctions Browser|Auctions:|You  )")]
    internal static partial Regex AuctionHighlight { get; }

    /// <summary>Dark auction pet round.</summary>
    [GeneratedRegex(@"Pet - Round \d$")]
    internal static partial Regex PetRound { get; }

    /// <summary>Bazaar orders overview.</summary>
    [GeneratedRegex("Bazaar Orders$")]
    internal static partial Regex BazaarOrders { get; }

    /// <summary>Any bazaar screen.</summary>
    [GeneratedRegex("^Bazaar ")]
    internal static partial Regex BazaarPrefix { get; }

    /// <summary>Any chest name containing the "➜" price arrow.</summary>
    [GeneratedRegex(".*➜.*")]
    internal static partial Regex ArrowAnywhere { get; }

    /// <summary>Bazaar instant buy screen.</summary>
    [GeneratedRegex("➜ Instant Buy")]
    internal static partial Regex InstantBuyArrow { get; }

    /// <summary>The Forge.</summary>
    [GeneratedRegex("^The Forge")]
    internal static partial Regex TheForge { get; }

    /// <summary>Fish family collection page.</summary>
    [GeneratedRegex(@"^\(\d\/2\) Fish Family")]
    internal static partial Regex FishFamily { get; }

    /// <summary>Crafting table.</summary>
    [GeneratedRegex("^Crafting")]
    internal static partial Regex Crafting { get; }

    /// <summary>Dungeon reward chest (wooden/gold/diamond/emerald/obsidian/bedrock).</summary>
    [GeneratedRegex("^(Wooden|Gold|Diamond|Emerald|Obsidian|Bedrock) Chest")]
    internal static partial Regex DungeonChest { get; }

    /// <summary>Kuudra paid chest.</summary>
    [GeneratedRegex("Paid Chest")]
    internal static partial Regex PaidChest { get; }

    /// <summary>Bazaar flip suggestion order options.</summary>
    [GeneratedRegex("^Order options")]
    internal static partial Regex OrderOptions { get; }

    /// <summary>Consume booster cookie confirmation.</summary>
    [GeneratedRegex("^Consume Booster Cookie")]
    internal static partial Regex BoosterCookie { get; }
}

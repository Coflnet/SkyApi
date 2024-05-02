using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Kafka;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Api;
using fNbt;
using fNbt.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SkyApi.Services.Description;

namespace Coflnet.Sky.Api.Services;

// wrapper for the deserialized cache
public class DeserializedCache
{
    public Dictionary<string, Crafts.Client.Model.ProfitableCraft> Crafts = new();
    public Dictionary<(string, Tier), Crafts.Client.Model.KatUpgradeCost> Kat = new();
    public Dictionary<string, ItemPrice> BazaarItems = new();
    public Dictionary<string, long> ItemPrices = new();
    public DateTime LastUpdate = DateTime.MinValue;
    public bool IsUpdating = false;
}

public class ModDescriptionService : IDisposable
{
    private static readonly string BitsRegexPattern = @".*?(\d*\.?\d+|\d{1,3}(,\d{3})*(\.\d+)?) Bits.*";

    private readonly ICraftsApi craftsApi;
    private readonly ISniperClient sniperClient;
    private readonly AhListChecker ahListChecker;
    public readonly SettingsService settingsService;
    private readonly IdConverter idConverter;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly BazaarApi bazaarApi;
    private readonly PlayerName.PlayerNameService playerNameService;
    public ILogger<ModDescriptionService> logger;
    private readonly IConfiguration config;
    private readonly IStateUpdateService stateService;
    private readonly ItemSkinHandler itemSkinHandler;
    private readonly IKatApi katApi;
    private readonly ClassNameDictonary<CustomModifier> customModifiers = new();
    private readonly DeserializedCache deserializedCache = new();
    private readonly PropertyMapper mapper = new();
    private readonly Core.Services.HypixelItemService itemService;
    private readonly FlipTracker.Client.Api.ITrackerApi trackerApi;

    public DeserializedCache DeserializedCache => deserializedCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModDescriptionService"/> class.
    /// </summary>
    /// <param name="craftsApi"></param>
    /// <param name="settingsService"></param>
    /// <param name="idConverter"></param>
    /// <param name="scopeFactory"></param>
    /// <param name="bazaarApi"></param>
    /// <param name="playerNameService"></param>
    /// <param name="logger"></param>
    /// <param name="config"></param>
    /// <param name="stateService"></param>
    /// <param name="sniperClient"></param>
    /// <param name="itemSkinHandler"></param>
    /// <param name="ahListChecker"></param>
    /// <param name="katApi"></param>
    /// <param name="itemService"></param>
    /// <param name="trackerApi"></param>
    public ModDescriptionService(ICraftsApi craftsApi,
                                 SettingsService settingsService,
                                 IdConverter idConverter,
                                 IServiceScopeFactory scopeFactory,
                                 BazaarApi bazaarApi,
                                 PlayerName.PlayerNameService playerNameService,
                                 ILogger<ModDescriptionService> logger,
                                 IConfiguration config,
                                 IStateUpdateService stateService,
                                 ISniperClient sniperClient,
                                 ItemSkinHandler itemSkinHandler,
                                 AhListChecker ahListChecker,
                                 IKatApi katApi,
                                 Core.Services.HypixelItemService itemService,
                                 FlipTracker.Client.Api.ITrackerApi trackerApi)
    {
        this.craftsApi = craftsApi;
        this.settingsService = settingsService;
        this.idConverter = idConverter;
        this.scopeFactory = scopeFactory;
        this.bazaarApi = bazaarApi;
        this.playerNameService = playerNameService;
        this.logger = logger;
        this.config = config;
        this.stateService = stateService;
        this.sniperClient = sniperClient;
        RegisterModifiers();
        this.itemSkinHandler = itemSkinHandler;
        this.ahListChecker = ahListChecker;
        this.katApi = katApi;
        this.itemService = itemService;
        this.trackerApi = trackerApi;
    }

    private void RegisterModifiers()
    {
        customModifiers.Add("^You    ", new TradeWarning());
        customModifiers.Add("^Create BIN", new ListPriceRecommend());
        customModifiers.Add("^Manage Auctions", new AuctionValueSummary());
        customModifiers.Add("^(Auctions Browser|Auctions:)", new FlipOnNextPage());
        customModifiers.Add("^(Community Shop|Bits Shop)", new BitsCoinValue());
        customModifiers.Add("s Auctions$", new PlayerPageFlipHighlight());
        customModifiers.Add("^(Auctions Browser|Auctions:|You  )", new MatchWhiteBlacklist());
    }

    private readonly ConcurrentDictionary<string, (SelfUpdatingValue<DescriptionSetting>, SelfUpdatingValue<AccountInfo>)> settings = new();

    public List<Item> ProduceInventory(InventoryData modDescription, string playerId, string userId)
    {
        try
        {
            var items = InventoryToItems(modDescription);
            ahListChecker.CheckItems(items, playerId);
            var inventoryhash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(modDescription.FullInventoryNbt));
            // anonymous player only ineresting if ah contains seller
            if (playerId == null && !items.Any(i => i?.Description?.Contains("Seller") ?? false))
                return items;
            ProduceInventory(modDescription.ChestName, playerId, userId, items);
            return items;
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to parse inventory");
            foreach (var item in InventoryToItems(modDescription))
            {
                try
                {
                    MessagePack.MessagePackSerializer.Serialize(item);
                }
                catch (Exception)
                {
                    Console.WriteLine(JsonConvert.SerializeObject(item, Formatting.Indented));
                }
            }
            throw;
        }

    }

    /// <summary>
    /// Produce an inventory update for the given player
    /// </summary>
    /// <param name="chestName"></param>
    /// <param name="playerId"></param>
    /// <param name="userId"></param>
    /// <param name="items"></param>
    public void ProduceInventory(string chestName, string playerId, string userId, List<Item> items)
    {
        stateService.Produce(playerId, new()
        {
            Kind = UpdateMessage.UpdateKind.INVENTORY,
            Chest = new ChestView
            {
                Name = chestName,
                Items = items
            },
            UserId = userId,
            ReceivedAt = DateTime.UtcNow
        });
        Console.WriteLine("produced state update " + playerId + " " + chestName);
    }

    private List<Item> InventoryToItems(InventoryData modDescription)
    {
        return NBT.File(Convert.FromBase64String(modDescription.FullInventoryNbt)).RootTag.Get<NbtList>("i").Select(t =>
        {
            try
            {
                var compound = t as NbtCompound;
                if (compound.Count == 0)
                    return new Item();

                var item = new Item()
                { // order of parsing is important
                    Enchantments = NBT.GetEnchants(compound),
                    Tag = NBT.ItemID(compound),
                    ItemName = NBT.GetName(compound),
                    Description = string.Join('\n', NBT.GetLore(compound)),
                    Color = NBT.GetColor(compound),
                    Count = NBT.Count(compound),
                    ExtraAttributes = GetRemainingAttributes(compound)
                };
                if (!item.ExtraAttributes?.TryGetValue("tier", out _) ?? false)
                {
                    foreach (var line in NBT.GetLore(compound).Reverse())
                    {
                        if (NBT.TryFindTierInString(line, out Tier tier))
                            item.ExtraAttributes["tier"] = tier;
                    }
                }
                itemSkinHandler.StoreIfNeeded(item.Tag, compound);

                return item;
            }
            catch (Exception e)
            {
                logger.LogError(e, "parsing nbt to item");
                return new Item();
            }
        }).ToList();
    }

    private static Dictionary<string, object> GetRemainingAttributes(NbtCompound compound)
    {
        var extraAttributes = NbtData.AsDictonary(NBT.GetReducedExtra(compound));
        if (extraAttributes != null && extraAttributes.TryGetValue("petInfo", out var pet) && pet is string petString)
        {
            var info = JsonConvert.DeserializeObject<Dictionary<string, object>>(petString);
            if (info.TryGetValue("extraData", out var extra) && extra is Newtonsoft.Json.Linq.JObject jobj)
            {
                info["extraData"] = jobj.ToObject<Dictionary<string, object>>();
            }
            extraAttributes["petInfo"] = info;
        }

        return extraAttributes;
    }

#nullable enable
    /// <summary>
    /// Get modifications for given inventory
    /// </summary>
    /// <param name="inventory"></param>
    /// <param name="mcName"></param>
    /// <param name="sessionId"></param>
    /// <returns></returns>
    public async Task<IEnumerable<IEnumerable<DescModification>>> GetModifications(InventoryDataWithSettings inventory, string mcName, string sessionId)
    {
        var auctionRepresent = ConvertToAuctions(inventory);
        var menuItemName = auctionRepresent.Last().auction?.ItemName;
        if (inventory.ChestName == "Game Menu" || !(menuItemName?.Contains("§8Quiver ") ?? true) && (!menuItemName?.Contains("SkyBlock") ?? true))
        {
            logger.LogInformation("Skipping game menu " + menuItemName);
            return new List<List<DescModification>>(auctionRepresent.Count).Select(_ => new List<DescModification>());
        }

        var result = new List<List<DescModification>>();
        try
        {
            // compute descriptions and return everything computed on error
            await ComputeDescriptions(inventory, mcName, sessionId, auctionRepresent, result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to compute descriptions");
        }
        return result;
    }

    private async Task ComputeDescriptions(InventoryDataWithSettings inventory, string mcName, string sessionId, List<(SaveAuction auction, string[] desc)> auctionRepresent, List<List<DescModification>> result)
    {
        var pricesTask = GetPrices(auctionRepresent.Select(a => a.auction));
        CheckUpToDateCache();

        var (userSettings, userInfo) = await GetSettingForConid(mcName, sessionId);
        List<Item> items = new();
        try
        {
            items = ProduceInventory(inventory, mcName, userInfo.UserId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to publish inventory");
        }

        var span = Activity.Current;
        var none = new List<DescModification>();
        if (inventory.Settings == null)
            inventory.Settings = new DescriptionSetting();
        if (inventory.Settings.Fields == null || inventory.Settings.Fields.Count == 0)
        {
            inventory.Settings = userSettings;
        }

        var pricesPaidTask = GetPriceData(inventory, mcName, auctionRepresent);
        var bazaarPrices = deserializedCache.BazaarItems ?? new Dictionary<string, ItemPrice>();

        var salesData = await pricesPaidTask;
        var pricePaid = salesData.Where(p => p.Where(s => !s.requestingUserIsSeller && s.highest > 0 && s.end < DateTime.UtcNow).Any()).ToDictionary(p => p.Key, p =>
        {
            var sell = p.OrderByDescending(a => a.end).Where(s => !s.requestingUserIsSeller && s.highest > 0 && s.end < DateTime.UtcNow).FirstOrDefault();
            return (sell?.highest ?? -p.OrderByDescending(a => a.end).First().highest, sell?.end ?? default, sell?.Tag);
        });
        List<FlipTracker.Client.Model.Flip> flips = null;
        if (inventory.Settings.Fields.Any(f => f.Any(x => x == DescriptionField.FinderEstimates)))
        {
            // request finder estimates
            flips = await trackerApi.TrackerBatchFlipsPostAsync(salesData.Select(s => s.OrderByDescending(x => x.end).First().AuctionUid).ToList());
        }
        var res = await pricesTask;
        var allCrafts = deserializedCache.Crafts;
        var enabledFields = inventory.Settings.Fields;

        var container = new DataContainer
        {
            inventory = inventory,
            auctionRepresent = auctionRepresent,
            bazaarPrices = bazaarPrices,
            mods = result,
            pricesPaid = pricePaid,
            itemListings = salesData,
            katUpgradeCost = deserializedCache.Kat,
            PriceEst = res,
            modService = this,
            itemPrices = deserializedCache.ItemPrices,
            Items = items,
            allCrafts = allCrafts,
            accountInfo = userInfo,
            flips = flips?.ToLookup(f => f.AuctionId)
        };

        for (int i = 0; i < auctionRepresent.Count; i++)
        {
            var desc = auctionRepresent[i].desc;
            var auction = auctionRepresent[i].auction;
            var price = res?[i];
            if (desc == null || price == null)
            {
                span.Log(JsonConvert.SerializeObject(desc) + JsonConvert.SerializeObject(auction));
                result.Add(new());
                continue;
            }
            if (desc.Count() == 0)
            {
                span.Log("no desc");
                result.Add(none);
                continue;
            }
            List<DescModification> mods = GetModifications(enabledFields, auction, i, container);
            if (auction.Tag == "SKYBLOCK_MENU")
            {
                try
                {
                    AddSummaryToMenu(inventory, auctionRepresent, res, bazaarPrices, mods, pricePaid);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "failed to add summary");
                }
            }

            if (desc != null && span != null)
                span.Log(string.Join('\n', mods.Select(m => $"{m.Line} {m.Value}")) + JsonConvert.SerializeObject(auction, Formatting.Indented) + JsonConvert.SerializeObject(price, Formatting.Indented));
            result.Add(mods);
        }
        foreach (var item in customModifiers)
        {
            if (inventory.ChestName == null || !Regex.IsMatch(inventory.ChestName, item.Key))
                continue;
            try
            {
                item.Value.Apply(container);
            }
            catch (Exception e)
            {
                logger.LogError(e, "failed to apply custom modifier " + item.Key);
            }
        }
    }

    private void CheckUpToDateCache()
    {
        if (deserializedCache.LastUpdate < DateTime.UtcNow.AddMinutes(-2) && !deserializedCache.IsUpdating)
        {
            deserializedCache.IsUpdating = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var allCrafts = await craftsApi.CraftsAllGetAsync();
                    deserializedCache.Crafts = allCrafts.Where(c => c.CraftCost > 0).ToDictionary(c => c.ItemId, c => c);
                    deserializedCache.BazaarItems = (await bazaarApi.ApiBazaarPricesGetAsync())?.ToDictionary(p => p.ProductId);
                    deserializedCache.LastUpdate = DateTime.UtcNow;
                    var kat = await katApi.KatRawGetAsync();
                    deserializedCache.Kat = kat.ToDictionary(k => (k.ItemTag, Enum.Parse<Tier>(k?.BaseRarity?.ToString() ?? "LEGENDARY")));
                    await itemService.GetItemsAsync();
                    deserializedCache.ItemPrices = await sniperClient.GetCleanPrices();
                    logger.LogInformation($"Refreshed deserialized cache");
                    deserializedCache.IsUpdating = false;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "failed to update deserialized cache");
                }
            });
        }
    }
#nullable disable

    private void AddSummaryToMenu(
        InventoryDataWithSettings inventory,
        List<(SaveAuction auction, string[] desc)> auctionRepresent,
        List<Sniper.Client.Model.PriceEstimate> res,
        Dictionary<string, ItemPrice> bazaarPrices,
        List<DescModification> mods,
        Dictionary<string, (long, DateTime, string)> pricesPaid)
    {
        var take = 45;
        if (auctionRepresent.Count > take)
        {
            // container of some sort, only display whats in the container by taking the top
            take = auctionRepresent.Count - 36;
            mods.Add(new($"Chest Value Summary:"));
        }
        else
            mods.Add(new($"Inventory Value Summary:"));
        if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.MEDIAN)))
        {
            mods.Add(new($"Med summary: {McColorCodes.AQUA}{FormatPriceShort(res.Take(take).Sum(r => r?.Median ?? 0))}"));
        }
        if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.LBIN)))
        {
            mods.Add(new($"Lbin summary: {McColorCodes.YELLOW}{FormatPriceShort(res?.Take(take)?.Sum(r => r?.Lbin?.Price ?? 0) ?? -1)}"));
        }
        if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.PRICE_PAID)))
        {
            var uuidRelevant = auctionRepresent.Take(take).Select(a => a.auction).Where(a => a != null)
                    .Select(t => t.FlatenedNBT?.GetValueOrDefault("uid")).Where(t => t != default).ToHashSet();
            var relevantPrices = pricesPaid?.Where(p => uuidRelevant.Contains(p.Key))?.Sum(p => p.Value.Item1) ?? 0;
            mods.Add(new($"Price Paid summary: {McColorCodes.YELLOW}{FormatPriceShort(relevantPrices)}"));
        }
        if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.BazaarSell)))
        {
            var bazaarSellValue = auctionRepresent.Take(take).Select(a => a.auction).Where(a => a != null)
                    .Where(t => bazaarPrices?.ContainsKey(GetBazaarTag(t)) ?? false)
                    .Sum(t => bazaarPrices[GetBazaarTag(t)].SellPrice * (t.Count > 1 ? t.Count : 1));
            mods.Add(new($"Bazaar sell: {McColorCodes.GOLD}{FormatPriceShort(bazaarSellValue)}"));
        }
    }


    private async Task<ILookup<string, ListingSum>> GetPriceData(InventoryDataWithSettings inventory, string mcName, List<(SaveAuction auction, string[] desc)> auctionRepresent)
    {
        if (!inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.PRICE_PAID)))
            return Array.Empty<ListingSum>().ToLookup(a => "");
        var numericIds = auctionRepresent.Where(a => a.auction != null)
                .Select(a => a.auction.FlatenedNBT?.GetValueOrDefault("uid")).Where(v => v != null)
                .Distinct()
                .ToDictionary(uid => GetUidFromString(uid));
        var key = NBT.Instance.GetKeyId("uid");
        using var scope = scopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<HypixelContext>();
        var nameRequest = playerNameService.GetUuid(mcName);
        var lastSells = await context.Auctions
                    .Where(a => a.NBTLookup.Where(l => l.KeyId == key && numericIds.Keys.Contains(l.Value)).Any())
                    //.Where(a => a.HighestBidAmount > 0)
                    .AsSplitQuery().AsNoTracking()
                    .Select(a => new
                    {
                        a.HighestBidAmount,
                        a.StartingBid,
                        a.End,
                        a.AuctioneerId,
                        a.Start,
                        uid = a.NBTLookup.Where(l => l.KeyId == key).Select(l => l.Value).FirstOrDefault(),
                        auctionUid = a.UId,
                        a.Tag
                    })
                    .ToListAsync();
        var uuid = await nameRequest;
        return lastSells.ToLookup(g => numericIds[g.uid], a =>
        {
            return new ListingSum()
            {
                end = a.End,
                highest = a.HighestBidAmount,
                StartingBid = a.StartingBid,
                start = a.Start,
                requestingUserIsSeller = a.AuctioneerId == uuid,
                AuctionUid = a.auctionUid,
                Tag = a.Tag
            };
        });
    }

    private static long GetUidFromString(string u)
    {
        if (u.Length < 12)
            throw new CoflnetException("invalid_uuid", "One or more passed uuids are invalid (too short)");
        return NBT.UidToLong(u.Substring(u.Length - 12));
    }

    private async Task<(DescriptionSetting, AccountInfo)> GetSettingForConid(string playeruuid, string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return (DescriptionSetting.Default, new());
        if (settings.ContainsKey(sessionId))
            return (settings[sessionId].Item1.Value, settings[sessionId].Item2.Value);
        var conId = idConverter.ComputeConnectionId(playeruuid, sessionId).Item2;
        var userId = await settingsService.GetCurrentValue<string>("mod", conId, () => null);
        var userSettings = DescriptionSetting.Default;
        if (userId != null)
        {
            var accountInfoTask = SelfUpdatingValue<AccountInfo>.Create(userId, "accountInfo", () => new());
            var updatingSettings = await SelfUpdatingValue<DescriptionSetting>.Create(userId, "description", () => DescriptionSetting.Default);
            settings.TryAdd(sessionId, (updatingSettings, await accountInfoTask));
            userSettings = updatingSettings.Value;
            if (settings.Count > 1000)
            {
                settings.Clear();
            }
        }
        else
        {
            return (DescriptionSetting.Default, new());
        }

        return (settings[sessionId].Item1.Value, settings[sessionId].Item2.Value); ;
    }

    private List<DescModification> GetModifications(List<List<DescriptionField>> enabledFields,
                                                    SaveAuction auction,
                                                    int index,
                                                    DataContainer data)
    {
        var mods = new List<DescModification>();

        //if (desc.LastOrDefault()?.EndsWith("Click to open!") ?? false)
        //    mods.Add(new DescModification(DescModification.ModType.REPLACE, desc.Count() - 1, "Click to open"));
        if (auction.Tag == null)
        { //add nothing for now
            return new();
        }

        var builder = new StringBuilder(40);
        foreach (var line in enabledFields)
        {
            foreach (var item in line)
            {
                try
                {
                    AddFieldToBuilder(auction, index, data, builder, item);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "failed to add description element {item} on {auction}", item, JsonConvert.SerializeObject(auction));
                }
            }
            if (builder.Length > 0)
                mods.Add(new DescModification(builder.ToString()));
            builder.Clear();
        }


        return mods;
    }

    private void AddFieldToBuilder(SaveAuction auction, int index, DataContainer data, StringBuilder builder, DescriptionField item)
    {
        var price = data.PriceEst[index];
        switch (item)
        {
            case DescriptionField.LBIN:
                AddLbin(auction, price, builder);
                break;
            case DescriptionField.LBIN_KEY:
                builder.Append($"Lbin-Key: {price.LbinKey} ");
                break;
            case DescriptionField.MEDIAN:
                AddMedian(auction, price, builder);
                break;
            case DescriptionField.MEDIAN_KEY:
                builder.Append($"Med-Key: {price.MedianKey}");
                break;
            case DescriptionField.ITEM_KEY:
                builder.Append($"Item-Key: {price.ItemKey}");
                break;
            case DescriptionField.VOLUME:
                AddVolume(auction, price, builder);
                break;
            case DescriptionField.TAG:
                builder.Append($"{auction.Tag} ");
                break;
            case DescriptionField.BazaarBuy:
                AddBazaarBuy(auction, data.bazaarPrices, builder);
                break;
            case DescriptionField.BazaarSell:
                AddBazaarSell(auction, data.bazaarPrices, builder);
                break;
            case DescriptionField.EnchantCost:
                AddEnchantCost(auction, builder, data.bazaarPrices);
                break;
            case DescriptionField.PRICE_PAID:
                AddPricePaid(auction, data.pricesPaid, builder);
                break;
            case DescriptionField.CRAFT_COST:
                AddCraftcost(auction, data, builder);
                break;
            case DescriptionField.GemValue:
                AddGemValue(auction, builder, data.bazaarPrices);
                break;
            case DescriptionField.SpentOnAhFees:
                AddSpentOnAhFees(auction, builder, data);
                break;
            case DescriptionField.KatUpgradeCost:
                AddKatUpgradeCost(auction, builder, data);
                break;
            case DescriptionField.ModifierCost:
                AddModifierCost(auction, builder, data);
                break;
            case DescriptionField.ModifierCostList:
                AddModifierCostList(auction, builder, data);
                break;
            case DescriptionField.FullCraftCost:
                AddFullCraftCost(auction, builder, data);
                break;
            case DescriptionField.InstaSellPrice:
                AddInstasellEstimate(price, builder, data);
                break;
            case DescriptionField.FinderEstimates:
                AddFinderEstimates(auction, data, builder);
                break;
            case DescriptionField.LastSoldFor:
                AddLastSoldFor(auction, price, builder);
                break;
            case DescriptionField.Volatility:
                AddVolatility(auction, price, builder);
                break;
            case DescriptionField.NONE:
                break; // ignore
            default:
                if (Random.Shared.Next() % 100 == 0)
                    logger.LogError("Invalid description type " + item);
                break;
        }
    }

    private void AddVolatility(SaveAuction auction, Sniper.Client.Model.PriceEstimate data, StringBuilder builder)
    {
        var warning = data.Volatility switch
        {
            >= 30 => (McColorCodes.DARK_RED, "very high"),
            >= 20 => (McColorCodes.RED, "high"),
            >= 10 => (McColorCodes.YELLOW, "medium"),
            _ => (McColorCodes.GREEN, "low")
        };
        var formatted = data.Volatility > 119 ? "over 120" : data.Volatility.ToString();
        builder.Append($"{McColorCodes.GRAY}Volatility: {warning.Item1}{warning.Item2}{McColorCodes.GRAY}({formatted}) ");
    }

    private void AddLastSoldFor(SaveAuction auction, Sniper.Client.Model.PriceEstimate data, StringBuilder builder)
    {
        if (data.LastSale.Price == 0)
        {
            builder.Append($"{McColorCodes.GRAY}Last Sold For: {McColorCodes.YELLOW}Unknown ");
            return;
        }
        builder.Append($"{McColorCodes.GRAY}Last Sold For: {McColorCodes.YELLOW}{FormatPriceShort(data.LastSale.Price)} ");
    }

    private void AddFinderEstimates(SaveAuction auction, DataContainer data, StringBuilder builder)
    {
        if (auction.FlatenedNBT == null || !auction.FlatenedNBT.TryGetValue("uid", out var uid))
            return;

        if (!data.itemListings.Contains(uid))
            return;
        var listing = data.itemListings[uid].OrderByDescending(l => l.end).FirstOrDefault();
        if (listing == null)
            return;

        var flips = data.flips?[listing.AuctionUid];
        if (flips == null)
            return;
        foreach (var flip in flips)
        {
            builder.Append($"{McColorCodes.YELLOW}{flip.FinderType}: {McColorCodes.GRAY}{FormatPriceShort(flip.TargetPrice)} ");
        }
    }

    private void AddFullCraftCost(SaveAuction auction, StringBuilder builder, DataContainer data)
    {
        (double? craftPrice, double summary) value = FullCraftCost(auction, data);

        if (value.craftPrice == null || value.craftPrice == 0)
        {
            builder.Append($"{McColorCodes.GRAY}No Craft Cost found");
            return;
        }
        if (value.craftPrice.Value >= 10_000_000_000)
        {
            builder.Append($"{McColorCodes.GRAY}Craft ingredients unavailable");
            return;
        }
        builder.Append($"{McColorCodes.GRAY}Full Craft Cost: {McColorCodes.YELLOW}{FormatPriceShort(value.summary)}");
    }

    public (double? craftPrice, double summary) FullCraftCost(SaveAuction auction, DataContainer data)
    {
        double? craftPrice = BaseItemPrice(auction, data);
        double summary = craftPrice.Value + ModifierCostSum(auction, data) + EnchantCost(auction, data.bazaarPrices);
        var value = (craftPrice, summary);
        return value;
    }

    private static double? BaseItemPrice(SaveAuction auction, DataContainer data)
    {
        var craftPrice = data.allCrafts.GetValueOrDefault(auction.Tag)?.CraftCost;
        var clean = CleanItemprice(auction, data);
        craftPrice ??= clean;
        craftPrice = Math.Min(craftPrice ?? 0, clean);
        return craftPrice;
    }

    private static long CleanItemprice(SaveAuction auction, DataContainer data)
    {
        if (NBT.IsPet(auction.Tag))
        {
            var name = auction.ItemName;
            if (!name.Contains("Lvl"))
                name = data.auctionRepresent.Where(a => a.auction == auction).Select(a => a.desc).FirstOrDefault()?.FirstOrDefault(d => d.Contains("Lvl"));
            var level = int.Parse(Regex.Match(name, @"Lvl (\d+)").Groups[1].Value);
            var mapped = level switch
            {
                >= 100 => 100,
                >= 92 => 90,
                _ => 0
            };
            var key = $"{auction.Tag}_{auction.Tier}_{mapped}";
            if (data.itemPrices.TryGetValue(key, out var price))
                return price;

        }
        return data.itemPrices.GetValueOrDefault(auction.Tag);
    }

    private void AddModifierCostList(SaveAuction auction, StringBuilder builder, DataContainer data)
    {
        IEnumerable<List<(string id, int amount, double coins)>> cost = GetModifiersOnItem(auction, data);
        var all = cost.Zip(auction.FlatenedNBT);
        foreach (var item in all.Where(i => i.First.Count > 0))
        {
            builder.Append($"\n{McColorCodes.GRAY}{item.Second.Key}: {McColorCodes.YELLOW}{item.Second.Value} {McColorCodes.GRAY}\n"
                + $"=> {McColorCodes.YELLOW}{string.Join(", ", item.First.Select(i => $"{i.amount}x {i.id}{(i.coins > 0 ? $" +{FormatPriceShort(i.coins)} coins" : "")}"))}");
        }
    }

    private void AddModifierCost(SaveAuction auction, StringBuilder builder, DataContainer data)
    {
        double valueSum = ModifierCostSum(auction, data);
        builder.Append($"{McColorCodes.GRAY}Modifier Cost: {McColorCodes.YELLOW}{FormatPriceShort(valueSum)}");

    }

    private double ModifierCostSum(SaveAuction auction, DataContainer data)
    {
        IEnumerable<List<(string id, int amount, double coins)>> cost = GetModifiersOnItem(auction, data);

        var valueSum = cost.SelectMany(c => c).Select(d =>
        {
            return data.GetItemprice(d.id) * d.amount + d.coins;
        });
        return valueSum.Sum();
    }

    public IEnumerable<CraftPrice> GetItemValueBreakdown(SaveAuction auction)
    {
        var data = new DataContainer
        {
            auctionRepresent = new List<(SaveAuction auction, string[] desc)> { (auction, new string[0]) },
            bazaarPrices = deserializedCache.BazaarItems,
            itemPrices = deserializedCache.ItemPrices,
            allCrafts = deserializedCache.Crafts,
            modService = this
        };
        yield return new CraftPrice()
        {
            ItemTag = auction.Tag,
            Count = 1,
            Price = (long)BaseItemPrice(auction, data),
            FormattedReson = "Base Item price"
        };
        foreach (var item in GetEnchantBreakdown(auction, deserializedCache.BazaarItems))
        {
            yield return new CraftPrice()
            {
                ItemTag = "ENCHANTMENT_" + item.e.Type.ToString().ToUpper() + "_" + item.e.Level,
                Count = 1,
                Price = item.Item2,
                Attribute = "enchant_" + item.e.Type.ToString(),
                FormattedReson = $"Enchant {item.e.Type} {item.e.Level}"
            };
        }
        var i = 0;
        foreach (var item in GetModifiersOnItem(auction, data))
        {
            var modifier = auction.FlatenedNBT.Skip(i++).FirstOrDefault();
            foreach (var mod in item)
            {
                var itemPrice = 0L;
                if (!string.IsNullOrEmpty(mod.id))
                    itemPrice = data.GetItemprice(mod.id);
                yield return new CraftPrice()
                {
                    ItemTag = mod.id,
                    Count = mod.amount,
                    Price = itemPrice * mod.amount + (long)mod.coins,
                    Attribute = modifier.Key,
                    FormattedReson = $"Modifier {modifier.Key}={ItemDetails.TagToName(modifier.Value)}"
                };
            }
        }
    }

    public IEnumerable<List<(string id, int amount, double coins)>> GetModifiersOnItem(SaveAuction auction, DataContainer data)
    {
        var cost = auction.FlatenedNBT.Select(mod =>
        {
            if (data.GetItemprice(mod.Value.ToUpper()) > 0) // items in slot, to upper for drill parts
                return new() { (mod.Value, 1, 0) };
            if (mod.Key == "heldItem")
                return new() { (mod.Value, 1, 0) };
            if (mod.Key == "skin") // try again with pet skin prefix
                return new() { ("PET_SKIN_" + mod.Value, 1, 0) };
            if (mod.Value == "PERFECT" || mod.Value == "FLAWLESS" || mod.Value == "FINE")
                return new() { (mapper.GetItemKeyForGem(mod, auction.FlatenedNBT), 1, 0) };

            var itemIds = new List<(string id, int amount, double coins)>();
            if (mapper.TryGetIngredients(mod.Key, mod.Value, null, out var items))
                foreach (var item in items)
                {
                    itemIds.Add((item, 1, 0));
                }
            if (mod.Key == "upgrade_level")
            {
                itemIds.Add((null, 0, EstStarCost(auction.Tag, int.Parse(mod.Value))));
            }
            if (Constants.AttributeKeys.Contains(mod.Key))
            {
                itemIds.Add(($"{auction.Tag}+{mod.Key};1", (int)Math.Pow(2, int.Parse(mod.Value)), 0));
                // check for combo
                var attributes = auction.FlatenedNBT.Where(a => Constants.AttributeKeys.Contains(a.Key)).ToList();
                if (attributes.Count > 1 && attributes[0].Key == mod.Key)
                {
                    itemIds.Add(($"{auction.Tag}+{string.Join("_", attributes.Select(a => a.Value))}", 1, 0));
                }
            }
            if (mod.Key == "unlocked_slots")
            {
                var costs = itemService.GetSlotCostSync(auction.Tag, new(), mod.Value.Split(',').ToList());
                foreach (var cost in costs.Item1)
                {
                    itemIds.Add((cost.ItemId, cost.Amount ?? 1, cost.Coins));
                }
            }
            return itemIds;
        }).ToList();

        if (auction.Reforge != ItemReferences.Reforge.None && auction.Reforge != ItemReferences.Reforge.Unknown)
        {
            var reforgeCost = mapper.GetReforgeCost(auction.Reforge);
            if (reforgeCost.Item1 != null)
                cost.Add(new() { (reforgeCost.Item1, 1, reforgeCost.Item2) });
        }

        long EstStarCost(string item, int tier)
        {
            var items = itemService.GetStarIngredients(item, tier);
            var sum = 0;
            foreach (var ingred in items)
            {
                if (data.bazaarPrices.TryGetValue(ingred.itemId, out var cost))
                    sum += (int)cost.SellPrice * ingred.amount;
                else
                    sum += 1_000_000;
            }
            return sum;
        }

        return cost;
    }

    public long GetItemprice(string itemKey)
    {
        if (itemKey == null)
            return 0;
        if (deserializedCache.ItemPrices.TryGetValue(itemKey, out var price))
            return price;
        if (deserializedCache.BazaarItems.TryGetValue(itemKey, out var bazaarPrice))
            return (long)bazaarPrice.SellPrice;
        return 0;
    }

    private void AddInstasellEstimate(Sniper.Client.Model.PriceEstimate est, StringBuilder builder, DataContainer data)
    {
        builder.Append(ListPriceRecommend.GetRecommendText(est, this));
    }

    private void AddKatUpgradeCost(SaveAuction auction, StringBuilder builder, DataContainer data)
    {
        if (!data.katUpgradeCost.TryGetValue((auction.Tag, auction.Tier), out var cost))
            return;
        double materialCost = 0;
        if (!string.IsNullOrEmpty(cost.Material))
        {
            if (!data.bazaarPrices.TryGetValue(cost.Material, out var bazaarPrice))
            {
                logger.LogError($"No bazaar price for {cost.Material} on {auction.Tag} {auction.Tier}");

            }
            else
                materialCost = cost.Amount * bazaarPrice.BuyPrice;
        }
        var level = (float)int.Parse(Regex.Replace(auction.ItemName.Substring(2, 7), "[^0-9]", ""));
        var upgradeCost = cost.Cost * (1 - (level - 1) * 0.003);
        var totalCost = materialCost + upgradeCost;
        builder.Append($"{McColorCodes.GRAY}Kat Upgrade Cost: {McColorCodes.YELLOW}{FormatPriceShort(totalCost)}");
    }

    private void AddSpentOnAhFees(SaveAuction auction, StringBuilder builder, DataContainer data)
    {
        if (auction.FlatenedNBT == null || !auction.FlatenedNBT.TryGetValue("uid", out var uid))
            return;

        if (!data.itemListings.Contains(uid))
            return;
        var listing = data.itemListings[uid];
        var sum = listing.Where(l => l.requestingUserIsSeller && l.highest == 0).Sum(l => l.StartingBid * 0.02);
        var latest = listing.Where(l => l.requestingUserIsSeller && l.highest == 0).OrderByDescending(l => l.start).Select(l => l.start).FirstOrDefault();
        var formated = latest == default ? "never" : $"{FormatTime(DateTime.UtcNow - latest)} ago";
        builder.Append($"{McColorCodes.GRAY}Spent on listing: {McColorCodes.YELLOW}{FormatPriceShort(sum)}{McColorCodes.GRAY} Last attempt {McColorCodes.BLUE}{formated}");
    }

    private void AddGemValue(SaveAuction auction, StringBuilder builder, Dictionary<string, ItemPrice> bazaarPrices)
    {
        var sum = 0L;
        foreach (var prop in auction.FlatenedNBT)
        {
            if (prop.Value != "PERFECT" && prop.Value != "FLAWLESS" && prop.Value != "FINE")
                continue;
            var key = mapper.GetItemKeyForGem(prop, auction.FlatenedNBT);
            if (bazaarPrices.ContainsKey(key))
                sum += (long)bazaarPrices[key].SellPrice;
        }
        if (sum == 0)
            return;
        builder.Append($"{McColorCodes.GRAY}Gems: {McColorCodes.YELLOW}{FormatNumber(sum)} ");
    }



    private void AddEnchantCost(SaveAuction auction, StringBuilder builder, Dictionary<string, ItemPrice> bazaarPrices)
    {
        long enchantCost = EnchantCost(auction, bazaarPrices);
        builder.Append($"{McColorCodes.GRAY}Enchants: {McColorCodes.YELLOW}{FormatNumber(enchantCost)} ");
    }

    private long EnchantCost(SaveAuction auction, Dictionary<string, ItemPrice> bazaarPrices)
    {
        var enchants = auction.Enchantments;
        if (enchants == null || enchants.Count <= 0 || bazaarPrices == null)
            return 0;
        IEnumerable<(Enchantment e, long)> enchantValues = GetEnchantBreakdown(auction, bazaarPrices);
        var enchantCost = enchantValues.Sum(e => e.Item2);
        if (enchantCost < 0)
            enchantCost = 0;
        return enchantCost;
    }

    public IEnumerable<(Enchantment e, long)> GetEnchantBreakdown(SaveAuction auction, Dictionary<string, ItemPrice> bazaarPrices)
    {
        var enchants = auction.Enchantments;
        var lookup = bazaarPrices.ToDictionary(a => a.Key, a => a.Value.BuyPrice);
        var enchantValues = enchants.Select(e => (e, mapper.EnchantValue(e, auction.FlatenedNBT, lookup)));
        return enchantValues;
    }

    private void AddCraftcost(SaveAuction auction, DataContainer data, StringBuilder builder)
    {
        var craftPrice = data.allCrafts?.GetValueOrDefault(auction.Tag)?.CraftCost;
        if (craftPrice.HasValue)
        {
            if (craftPrice.Value >= int.MaxValue)
                builder.Append($"clean craft: unavailable ingredients ");
            else
                builder.Append($"{McColorCodes.GRAY}clean craft: {McColorCodes.YELLOW}{FormatNumber((long)craftPrice)} ");
        }
    }

    private void AddPricePaid(SaveAuction auction, Dictionary<string, (long, DateTime, string tag)> pricesPaid, StringBuilder builder)
    {
        if (auction.FlatenedNBT != null && auction.FlatenedNBT.ContainsKey("uid"))
        {
            var uid = auction.FlatenedNBT["uid"];
            if (!pricesPaid.ContainsKey(uid))
                return;

            builder.Append($"{McColorCodes.GRAY}Paid: {McColorCodes.YELLOW}{FormatNumber(pricesPaid[uid].Item1)}");
            if (pricesPaid[uid].Item2 < new DateTime(2029, 1, 1) && pricesPaid[uid].Item2 > new DateTime(2000, 1, 1))
                builder.Append($" {McColorCodes.DARK_GRAY}{FormatTime(DateTime.UtcNow - pricesPaid[uid].Item2)} ago");
            if (pricesPaid[uid].Item3 != null && pricesPaid[uid].Item3 != auction.Tag)
                builder.Append($" {McColorCodes.GRAY}(crafted)");
        }
    }

    private string FormatTime(TimeSpan timeSpan)
    {
        var prefix = timeSpan.TotalSeconds < 0 ? "-" : "";
        timeSpan = timeSpan.Duration();
        if (timeSpan.TotalDays > 1.05)
            return $"{timeSpan.TotalDays.ToString("0.#")}d";
        if (timeSpan.TotalHours > 1)
            return $"{timeSpan.TotalHours.ToString("0.#")}h";
        if (timeSpan.TotalMinutes > 1)
            return $"{timeSpan.TotalMinutes.ToString("0.#")}m";
        return $"{prefix}{timeSpan.TotalSeconds.ToString("0.#")}s";
    }

    private void AddBazaarBuy(SaveAuction auction, Dictionary<string, ItemPrice> bazaarPrices, StringBuilder builder)
    {
        AddBazaar(auction, bazaarPrices, builder, "Buy", (ItemPrice p) => p.BuyPrice);
    }

    private void AddBazaarSell(SaveAuction auction, Dictionary<string, ItemPrice> bazaarPrices, StringBuilder builder)
    {
        AddBazaar(auction, bazaarPrices, builder, "Sell", (ItemPrice p) => p.SellPrice);
    }

    private void AddBazaar(SaveAuction auction, Dictionary<string, ItemPrice> bazaarPrices, StringBuilder builder, string word, Func<ItemPrice, double> priceGet)
    {
        var tag = GetBazaarTag(auction);
        if (bazaarPrices?.ContainsKey(tag) ?? false)
        {
            var price = priceGet(bazaarPrices[tag]);
            if (auction.Count > 1)
                builder.Append($"{McColorCodes.GRAY}{word}: {McColorCodes.GOLD}{FormatPriceShort(price * auction.Count)} ({FormatPriceShort(price)} each)");
            else
                builder.Append($"{McColorCodes.GRAY}{word}: {McColorCodes.GOLD}{FormatPriceShort(price)} ");
        }
    }

    private void AddVolume(SaveAuction auction, Sniper.Client.Model.PriceEstimate price, StringBuilder builder)
    {
        if (price != null && price.Median != 0)
            if (float.IsInfinity(price.Volume))
                logger.LogInformation($"Volume is infinity for {auction.Tag} {price.ItemKey}");
        builder.Append($"{McColorCodes.GRAY}Vol: {McColorCodes.YELLOW}{price.Volume.ToString("0.#")} ");
    }

    private void AddMedian(SaveAuction auction, Sniper.Client.Model.PriceEstimate price, StringBuilder builder)
    {
        if (price != null && price.Median != 0)
        {
            var prefix = price.ItemKey == price.MedianKey.Replace("&comb", "") ? "" : "~";
            builder.Append($"{McColorCodes.GRAY}Med: {McColorCodes.AQUA}{prefix}{FormatNumber(price.Median)} ");
            if (auction.Count > 1)
            {
                builder.Append($"({FormatNumber(price.Median / auction.Count)} each)");
            }
        }
    }

    private void AddLbin(SaveAuction auction, Sniper.Client.Model.PriceEstimate price, StringBuilder builder)
    {
        if (price?.Lbin != null && price.Lbin.Price != 0)
        {
            if (price.ItemKey == price.LbinKey)
                builder.Append($"{McColorCodes.GRAY}lbin: {McColorCodes.YELLOW}{FormatNumber(price.Lbin.Price)} ");
            else
                builder.Append($"{McColorCodes.GRAY}lbin: ~{FormatNumber(price.Lbin.Price)} {McColorCodes.DARK_GRAY}(estimate, no match found)");
            if (auction.Count > 1)
            {
                builder.Append($"({FormatNumber(price.Lbin.Price / auction.Count)} each)");
            }
        }
    }

    private static string GetBazaarTag(SaveAuction auction)
    {
        var tag = auction.Tag;
        if (tag == "ENCHANTED_BOOK" && auction.Enchantments.Count == 1)
        {
            var enchant = auction.Enchantments.First();
            tag = "ENCHANTMENT_" + enchant.Type.ToString().ToUpper() + '_' + enchant.Level;
        }
        if (tag == null)
            return string.Empty;
        return tag;
    }

    public async Task<IEnumerable<string[]>> GetDescriptions(InventoryDataWithSettings inventory)
    {
        var auctionRepresent = ConvertToAuctions(inventory);

        var allCraftsTask = craftsApi.CraftsAllGetAsync();
        List<Sniper.Client.Model.PriceEstimate> res = await GetPrices(auctionRepresent.Select(a => a.auction));
        var allCrafts = await allCraftsTask;

        var result = new List<string[]>();
        for (int i = 0; i < auctionRepresent.Count; i++)
        {
            var desc = auctionRepresent[i].desc;
            var auction = auctionRepresent[i].auction;
            var price = res[i];
            if (desc == null || price == null)
            {
                result.Add(null);
                continue;
            }
            if (desc.Count() == 0)
            {
                result.Add(new string[] { "{line:0}" });
                continue;
            }
            var craftPrice = allCrafts?.Where(c => auction != null && c.ItemId == auction.Tag && c.CraftCost > 0)?.FirstOrDefault()?.CraftCost;
            var newOne = desc.Select((l, i) =>
            {
                if (l.StartsWith("§7Ends in") || l.StartsWith("§7Seller"))
                    return $"{{line:{i + 1}}}";
                return l;
            }).Prepend("{line:0}");

            if (desc.LastOrDefault()?.EndsWith("Click to open!") ?? false)
                newOne = newOne.Append("this is the menu");
            else if (price.Volume == 0 && !craftPrice.HasValue)
                newOne = newOne.Append("no auction price data");
            else
            {
                if (price.Lbin.Price > 0)
                    newOne = newOne.Append($"lbin: {FormatNumber(price.Lbin.Price)}");
                if (price.Lbin.Price > 0)
                    newOne = newOne.Append($"Med: {FormatNumber(price.Median)} Vol: {price.Volume.ToString("0.#")}");
                if (craftPrice != null)
                    if (craftPrice.Value >= int.MaxValue)
                        newOne = newOne.Append($"craft: unavailable ingredients");
                    else
                        newOne = newOne.Append($"craft: {FormatNumber((long)craftPrice)}");
            }
            result.Add(newOne.ToArray());
        }
        return result;
    }

    /// <summary>
    /// Parses nbt data from inventory to auctions
    /// </summary>
    /// <param name="inventory"></param>
    /// <returns></returns>
    public List<(SaveAuction auction, string[] desc)> ConvertToAuctions(InventoryData inventory)
    {
        if (inventory.JsonNbt != null)
        {
            return (new InventoryParser().Parse(inventory.JsonNbt) as IEnumerable<SaveAuction>)
                    .Select(a => (a, a?.Context?.GetValueOrDefault("lore")?.Split("\n") ?? new string[0])).ToList();
        }
        var nbtString = inventory.FullInventoryNbt;
        return GetAuctionsFromNbt(nbtString);
    }

    public List<(SaveAuction auction, string[] desc)> GetAuctionsFromNbt(string nbtString)
    {
        var nbt = NBT.File(Convert.FromBase64String(nbtString));
        var auctionRepresent = nbt.RootTag.Get<NbtList>("i").Select(t =>
        {
            try
            {
                var compound = t as NbtCompound;
                if (compound.Count == 0)
                    return (null, new string[0]);
                if (NBT.ItemID(compound) == null)
                    if (!NBT.GetName(compound)?.StartsWith("§8Quiver Arrow") ?? true)
                        return (null, new string[0]); // skip all items without id
                    else
                        // quiver arrow when selected a bow
                        return (new SaveAuction() { Tag = "SKYBLOCK_MENU", ItemName = "§8Quiver Arrow" }, new string[0]);
                var auction = new SaveAuction();
                auction.Context = new Dictionary<string, string>();
                NBT.FillFromTag(auction, compound, true);
                if (auction.Tier == Tier.UNKNOWN && (auction.Tag?.StartsWith("PET_") ?? false))
                {
                    var tier = auction.FlatenedNBT.Where(kv => kv.Key == "tier").Select(kv => kv.Value).FirstOrDefault();
                    if (tier != default && Enum.TryParse<Tier>(tier, true, out var parsedTier))
                        auction.Tier = parsedTier;
                }
                var desc = NBT.GetLore(compound);
                return (auction, desc.ToArray());
            }
            catch (Exception e)
            {
                logger.LogError(e, "parsing nbt to auction: " + t);
                return (null, new string[0]);
            }
        }).ToList();
        return auctionRepresent;
    }

    public async Task<List<Sniper.Client.Model.PriceEstimate>> GetPrices(IEnumerable<SaveAuction> auctionRepresent)
    {
        return await sniperClient.GetPrices(auctionRepresent);
    }

    public string FormatNumber(double price)
    {
        if (price < 1_000)
            return string.Format(CultureInfo.InvariantCulture, "{0:n1}", price);
        return string.Format(CultureInfo.InvariantCulture, "{0:n0}", price);
    }

    public long GetAuctionPrice(IEnumerable<string> desc)
    {
        return desc.Where(x => x.StartsWith(McColorCodes.GRAY + "Buy it now: §"))
                        .Select(x => long.Parse(x["xxBuy it now: §a".Length..].Replace(" coins", "").Replace(",", ""), NumberStyles.AllowThousands, CultureInfo.InvariantCulture)).FirstOrDefault();
    }

    /// <summary>
    /// By RenniePet on Stackoverflow
    /// https://stackoverflow.com/a/30181106
    /// </summary>
    /// <param name="num"></param>
    /// <returns></returns>
    public static string FormatPriceShort(double num)
    {
        if (num == 0) // there was an issue with flips attempting to be devided by 0
            return "0";
        var minusPrefix = num < 0 ? "-" : "";
        num = Math.Abs(num);
        // Ensure number has max 3 significant digits (no rounding up can happen)
        long i = (long)Math.Pow(10, (long)Math.Max(0, Math.Log10(num) - 2));
        num = num / i * i;

        if (num >= 1000000000)
            return Format(1000000000D, "B");
        if (num > 1000000 - 1)
            return Format(1000000D, "M");
        if (num > 999)
            return Format(1000D, "K");

        return Format(1D, "");

        string Format(double devider, string suffix)
        {
            return minusPrefix + (num / devider).ToString("0.##", CultureInfo.InvariantCulture) + suffix;
        }
    }

    /// <summary>
    /// Orderly dispose subscriptions
    /// </summary>
    public void Dispose()
    {
        foreach (var item in this.settings.ToList())
        {
            this.settings.TryRemove(item);
            item.Value.Item1?.Dispose();
            item.Value.Item2?.Dispose();
        }
    }
}
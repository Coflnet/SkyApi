using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenTracing;

namespace Coflnet.Sky.Api.Services;
public class ModDescriptionService : IDisposable
{
    private ICraftsApi craftsApi;
    private ISniperClient sniperClient;
    private ITracer tracer;
    private SettingsService settingsService;
    private IdConverter idConverter;
    private IServiceScopeFactory scopeFactory;
    private BazaarApi bazaarApi;
    private PlayerName.PlayerNameService playerNameService;
    private ILogger<ModDescriptionService> logger;
    private IConfiguration config;
    private IStateUpdateService stateService;
    private ClassNameDictonary<CustomModifier> customModifiers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ModDescriptionService"/> class.
    /// </summary>
    /// <param name="craftsApi"></param>
    /// <param name="tracer"></param>
    /// <param name="settingsService"></param>
    /// <param name="idConverter"></param>
    /// <param name="scopeFactory"></param>
    /// <param name="bazaarApi"></param>
    /// <param name="playerNameService"></param>
    /// <param name="logger"></param>
    /// <param name="config"></param>
    /// <param name="stateService"></param>
    /// <param name="sniperClient"></param>
    /// <param name="kafkaCreator"></param>
    public ModDescriptionService(ICraftsApi craftsApi,
                                 ITracer tracer,
                                 SettingsService settingsService,
                                 IdConverter idConverter,
                                 IServiceScopeFactory scopeFactory,
                                 BazaarApi bazaarApi,
                                 PlayerName.PlayerNameService playerNameService,
                                 ILogger<ModDescriptionService> logger,
                                 IConfiguration config,
                                 IStateUpdateService stateService,
                                 ISniperClient sniperClient,
                                 KafkaCreator kafkaCreator)
    {
        this.craftsApi = craftsApi;
        this.tracer = tracer;
        this.settingsService = settingsService;
        this.idConverter = idConverter;
        this.scopeFactory = scopeFactory;
        this.bazaarApi = bazaarApi;
        this.playerNameService = playerNameService;
        this.logger = logger;
        this.config = config;
        this.stateService = stateService;
        this.sniperClient = sniperClient;
        customModifiers.Add("You    ", new TradeWarning());
        customModifiers.Add("Create BIN", new ListPriceRecommend());
    }

    private ConcurrentDictionary<string, SelfUpdatingValue<DescriptionSetting>> settings = new();

    public List<Item> ProduceInventory(InventoryData modDescription, string playerId, string sessionId)
    {
        var inventoryhash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(modDescription.FullInventoryNbt));
        var nbt = NBT.File(Convert.FromBase64String(modDescription.FullInventoryNbt));
        try
        {
            var items = InventoryToItems(modDescription);
            ProduceInventory(modDescription.ChestName, playerId, sessionId, items);
            return items;
        }
        catch (System.Exception)
        {
            foreach (var item in InventoryToItems(modDescription))
            {
                try
                {
                    MessagePack.MessagePackSerializer.Serialize(item);
                }
                catch (System.Exception)
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
    /// <param name="sessionId"></param>
    /// <param name="items"></param>
    public void ProduceInventory(string chestName, string playerId, string sessionId, List<Item> items)
    {
        stateService.Produce(playerId, new()
        {
            Kind = UpdateMessage.UpdateKind.INVENTORY,
            Chest = new ChestView
            {
                Name = chestName,
                Items = items
            },
            SessionId = sessionId,
            ReceivedAt = DateTime.UtcNow
        });
        Console.WriteLine("produced state update " + playerId + " " + chestName);
    }

    private List<Item> InventoryToItems(InventoryData modDescription)
    {
        return NBT.File(Convert.FromBase64String(modDescription.FullInventoryNbt)).RootTag.Get<fNbt.NbtList>("i").Select(t =>
        {
            try
            {
                var compound = t as fNbt.NbtCompound;
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

                return item;
            }
            catch (System.Exception e)
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

    /// <summary>
    /// Get modifications for given inventory
    /// </summary>
    /// <param name="inventory"></param>
    /// <param name="mcName"></param>
    /// <param name="sessionId"></param>
    /// <returns></returns>
    public async Task<IEnumerable<IEnumerable<DescModification>>> GetModifications(InventoryDataWithSettings inventory, string mcName, string sessionId)
    {
        List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent = ConvertToAuctions(inventory);
        var menuItemName = auctionRepresent.Last().auction?.ItemName;
        if (inventory.ChestName == "Game Menu" || menuItemName != "§8Quiver Arrow" && !menuItemName.Contains("SkyBlock"))
        {
            logger.LogInformation("Skipping game menu " + menuItemName);
            return new List<List<DescModification>>(auctionRepresent.Count).Select(_ => new List<DescModification>());
        }
        var userSettings = await GetSettingForConid(mcName, sessionId);
        List<Item> items = new();
        try
        {
            items = ProduceInventory(inventory, mcName, sessionId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to publish inventory");
        }

        var allCraftsTask = craftsApi.CraftsAllGetAsync();
        var pricesTask = GetPrices(auctionRepresent.Select(a => a.auction));

        var span = tracer.ActiveSpan;
        var result = new List<List<DescModification>>();
        var none = new List<DescModification>();
        if (inventory.Settings == null)
            inventory.Settings = new DescriptionSetting();
        if (inventory.Settings.Fields == null || inventory.Settings.Fields.Count == 0)
        {
            inventory.Settings = userSettings;
        }

        var pricesPaidTask = GetPricePaidData(inventory, mcName, auctionRepresent);
        var bazaarPrices = new Dictionary<string, Bazaar.Client.Model.ItemPrice>();
        if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.BazaarBuy) || line.Contains(DescriptionField.BazaarSell)))
            bazaarPrices = (await bazaarApi.ApiBazaarPricesGetAsync())?.ToDictionary(p => p.ProductId);

        var pricesPaid = await pricesPaidTask;
        var res = await pricesTask;
        var allCrafts = await allCraftsTask;
        var enabledFields = inventory.Settings.Fields;

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
            var craftPrice = allCrafts?.Where(c => auction != null && c.ItemId == auction.Tag && c.CraftCost > 0)?.FirstOrDefault()?.CraftCost;
            List<DescModification> mods = GetModifications(enabledFields, desc, auction, price, craftPrice, pricesPaid, bazaarPrices);
            if (auction.Tag == "SKYBLOCK_MENU")
            {
                try
                {
                    AddSummaryToMenu(inventory, auctionRepresent, res, bazaarPrices, mods, pricesPaid);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "failed to add summary");
                }
            }

            if (desc != null)
                span.Log(string.Join('\n', mods.Select(m => $"{m.Line} {m.Value}")) + JsonConvert.SerializeObject(auction, Formatting.Indented) + JsonConvert.SerializeObject(price, Formatting.Indented) + "\ncraft:" + craftPrice);
            result.Add(mods);
        }
        foreach (var item in customModifiers)
        {
            if (!inventory.ChestName?.StartsWith(item.Key) ?? true)
                continue;
            try
            {
                item.Value.Apply(new DataContainer
                {
                    auctionRepresent = auctionRepresent,
                    bazaarPrices = bazaarPrices,
                    mods = result,
                    pricesPaid = pricesPaid,
                    res = res,
                    modService = this,
                    Items = items
                });
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "failed to apply custom modifier " + item.Key);
            }
        }
        return result;
    }

    private void AddSummaryToMenu(InventoryDataWithSettings inventory, List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent, List<Sniper.Client.Model.PriceEstimate> res, Dictionary<string, ItemPrice> bazaarPrices, List<DescModification> mods, Dictionary<string, long> pricesPaid)
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
            mods.Add(new($"Med summary: {McColorCodes.AQUA}{FormatNumber(res.Take(take).Sum(r => r?.Median ?? 0))}"));
        }
        if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.LBIN)))
        {
            mods.Add(new($"Lbin summary: {McColorCodes.YELLOW}{FormatNumber(res?.Take(take)?.Sum(r => r?.Lbin?.Price ?? 0) ?? -1)}"));
        }
        if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.PRICE_PAID)))
        {
            mods.Add(new($"Total Price Paid: {McColorCodes.YELLOW}{FormatNumber(pricesPaid?.Sum(p => p.Value) ?? 0)}"));
        }
        if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.BazaarSell)))
        {
            var bazaarSellValue = auctionRepresent.Take(take).Select(a => a.auction).Where(a => a != null)
                    .Where(t => bazaarPrices?.ContainsKey(GetBazaarTag(t)) ?? false)
                    .Sum(t => bazaarPrices[GetBazaarTag(t)].SellPrice * (t.Count > 1 ? t.Count : 1));
            mods.Add(new($"Bazaar sell: {McColorCodes.GOLD}{FormatNumber(bazaarSellValue)}"));
        }
    }

    private async Task<Dictionary<string, long>> GetPricePaidData(InventoryDataWithSettings inventory, string mcName, List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent)
    {
        if (!inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.PRICE_PAID)))
            return new Dictionary<string, long>();
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
                    .Where(a => a.HighestBidAmount > 0)
                    .AsSplitQuery().AsNoTracking()
                    .Select(a => new { a.HighestBidAmount, a.End, a.AuctioneerId, uid = a.NBTLookup.Where(l => l.KeyId == key).Select(l => l.Value).FirstOrDefault() })
                    .ToListAsync();
        var uuid = await nameRequest;
        return lastSells.GroupBy(l => l.uid).ToDictionary(g => numericIds[g.Key], g => g.OrderByDescending(a => a.End).Where(s => s.AuctioneerId != uuid).First().HighestBidAmount);
    }

    private static long GetUidFromString(string u)
    {
        if (u.Length < 12)
            throw new CoflnetException("invalid_uuid", "One or more passed uuids are invalid (too short)");
        return NBT.UidToLong(u.Substring(u.Length - 12));
    }

    private async Task<DescriptionSetting> GetSettingForConid(string playeruuid, string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return DescriptionSetting.Default;
        if (settings.ContainsKey(sessionId))
            return settings[sessionId].Value;
        var conId = idConverter.ComputeConnectionId(playeruuid, sessionId).Item2;
        var userId = await settingsService.GetCurrentValue<string>("mod", conId, () => null);
        var userSettings = DescriptionSetting.Default;
        if (userId != null)
        {
            var updatingSettings = await SelfUpdatingValue<DescriptionSetting>.Create(userId, "description", () => DescriptionSetting.Default);
            this.settings.TryAdd(sessionId, updatingSettings);
            userSettings = updatingSettings.Value;
        }

        return userSettings;
    }

    private List<DescModification> GetModifications(List<List<DescriptionField>> enabledFields,
                                                    IEnumerable<string> desc,
                                                    SaveAuction auction,
                                                    Sniper.Client.Model.PriceEstimate price,
                                                    double? craftPrice,
                                                    Dictionary<string, long> pricesPaid,
                                                    Dictionary<string, Bazaar.Client.Model.ItemPrice> bazaarPrices)
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
                        AddBazaarBuy(auction, bazaarPrices, builder);
                        break;
                    case DescriptionField.BazaarSell:
                        AddBazaarSell(auction, bazaarPrices, builder);
                        break;
                    case DescriptionField.EnchantCost:
                        AddEnchantCost(auction, builder, bazaarPrices);
                        break;
                    case DescriptionField.PRICE_PAID:
                        AddPricePaid(auction, pricesPaid, builder);
                        break;
                    case DescriptionField.CRAFT_COST:
                        AddCraftcost(craftPrice, builder);
                        break;
                    default:
                        if (Random.Shared.Next() % 100 == 0)
                            logger.LogError("Invalid description type " + item);
                        break;
                }
            }
            if (builder.Length > 0)
                mods.Add(new DescModification(builder.ToString()));
            builder.Clear();
        }


        return mods;
    }

    private void AddEnchantCost(SaveAuction auction, StringBuilder builder, Dictionary<string, ItemPrice> bazaarPrices)
    {
        var enchants = auction.Enchantments;
        if (enchants == null || enchants.Count <= 0 || bazaarPrices == null)
            return;
        var enchantCost = 0L;
        foreach (var enchant in enchants)
        {
            var key = $"ENCHANTMENT_{enchant.Type.ToString().ToUpper()}_{enchant.Level}";

            if (bazaarPrices.ContainsKey(key) && bazaarPrices[key].BuyPrice > 0)
                enchantCost += (long)(bazaarPrices[key].BuyPrice);
            else
            {
                // from lvl 1 ench
                key = $"ENCHANTMENT_{enchant.Type.ToString().ToUpper()}_1";
                if (bazaarPrices.ContainsKey(key) && bazaarPrices[key].BuyPrice > 0)
                    enchantCost += (long)(bazaarPrices[key].BuyPrice * Math.Pow(2, enchant.Level - 1));
            }

        }
        builder.Append($"{McColorCodes.GRAY}Enchants: {McColorCodes.YELLOW}{FormatNumber(enchantCost)} ");
    }

    private void AddCraftcost(double? craftPrice, StringBuilder builder)
    {
        if (craftPrice.HasValue)
        {
            if (craftPrice.Value >= int.MaxValue)
                builder.Append($"craft: unavailable ingredients ");
            else
                builder.Append($"{McColorCodes.GRAY}craft: {McColorCodes.YELLOW}{FormatNumber((long)craftPrice)} ");
        }
    }

    private void AddPricePaid(SaveAuction auction, Dictionary<string, long> pricesPaid, StringBuilder builder)
    {
        if (auction.FlatenedNBT != null && auction.FlatenedNBT.ContainsKey("uid"))
        {
            var uid = auction.FlatenedNBT["uid"];
            if (pricesPaid.ContainsKey(uid))
                builder.Append($"{McColorCodes.GRAY}Paid: {McColorCodes.YELLOW}{FormatNumber(pricesPaid[uid])} ");
        }
    }

    private void AddBazaarBuy(SaveAuction auction, Dictionary<string, ItemPrice> bazaarPrices, StringBuilder builder)
    {
        string tag = GetBazaarTag(auction);
        if (bazaarPrices?.ContainsKey(tag) ?? false)
            builder.Append($"{McColorCodes.GRAY}Buy: {McColorCodes.GOLD}{FormatNumber(bazaarPrices[tag].BuyPrice)} ");
    }

    private void AddBazaarSell(SaveAuction auction, Dictionary<string, ItemPrice> bazaarPrices, StringBuilder builder)
    {
        var tag = GetBazaarTag(auction);
        if (bazaarPrices?.ContainsKey(tag) ?? false)
            builder.Append($"{McColorCodes.GRAY}Sell: {McColorCodes.GOLD}{FormatNumber(bazaarPrices[tag].SellPrice)} ");
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
            var prefix = price.ItemKey == price.MedianKey ? "" : "~";
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
            var prefix = price.ItemKey == price.LbinKey ? "" : "~";
            builder.Append($"{McColorCodes.GRAY}lbin: {McColorCodes.YELLOW}{prefix}{FormatNumber(price.Lbin.Price)} ");
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
        List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent = ConvertToAuctions(inventory);

        var allCraftsTask = craftsApi.CraftsAllGetAsync();
        List<Sniper.Client.Model.PriceEstimate> res = await GetPrices(auctionRepresent.Select(a => a.auction));
        var allCrafts = await allCraftsTask;
        var span = tracer.ActiveSpan;
        span.Log(JsonConvert.SerializeObject(allCrafts));

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
            if (desc != null)
                span.Log(string.Join('\n', newOne) + JsonConvert.SerializeObject(auction, Formatting.Indented) + JsonConvert.SerializeObject(price, Formatting.Indented) + "\ncraft:" + craftPrice);
            result.Add(newOne.ToArray());
        }
        return result;
    }

    /// <summary>
    /// Parses nbt data from inventory to auctions
    /// </summary>
    /// <param name="inventory"></param>
    /// <returns></returns>
    public List<(SaveAuction auction, IEnumerable<string> desc)> ConvertToAuctions(InventoryData inventory)
    {
        if (inventory.JsonNbt != null)
        {
            return (new InventoryParser().Parse(inventory.JsonNbt) as IEnumerable<SaveAuction>)
                    .Select(a => (a, new string[0].AsEnumerable())).ToList();
        }
        var nbt = NBT.File(Convert.FromBase64String(inventory.FullInventoryNbt));
        var auctionRepresent = nbt.RootTag.Get<fNbt.NbtList>("i").Select(t =>
        {
            try
            {
                var compound = t as fNbt.NbtCompound;
                if (compound.Count == 0)
                    return (null, new string[0]);
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
                return (auction, desc);
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "parsing nbt to auction");
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

    /// <summary>
    /// Orderly dispose subscriptions
    /// </summary>
    public void Dispose()
    {
        foreach (var item in this.settings.ToList())
        {
            this.settings.TryRemove(item);
            item.Value.Dispose();
        }
    }
}
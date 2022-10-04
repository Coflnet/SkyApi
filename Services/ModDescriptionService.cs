using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.PlayerState.Models;
using Coflnet.Sky.Sniper.Client.Api;
using Confluent.Kafka;
using fNbt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using OpenTracing;
using RestSharp;

namespace Coflnet.Sky.Api.Services
{
    public class ModDescriptionService : IDisposable
    {
        private ICraftsApi craftsApi;
        private ISniperApi sniperApi;
        private RestSharp.RestClient sniperClient;
        private ITracer tracer;
        private SettingsService settingsService;
        private IdConverter idConverter;
        private IServiceScopeFactory scopeFactory;
        private BazaarApi bazaarApi;
        private PlayerName.PlayerNameService playerNameService;
        private ILogger<ModDescriptionService> logger;
        private IConfiguration config;
        IProducer<string, UpdateMessage> producer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModDescriptionService"/> class.
        /// </summary>
        /// <param name="craftsApi"></param>
        /// <param name="sniperApi"></param>
        /// <param name="tracer"></param>
        /// <param name="settingsService"></param>
        /// <param name="idConverter"></param>
        /// <param name="scopeFactory"></param>
        /// <param name="bazaarApi"></param>
        /// <param name="playerNameService"></param>
        /// <param name="logger"></param>
        public ModDescriptionService(ICraftsApi craftsApi,
                                     ISniperApi sniperApi,
                                     ITracer tracer,
                                     SettingsService settingsService,
                                     IdConverter idConverter,
                                     IServiceScopeFactory scopeFactory,
                                     BazaarApi bazaarApi,
                                     PlayerName.PlayerNameService playerNameService,
                                     ILogger<ModDescriptionService> logger,
                                     IConfiguration config)
        {
            this.craftsApi = craftsApi;
            this.sniperApi = sniperApi;
            this.tracer = tracer;
            this.settingsService = settingsService;
            this.idConverter = idConverter;
            this.scopeFactory = scopeFactory;
            this.bazaarApi = bazaarApi;
            this.playerNameService = playerNameService;
            this.logger = logger;
            sniperClient = new(sniperApi.GetBasePath());

            ProducerConfig producerConfig = new ProducerConfig
            {
                BootstrapServers = config["KAFKA_HOST"],
                LingerMs = 2
            };
            producer = new ProducerBuilder<string, UpdateMessage>(producerConfig).SetValueSerializer(SerializerFactory.GetSerializer<UpdateMessage>()).SetDefaultPartitioner((topic, pcount, key, isNull) =>
            {
                if (isNull)
                    return Random.Shared.Next() % pcount;
                return new Partition((key[0] << 8 + key[1]) % pcount);
            }).Build();
            this.config = config;
        }

        private ConcurrentDictionary<string, SelfUpdatingValue<DescriptionSetting>> settings = new();

        private void ProduceInventory(InventoryData modDescription, string playerId, string sessionId)
        {
            var inventoryhash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(modDescription.FullInventoryNbt));
            var nbt = NBT.File(Convert.FromBase64String(modDescription.FullInventoryNbt));
            producer.Produce(config["TOPICS:STATE_UPDATE"], new Message<string, UpdateMessage>
            {
                Key = string.IsNullOrEmpty(playerId) ? null : playerId.Substring(0, 4) + Encoding.UTF8.GetString(inventoryhash),
                Value = new()
                {
                    Kind = UpdateMessage.UpdateKind.INVENTORY,
                    Chest = new ChestView
                    {
                        Name = modDescription.ChestName,
                        Items = InventoryToItems(modDescription)
                    },
                    PlayerId = playerId,
                    SessionId = sessionId,
                    ReceivedAt = DateTime.UtcNow
                }
            });
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
                extraAttributes["petInfo"] = JsonConvert.DeserializeObject<Dictionary<string, object>>(petString);
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
        public async Task<IEnumerable<IEnumerable<DescModification>>> GetModifications(InventoryData inventory, string mcName, string sessionId)
        {
            List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent = ConvertToAuctions(inventory);
            var userSettings = await GetSettingForConid(mcName, sessionId);
            ProduceInventory(inventory, mcName, sessionId);

            var allCraftsTask = craftsApi.CraftsAllGetAsync();
            List<Sniper.Client.Model.PriceEstimate> res = await GetPrices(auctionRepresent);
            var allCrafts = await allCraftsTask;

            var span = tracer.ActiveSpan;
            var result = new List<List<DescModification>>();
            var none = new List<DescModification>();
            if (inventory.Settings == null)
                inventory.Settings = new DescriptionSetting();
            if (inventory.Settings.Fields == null || inventory.Settings.Fields.Count == 0)
            {
                inventory.Settings = userSettings;
            }

            var pricesPaid = new Dictionary<string, long>();
            if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.PRICE_PAID)))
            {
                var numericIds = auctionRepresent.Where(a => a.auction != null)
                        .Select(a => a.auction.FlatenedNBT?.GetValueOrDefault("uid")).Where(v => v != null)
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
                pricesPaid = lastSells.GroupBy(l => l.uid).ToDictionary(g => numericIds[g.Key], g => g.OrderByDescending(a => a.End).Where(s => s.AuctioneerId != uuid).First().HighestBidAmount);
            }
            var bazaarPrices = new Dictionary<string, Bazaar.Client.Model.ItemPrice>();
            if (inventory.Settings.Fields.Any(line => line.Contains(DescriptionField.BazaarBuy) || line.Contains(DescriptionField.BazaarSell)))
                bazaarPrices = (await bazaarApi.ApiBazaarPricesGetAsync())?.ToDictionary(p => p.ProductId);


            var enabledFields = inventory.Settings.Fields;

            for (int i = 0; i < auctionRepresent.Count; i++)
            {
                var desc = auctionRepresent[i].desc;
                var auction = auctionRepresent[i].auction;
                var price = res?[i];
                if (desc == null || price == null)
                {
                    span.Log(JsonConvert.SerializeObject(desc) + JsonConvert.SerializeObject(auction));
                    result.Add(none);
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

                if (desc != null)
                    span.Log(string.Join('\n', mods.Select(m => $"{m.Line} {m.Value}")) + JsonConvert.SerializeObject(auction, Formatting.Indented) + JsonConvert.SerializeObject(price, Formatting.Indented) + "\ncraft:" + craftPrice);
                result.Add(mods);
            }

            return result;
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
                return mods;
            }

            foreach (var line in enabledFields)
            {
                var content = "";
                foreach (var item in line)
                {
                    switch (item)
                    {
                        case DescriptionField.LBIN:
                            if (price?.Lbin != null && price.Lbin.Price != 0)
                            {
                                var prefix = price.ItemKey == price.LbinKey ? "" : "~";
                                content += $"{McColorCodes.GRAY}lbin: {McColorCodes.YELLOW}{prefix}{FormatNumber(price.Lbin.Price)} ";
                                if (auction.Count > 1)
                                {
                                    content += $"({FormatNumber(price.Lbin.Price / auction.Count)} each)";
                                }
                            }
                            break;
                        case DescriptionField.LBIN_KEY:
                            content += $"Lbin-Key: {price.LbinKey} ";
                            break;
                        case DescriptionField.MEDIAN:
                            if (price != null && price.Median != 0)
                            {
                                var prefix = price.ItemKey == price.MedianKey ? "" : "~";
                                content += $"{McColorCodes.GRAY}Med: {McColorCodes.AQUA}{prefix}{FormatNumber(price.Median)} ";
                                if (auction.Count > 1)
                                {
                                    content += $"({FormatNumber(price.Median / auction.Count)} each)";
                                }
                            }
                            break;
                        case DescriptionField.MEDIAN_KEY:
                            content += $"Med-Key: {price.MedianKey}";
                            break;
                        case DescriptionField.ITEM_KEY:
                            content += $"Item-Key: {price.ItemKey}";
                            break;
                        case DescriptionField.VOLUME:
                            if (price != null && price.Median != 0)
                                content += $"{McColorCodes.GRAY}Vol: {McColorCodes.YELLOW}{price.Volume.ToString("0.#")} ";
                            break;
                        case DescriptionField.TAG:
                            content += $"{auction.Tag} ";
                            break;
                        case DescriptionField.BazaarBuy:
                            string tag = GetBazaarTag(auction);
                            if (bazaarPrices?.ContainsKey(tag) ?? false)
                                content += $"{McColorCodes.GRAY}Buy: {McColorCodes.GOLD}{FormatNumber(bazaarPrices[tag].BuyPrice)} ";
                            break;
                        case DescriptionField.BazaarSell:
                            tag = GetBazaarTag(auction);
                            if (bazaarPrices?.ContainsKey(tag) ?? false)
                                content += $"{McColorCodes.GRAY}Sell: {McColorCodes.GOLD}{FormatNumber(bazaarPrices[tag].SellPrice)} ";
                            break;
                        case DescriptionField.PRICE_PAID:
                            if (auction.FlatenedNBT != null && auction.FlatenedNBT.ContainsKey("uid"))
                            {
                                var uid = auction.FlatenedNBT["uid"];
                                if (pricesPaid.ContainsKey(uid))
                                    content += $"{McColorCodes.GRAY}Paid: {McColorCodes.YELLOW}{FormatNumber(pricesPaid[uid])} ";
                            }
                            break;
                        case DescriptionField.CRAFT_COST:
                            if (!craftPrice.HasValue)
                                continue;
                            if (craftPrice.Value >= int.MaxValue)
                                content += $"craft: unavailable ingredients ";
                            else
                                content += $"{McColorCodes.GRAY}craft: {McColorCodes.YELLOW}{FormatNumber((long)craftPrice)} ";
                            break;
                        default:
                            if (Random.Shared.Next() % 100 == 0)
                                logger.LogError("Invalid description type " + item);
                            break;
                    }
                }
                if (content.Length > 0)
                    mods.Add(new DescModification(content));
            }


            return mods;
        }

        private static string GetBazaarTag(SaveAuction auction)
        {
            var tag = auction.Tag;
            if (tag == "ENCHANTED_BOOK" && auction.Enchantments.Count == 1)
            {
                var enchant = auction.Enchantments.First();
                tag = "ENCHANTMENT_" + enchant.Type.ToString().ToUpper() + '_' + enchant.Level;
            }
            return tag;
        }

        public async Task<IEnumerable<string[]>> GetDescriptions(InventoryData inventory)
        {
            List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent = ConvertToAuctions(inventory);

            var allCraftsTask = craftsApi.CraftsAllGetAsync();
            List<Sniper.Client.Model.PriceEstimate> res = await GetPrices(auctionRepresent);
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

        private List<(SaveAuction auction, IEnumerable<string> desc)> ConvertToAuctions(InventoryData inventory)
        {
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

        private async Task<List<Sniper.Client.Model.PriceEstimate>> GetPrices(List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent)
        {
            var request = new RestRequest("/api/sniper/prices", RestSharp.Method.Post);
            request.AddJsonBody(JsonConvert.SerializeObject(Convert.ToBase64String(MessagePack.LZ4MessagePackSerializer.Serialize(auctionRepresent.Select(a => a.auction)))));

            var respone = await sniperClient.ExecuteAsync(request);
            if (respone.StatusCode == 0)
            {
                logger.LogError("sniper service could not be reached");
                return auctionRepresent.Select(a => new Sniper.Client.Model.PriceEstimate()).ToList();
            }
            try
            {
                return JsonConvert.DeserializeObject<List<Sniper.Client.Model.PriceEstimate>>(respone.Content);
            }
            catch (System.Exception)
            {
                logger.LogError("responded with " + respone.StatusCode + respone.Content);
                throw;
            }
            /*return await sniperApi.ApiSniperPricePostAsync(auctionRepresent.Select(el =>
            {
                var a = el.auction;
                if (a == null)
                    return null;
                return new Sky.Sniper.Client.Model.SaveAuction()
                {
                    Count = a.Count,
                    Enchantments = a.Enchantments?.Select(e => new Sky.Sniper.Client.Model.Enchantment(0, (Sky.Sniper.Client.Model.EnchantmentType?)e.Type, e.Level)).ToList() ?? new(),
                    FlatenedNBT = a.FlatenedNBT,
                    Reforge = (Sky.Sniper.Client.Model.Reforge?)a.Reforge,
                    Tier = (Sky.Sniper.Client.Model.Tier?)a.Tier,
                    Tag = a.Tag
                };
            }).ToList());*/
        }

        private string FormatNumber(double price)
        {
            if (price < 1_000)
                return string.Format("{0:n1}", price);
            return string.Format("{0:n0}", price);
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
}
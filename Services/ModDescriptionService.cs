using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Sniper.Client.Api;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using OpenTracing;

namespace Coflnet.Sky.Api.Services
{
    public class ModDescriptionService
    {
        private ICraftsApi craftsApi;
        private ISniperApi sniperApi;
        private ITracer tracer;

        public ModDescriptionService(ICraftsApi craftsApi, ISniperApi sniperApi, ITracer tracer)
        {
            this.craftsApi = craftsApi;
            this.sniperApi = sniperApi;
            this.tracer = tracer;
        }

        public async Task<IEnumerable<IEnumerable<DescModification>>> GetModifications(InventoryData inventory)
        {
            List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent = ConvertToAuctions(inventory);

            var allCraftsTask = craftsApi.CraftsAllGetAsync();
            List<Sniper.Client.Model.PriceEstimate> res = await GetPrices(auctionRepresent);
            var allCrafts = await allCraftsTask;

            var span = tracer.ActiveSpan;
            var result = new List<List<DescModification>>();
            var none = new List<DescModification>();

            for (int i = 0; i < auctionRepresent.Count; i++)
            {
                var desc = auctionRepresent[i].desc;
                var auction = auctionRepresent[i].auction;
                var price = res[i];
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
                var mods = new List<DescModification>();

                mods.Add(new DescModification(auction.Tag));
                if (desc.LastOrDefault()?.EndsWith("Click to open!") ?? false)
                    mods.Add(new DescModification(DescModification.ModType.REPLACE, desc.Count() - 1, "Open cool menu :)"));
                else if (price.Volume == 0 && !craftPrice.HasValue)
                    mods.Add(new DescModification("no auction price data"));
                else
                {
                    if (price.Lbin.Price > 0)
                        mods.Add(new DescModification($"lbin: {FormatNumber(price.Lbin.Price)}"));
                    if (price.Lbin.Price > 0)
                        mods.Add(new DescModification($"Med: {FormatNumber(price.Median)} Vol: {price.Volume.ToString("0.#")}"));
                    if (craftPrice != null)
                        if (craftPrice.Value >= int.MaxValue)
                            mods.Add(new DescModification($"craft: unavailable ingredients"));
                        else
                            mods.Add(new DescModification($"craft: {FormatNumber((long)craftPrice)}"));
                }
                if (desc != null)
                    span.Log(string.Join('\n', mods.Select(m => $"{m.Line} {m.Value}")) + JsonConvert.SerializeObject(auction, Formatting.Indented) + JsonConvert.SerializeObject(price, Formatting.Indented) + "\ncraft:" + craftPrice);
                result.Add(mods);
            }

            return result;
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

        private static List<(SaveAuction auction, IEnumerable<string> desc)> ConvertToAuctions(InventoryData inventory)
        {
            var nbt = NBT.File(Convert.FromBase64String(inventory.FullInventoryNbt));
            var auctionRepresent = nbt.RootTag.Get<fNbt.NbtList>("i").Select(t =>
            {
                var compound = t as fNbt.NbtCompound;

                if (compound.Count == 0)
                    return (null, new string[0]);
                var auction = new SaveAuction();
                auction.Context = new Dictionary<string, string>();
                NBT.FillFromTag(auction, compound, true);
                var desc = NBT.GetLore(compound);
                //Console.WriteLine(JsonConvert.SerializeObject(auction));
                //Console.WriteLine(JsonConvert.SerializeObject(auction.Context));
                return (auction, desc);
            }).ToList();
            return auctionRepresent;
        }

        private async Task<List<Sniper.Client.Model.PriceEstimate>> GetPrices(List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent)
        {
            return await sniperApi.ApiSniperPricePostAsync(auctionRepresent.Select(el =>
            {
                var a = el.auction;
                if (a == null)
                    return null;
                return new Sky.Sniper.Client.Model.SaveAuction()
                {
                    Count = a.Count,
                    Enchantments = a.Enchantments.Select(e => new Sky.Sniper.Client.Model.Enchantment(0, (Sky.Sniper.Client.Model.EnchantmentType?)e.Type, e.Level)).ToList(),
                    FlatenedNBT = a.FlatenedNBT,
                    Reforge = (Sky.Sniper.Client.Model.Reforge?)a.Reforge,
                    Tier = (Sky.Sniper.Client.Model.Tier?)a.Tier,
                    Tag = a.Tag
                };
            }).ToList());
        }

        private string FormatNumber(long price)
        {
            return string.Format("{0:n0}", price);
        }

        /// <summary>
        /// Representation of an inventory
        /// </summary>
        public class InventoryData
        {
            public string ChestName;
            public string FullInventoryNbt;
        }

        public class DescModification
        {
            /// <summary>
            /// What type of modification to make
            /// </summary>
            /// <value></value>
            [JsonConverter(typeof(StringEnumConverter))]
            public ModType Type { get; set; }
            /// <summary>
            /// Extra field containing index to insert (int), or value to replace (string)
            /// </summary>
            /// <value></value>
            public int Line { get; set; }
            /// <summary>
            /// New value to add,insert, or replace something with
            /// </summary>
            /// <value></value>
            public string Value { get; set; }

            [JsonConverter(typeof(StringEnumConverter))]
            public enum ModType
            {
                NONE,
                INSERT,
                REPLACE,
                APPEND,
                DELETE
            }

            public DescModification(ModType type, int target, string value)
            {
                Type = type;
                Line = target;
                Value = value;
            }
            public DescModification(string value) : this(ModType.APPEND, 0, value)
            { }
            public DescModification()
            {
            }


        }
    }
}
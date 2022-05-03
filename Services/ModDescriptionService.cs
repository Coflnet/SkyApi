using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Sniper.Client.Api;
using Newtonsoft.Json;
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

        public async Task<IEnumerable<string[]>> GetDescriptions(InventoryData inventory)
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

            var allCraftsTask = craftsApi.CraftsAllGetAsync();
            var res = await sniperApi.ApiSniperPricePostAsync(auctionRepresent.Select(el =>
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
            var allCrafts = await allCraftsTask;
            Console.WriteLine(JsonConvert.SerializeObject(allCrafts));
            var span = tracer.ActiveSpan;

            var result = new List<string[]>();
            for (int i = 0; i < auctionRepresent.Count; i++)
            {
                var desc = auctionRepresent[i].desc;
                var auction = auctionRepresent[i].auction;
                var price = res[i];
                var craftPrice = allCrafts?.Where(c => auction != null && c.ItemId == auction.Tag && c.CraftCost > 0)?.FirstOrDefault()?.CraftCost;
                if (desc != null)
                    span.Log(JsonConvert.SerializeObject(auction, Formatting.Indented) + string.Join('\n', desc) + JsonConvert.SerializeObject(price, Formatting.Indented) + "\ncraft:" + craftPrice);
                if (desc == null || price == null)
                    result.Add(null);
                else if (desc.LastOrDefault()?.EndsWith("Click to open!") ?? false)
                    result.Add(desc.Append("this is the menu").ToArray());
                else if (price.Volume == 0 && !craftPrice.HasValue)
                    result.Add(desc.Append("no auction price data").ToArray());
                else
                {
                    var newOne = desc.AsEnumerable();
                    if (price.Lbin.Price > 0)
                        newOne = desc.Append($"lbin: {FormatNumber(price.Lbin.Price)}");
                    if (price.Lbin.Price > 0)
                        newOne = desc.Append($"Med: {FormatNumber(price.Median)} Vol: {price.Volume.ToString("0.#")}");
                    if (craftPrice != null)
                        newOne = desc.Append($"craft: {FormatNumber((int)craftPrice)}");
                    result.Add(newOne.ToArray());
                }
            }
            return result;
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
    }
}
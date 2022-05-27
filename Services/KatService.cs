using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Commands.Shared;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using System.Collections.Generic;
using Coflnet.Sky.Crafts.Client.Model;
using System.Linq;
using System;

namespace Coflnet.Sky.Api.Services
{
    public class KatService
    {
        private KatApi katApi;
        PricesService pricesService;
        public KatService(IConfiguration config, PricesService pricesService)
        {
            katApi = new("http://" + config["CRAFTS_HOST"]);
            this.pricesService = pricesService;
        }

        public async Task<IEnumerable<KatFlip>> GetProfitable()
        {
            var flips = await katApi.KatProfitGetAsync();
            return await AddSaleData(flips);
        }
        public async Task<IEnumerable<Models.KatUpgradeCost>> GetRawData()
        {
            return (await katApi.KatRawGetAsync()).Select(c => new Models.KatUpgradeCost(c));
        }

        private async Task<IEnumerable<KatFlip>> AddSaleData(List<KatUpgradeResult> list)
        {
            var taskList = list.Select((async i =>
            {
                try
                {
                    var flip = new KatFlip(i);
                    var filter = new Dictionary<string, string>() { { "Rarity", i.TargetRarity.ToString() } };
                    var sumary = await pricesService.GetSumaryCache(i.CoreData.ItemTag, filter);
                    flip.Volume = sumary.Volume;
                    flip.Median = sumary.Med;
                    return flip;
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "getting price summary for kat flips");
                }
                return null;
            })).Select(t => t.ConfigureAwait(false));
            var result = new List<KatFlip>();
            foreach (var item in taskList)
            {
                var flip = await item;
                if (flip != null && flip.Volume > 2)
                    result.Add(flip);
            }
            return result.OrderByDescending(r => r.Profit / r.CoreData.Hours);
        }
    }
}
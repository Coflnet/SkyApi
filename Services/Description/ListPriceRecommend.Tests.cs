using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Core;
using Coflnet.Sky.Sniper.Client.Model;
using Newtonsoft.Json;
using NUnit.Framework;

namespace SkyApi.Services.Description;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class ListPriceRecommendTests
{
    /// <summary>
    /// Regression: items that were never sent as flip have no <see cref="ListPriceRecommend.PriceInfo.Recommended"/>,
    /// accessing its value threw and left the "Create BIN Auction" menu without any info display.
    /// </summary>
    [Test]
    public void LowVolumeWithoutFlipEstimateShowsNotSoldOften()
    {
        var data = CreateData(new PriceEstimate { Median = 29_000, Volume = 0.2f, ItemKey = "drill", MedianKey = "drill" });

        new ListPriceRecommend().Apply(data);

        var display = data.mods.Last();
        display.Select(m => m.Value).Should().Contain("Looks like this is not sold often");
        display.Should().NotContain(m => m.Type == DescModification.ModType.SUGGEST);
    }

    [Test]
    public void NonExactMatchWithoutFlipEstimateShowsTooFewSales()
    {
        var data = CreateData(new PriceEstimate { Median = 29_000, Volume = 5, ItemKey = "drill", MedianKey = "other" });

        new ListPriceRecommend().Apply(data);

        var display = data.mods.Last();
        display.Select(m => m.Value).Should().Contain("item has too few similar sales");
        display.Should().NotContain(m => m.Type == DescModification.ModType.SUGGEST);
    }

    private static DataContainer CreateData(PriceEstimate estimate)
    {
        var priceEst = Enumerable.Repeat<PriceEstimate>(null, 45).ToList();
        priceEst[13] = estimate;
        var items = Enumerable.Range(0, 45).Select(_ => new Item()).ToList();
        items[31] = new Item { ItemName = "§fItem price: §629,113 coins" };
        return new DataContainer
        {
            inventory = new InventoryDataWithSettings { ChestName = "Create BIN Auction", Settings = new() },
            Items = items,
            auctionRepresent = Enumerable.Range(0, 45).Select(_ => (new Coflnet.Sky.Core.SaveAuction(), new string[0])).ToList(),
            PriceEst = priceEst,
            mods = Enumerable.Range(0, 45).Select(_ => new List<DescModification>()).ToList(),
            Loaded = new Dictionary<string, Task<string>>
            {
                // no flip was sent for the item, so nothing is recommended
                { nameof(ListPriceRecommend), Task.FromResult(JsonConvert.SerializeObject(new ListPriceRecommend.PriceInfo())) }
            }
        };
    }
}

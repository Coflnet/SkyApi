using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Mayor.Client.Api;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;
/// <summary>
/// Tests for <see cref="AuctionConverter"/>
/// </summary>
public class AuctionsConverterTests
{
    /// <summary>
    /// Test search result threshold
    /// </summary>
    [Test]
    public async Task MinResultCount()
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
        var mayorApi = new Mock<IElectionPeriodsApi>();
        var craftsApi = new Mock<ICraftsApi>();
        var mappingCenter = new MappingCenter(new Core.Services.HypixelItemService(new System.Net.Http.HttpClient(), NullLogger<HypixelItemService>.Instance), get =>
        {
            return Task.FromResult(new Dictionary<DateTime, long>(){
                {DateTime.UtcNow.Date - TimeSpan.FromDays(2), 500},
                {DateTime.UtcNow.Date - TimeSpan.FromDays(1), 1000},
                {DateTime.UtcNow.Date, 2000}
            });
        });
        var tag = "HYPERION";
        await mappingCenter.SetIngredientsFor("HYPERION", new List<string>(){
            "NECRON_BLADE",
            "NECRON_HANDLE",
            "WITHER_CATALYST"
        });
        var converter = new AuctionConverter(mayorApi.Object, NullLogger<AuctionConverter>.Instance, mappingCenter, craftsApi.Object);
        var itemModifiers = new Dictionary<string, List<string>>(){
            {"ability_scroll", ["IMPLOSION_SCROLL"]},
            {"!enchcritical", ["6"]}
        };
        await mappingCenter.Load();
        var keys = converter.ColumnKeys(itemModifiers.Keys).ToHashSet();
        var craftItems = converter.GetRelevantIngredients(tag);
        var outputColumns = converter.Createmap(keys.ToList(), itemModifiers).Concat(craftItems).ToList();
        var auction = new SaveAuction()
        {
            FlatenedNBT = new Dictionary<string, string>(){
                {"ability_scroll", "IMPLOSION_SCROLL"}
            },
            Tag = tag,
            Uuid = "a23",
            End = DateTime.UtcNow - TimeSpan.FromDays(1),
            HighestBidAmount = 1_000_000_000,
            Enchantments = [new(Enchantment.EnchantmentType.critical, 6)]
        };

        outputColumns.Should().BeEquivalentTo(new[]{"sold_for", "count", "ACTIVE_event:None", "ACTIVE_event:TravelingZoo", "ACTIVE_event:SpookyFestival", "ACTIVE_event:DarkAuction", "ACTIVE_event:NewYear", "ACTIVE_event:SeasonOfJerry", 
                "ability_scroll:IMPLOSION_SCROLL", "!enchcritical:6", "NECRON_BLADE", "NECRON_HANDLE", "WITHER_CATALYST"});
        var floats = await converter.MapAsFrame(auction, keys.ToList(), itemModifiers, outputColumns);
        floats.Should().BeEquivalentTo($"a23;0.1;0;1;0;0;0;0;0;1E-07;1E-07;1E-07;1E-07;1E-07\n");
    }
}



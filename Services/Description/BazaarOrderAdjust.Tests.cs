using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using AwesomeAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace SkyApi.Services.Description;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class BazaarOrderAdjustTests
{
    [Test]
    public void Apply_OfferDescriptionWithoutPricePerUnit_DoesNotThrow()
    {
        // An order slot whose description lacks a "§7Price per unit: §6" line previously caused
        // a "Sequence contains no elements" exception via the inner .First()/.Max() calls.
        var tag = "ENCHANTED_DIAMOND";
        var orderBooks = new Dictionary<string, OrderBook>
        {
            { tag, new OrderBook {
                Buy = new List<OrderEntry> { new() { Amount = 10, PricePerUnit = 100 } },
                Sell = new List<OrderEntry> { new() { Amount = 5, PricePerUnit = 120 } }
            }}
        };
        var data = new DataContainer
        {
            inventory = new InventoryDataWithSettings { Version = 2 },
            auctionRepresent = new List<(SaveAuction auction, string[] desc)>
            {
                (new SaveAuction { Tag = tag, ItemName = "§6BUY" },
                    new []{ "§7Some line without a price", "§7Filled: §60§7/10" })
            },
            mods = new List<List<DescModification>> { new() },
            Loaded = new Dictionary<string, Task<string>>
            {
                { nameof(BazaarOrderAdjust), Task.FromResult(JsonConvert.SerializeObject(orderBooks)) }
            }
        };

        var modifier = new BazaarOrderAdjust(null);

        var act = () => modifier.Apply(data);

        act.Should().NotThrow();
    }
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

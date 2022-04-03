using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.PlayerName.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Api.Models;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Special endpoints for mods.
    /// Returns information about mod related things. e.g. available socket commands for a help text
    /// </summary>
    [ApiController]
    [Route("api/mod")]
    [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class ModController : ControllerBase
    {
        private HypixelContext db;
        private PricesService priceService;
        private PlayerNameApi playerNamService;

        public ModController(HypixelContext db, PricesService pricesService, PlayerNameApi playerName = null)
        {
            this.db = db;
            priceService = pricesService;
            this.playerNamService = playerName;
        }

        /// <summary>
        /// Returns a list of available server-side commands
        /// </summary>
        [Route("commands")]
        [HttpGet]
        public IEnumerable<CommandListEntry> GetSumary()
        {
            return new List<CommandListEntry>()
            {
                new CommandListEntry("report {message}","Creates an error report with an optional message"),
                new CommandListEntry("online", "Tells you how many connections there are to the server"),
                new CommandListEntry("reset", "Resets the mod (deletes everything)"),
                new CommandListEntry("profit 7","Shows your profit from flips in the last week")
            };
        }

        /// <summary>
        /// Returns extra information for an item
        /// </summary>
        [Route("item/{uuid}")]
        [HttpGet]
        public async Task<string> ItemDescription(string uuid, int count = 1)
        {
            if (uuid.Length < 32 && uuid.Length != 12)
            {
                if (ItemDetails.Instance.GetItemIdForTag(uuid) == 0)
                    throw new CoflnetException("invalid_id", "the passed id does not map to an item");
                var median = await priceService.GetSumary(uuid, new Dictionary<string, string>());
                return $"Median sell for {count} is {FormatPrice(median.Med)}";
            }

            var lookupId = NBT.UidToLong(uuid.Length == 12 ? uuid : uuid.Substring(24));
            var key = NBT.GetLookupKey("uId");
            var auctions = await db.Auctions.Where(a => a.NBTLookup.Where(l => l.KeyId == key && l.Value == lookupId).Any()).Include(a => a.Bids).OrderByDescending(a => a.End).ToListAsync();
            var lastSell = auctions.Where(a => a.End < System.DateTime.Now).FirstOrDefault();
            long med = await GetMedian(lastSell);
            var playerName = await playerNamService.PlayerNameNameUuidGetAsync(lastSell.Bids.FirstOrDefault()?.Bidder);
            if (lastSell == null)
                return "Item has no recorded sell";
            return $"Sold {auctions.Count} times\n"
                + (lastSell == null ? "" : $"last sold for {FormatPrice(lastSell.HighestBidAmount)} to {playerName?.Trim('"')}")
                + (auctions.Count == 0 ? "" : $"Median {FormatPrice(med)}");
        }
        /// <summary>
        /// Returns new descriptions for an array of items
        /// </summary>
        [Route("description")]
        [HttpPost]
        public async Task<IEnumerable<string[]>> ItemDescription([FromBody] InventoryData inventory, [FromHeader] string conId, [FromHeader] string uuid)
        {
            var name = await db.Players.Where(p=>p.UuId == uuid).Select(p=>p.Name).FirstOrDefaultAsync();
            return inventory.Items.Select(d => d.Tag.Display.Lore.Append("test line " + name).ToArray());
        }

        public class InventoryData
        {
            public string ChestName;
            public ItemNbtData[] Items;
        }

        public class ItemNbtData
        {
            public byte Count;
            public int Id;
            public ItemTag Tag;
        }

        public class ItemTag
        {
            public Display Display;
            public Dictionary<string, string> ExtraAttributes;
        }

        public class Display
        {
            public string[] Lore;
            public string Name;
        }

        private static async Task<long> GetMedian(SaveAuction lastSell)
        {
            var references = await FlipperService.Instance.GetReferences(lastSell.Uuid);
            var med = references.OrderByDescending(r => r.HighestBidAmount).Skip(references.Count / 2).FirstOrDefault()?.HighestBidAmount ?? 0;
            return med;
        }

        private static string FormatPrice(long price)
        {
            return string.Format("{0:n0}", price);
        }
    }
}


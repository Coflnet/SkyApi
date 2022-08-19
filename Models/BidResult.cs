
using System;
using System.Text.Json.Serialization;
using Coflnet.Sky.Core;
using MessagePack;
using System.Linq;

namespace Coflnet.Sky.Api.Models
{
    [MessagePackObject]
    public class BidResult : AuctionResult
    {
        /// <summary>
        /// The highest bid the requesting player has made
        /// </summary>
        [Key("highestOwn")]
        public long HighestOwnBid;

        public BidResult(SaveAuction a, string userUuid)
        {
            var highestOwn = a.Bids?.Where(bid => bid.Bidder == userUuid)
                        .OrderByDescending(bid => bid.Amount).FirstOrDefault();

            AuctionId = a.Uuid;
            if (a.Bids != null)
                HighestBid = a.Bids.Last().Amount;
            if (highestOwn != null)
                HighestOwnBid = highestOwn.Amount;
            ItemName = a.ItemName;
            End = a.End;
            Tag = a.Tag;
        }

        public BidResult() { }
    }


    [MessagePackObject]
    public class PaginatedRequest
    {
        [Key("uuid")]
        public string Uuid;

        [Key("amount")]
        public int Amount;

        [Key("offset")]
        public int Offset;
    }
}
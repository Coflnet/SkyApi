
using System;
using System.Text.Json.Serialization;
using Coflnet.Sky.Core;
using MessagePack;
using System.Linq;

namespace Coflnet.Sky.Api.Models
{
    /// <summary>
    /// Paginated bid result for player overview
    /// </summary>
    [MessagePackObject]
    public class BidResult : AuctionResult
    {
        /// <summary>
        /// The highest bid the requesting player has made
        /// </summary>
        [Key("highestOwn")]
        public long HighestOwnBid;

        /// <summary>
        /// Creates a new instance of <see cref="BidResult"/>
        /// </summary>
        /// <param name="a"></param>
        /// <param name="userUuid"></param>
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

        /// <summary>
        /// Creates a new instance of <see cref="BidResult"/>
        /// </summary>
        public BidResult() { }
    }
}
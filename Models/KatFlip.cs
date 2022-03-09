using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Api.Models
{
    public class KatFlip : KatUpgradeResult
    {
        public KatFlip(KatUpgradeResult up) : base(up.CoreData, up.OriginAuction, up.TargetRarity, up.ReferenceAuction)
        {
        }

        public float Volume;
        public long Median;
    }
}
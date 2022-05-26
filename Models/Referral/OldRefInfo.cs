using System;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models.Referral
{
#pragma warning disable CS1591
    [DataContract]
    public class OldRefInfo
    {
        [DataMember(Name = "refId")]
        public string RefId;
        [DataMember(Name = "count")]
        public int ReferCount;
        [DataMember(Name = "receivedTime")]
        public TimeSpan ReceivedTime;
        [DataMember(Name = "receivedHours")]
        public int ReceivedHours;
        [DataMember(Name = "bougthPremium")]
        public int BougthPremium;
    }
#pragma warning restore CS1591
}
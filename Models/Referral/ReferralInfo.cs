namespace Coflnet.Sky.Api.Models.Referral
{
    public class ReferralInfo
    {
        public int ReferedCount { get; set; }
        public int ValidatedMinecraft { get; set; }
        public int PurchasedCoins { get; set; }
        public int PurchasedCoinAmount { get; set; }
        public string ReferredBy { get; set; }
        public string InviterMinecraftName { get; set; }
        public OldRefInfo oldInfo { get; set; }
    }
}
namespace Coflnet.Sky.Api.Models.Referral
{
    /// <summary>Contains referral statistics and inviter details.</summary>
    public class ReferralInfo
    {
        /// <summary>Gets or sets the refered count.</summary>
        public int ReferedCount { get; set; }
        /// <summary>Gets or sets the validated minecraft.</summary>
        public int ValidatedMinecraft { get; set; }
        /// <summary>Gets or sets the purchased coins.</summary>
        public int PurchasedCoins { get; set; }
        /// <summary>Gets or sets the purchased coin amount.</summary>
        public int PurchasedCoinAmount { get; set; }
        /// <summary>Gets or sets the referred by.</summary>
        public string ReferredBy { get; set; }
        /// <summary>Gets or sets the inviter minecraft name.</summary>
        public string InviterMinecraftName { get; set; }
        /// <summary>Gets or sets the old info.</summary>
        public OldRefInfo oldInfo { get; set; }
    }
}

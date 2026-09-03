namespace Coflnet.Sky.Api.Models.Referral
{
    /// <summary>
    /// Object containing referral code to give reward to
    /// </summary>
    public class ReferredBy
    {
        /// <summary>
        /// 
        /// </summary>
        public string RefCode { get; set; }
        /// <summary>Version of the referral offer shown to the user.</summary>
        public string ProgramVersion { get; set; }
        /// <summary>Locale in which the referral offer was shown.</summary>
        public string Locale { get; set; }
    }
}

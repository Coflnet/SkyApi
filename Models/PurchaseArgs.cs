#nullable enable
namespace Coflnet.Sky.Api.Models
{
    /// <summary>
    /// Arguments for purchasing a service
    /// </summary>
    public class PurchaseArgs
    {
        /// <summary>
        /// The service to pruchase
        /// </summary>
        public string slug;
        /// <summary>
        /// How many instances to purchase (longer time)
        /// </summary>
        public int count;
        /// <summary>
        /// Reference to prevent dupplicates
        /// </summary>
        public string reference;
        /// <summary>
        /// Whether the user expressly requested early performance for this
        /// order.
        /// </summary>
        public bool? immediatePerformanceRequested { get; set; }
        /// <summary>
        /// Whether the user acknowledged the consequence of complete
        /// performance.
        /// </summary>
        public bool? withdrawalConsequenceAcknowledged { get; set; }
        /// <summary>
        /// Hash-verified declaration version shown to the user.
        /// </summary>
        public string? declarationVersion { get; set; }
        /// <summary>
        /// Locale in which the declaration was shown.
        /// </summary>
        public string? legalLocale { get; set; }
        /// <summary>
        /// Idempotency identifier for this declaration and order.
        /// </summary>
        public string? declarationRequestId { get; set; }
    }
}

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
    }
}
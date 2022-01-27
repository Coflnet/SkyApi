using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models
{
    /// <summary>
    /// Lowest bin response
    /// </summary>
    [DataContract]
    public class BinResponse
    {
        /// <summary>
        /// The lowest bin price
        /// </summary>
        [DataMember(Name = "lowest")]
        public long Lowest;
        /// <summary>
        /// The lowest bin auction uuid
        /// </summary>
        [DataMember(Name = "uuid")]
        public string Uuid;
        /// <summary>
        /// The price of the second lowest bin
        /// </summary>
        [DataMember(Name = "secondLowest")]
        public long SecondLowest;

        /// <summary>
        /// Creates a new instance of <see cref="BinResponse"/>
        /// </summary>
        /// <param name="lowest"></param>
        /// <param name="uuid"></param>
        /// <param name="secondLowest"></param>
        public BinResponse(long lowest, string uuid, long secondLowest)
        {
            Lowest = lowest;
            Uuid = uuid;
            SecondLowest = secondLowest;
        }
    }
}
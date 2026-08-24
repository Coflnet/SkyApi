namespace Coflnet.Sky.Api.Models
{
    /// <summary>Represents a slayer elem.</summary>
    public class SlayerElem
    {
        /// <summary>Gets or sets the level.</summary>
        public SlayerLevel Level {get;set;}
        /// <summary>Represents a slayer level.</summary>
        public class SlayerLevel
        {
            /// <summary>Stores the current level.</summary>
            public int currentLevel;
        }
    }
}

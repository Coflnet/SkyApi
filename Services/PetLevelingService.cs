using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Coflnet.Sky.Api.Services
{
    /// <summary>Finds pets that sell for more once leveled.</summary>
    public class PetLevelingService
    {
        /// <summary>Configuration key overriding where the NEU pet constants are read from.</summary>
        public const string ConstantsPathSetting = "PET_CONSTANTS_PATH";
        private const string DefaultConstantsPath = "NEU-REPO/constants/pets.json";
        private const string PetPrefix = "PET_";
        private const string LowLevelSuffix = "_0";
        private const int TargetLevel = 100;

        private readonly ISniperClient sniperClient;
        private readonly ILogger<PetLevelingService> logger;
        private readonly Lazy<JObject> constants;

        /// <summary>Initializes a new instance of the <see cref="PetLevelingService"/> class.</summary>
        public PetLevelingService(ISniperClient sniperClient, IConfiguration config, ILogger<PetLevelingService> logger)
        {
            this.sniperClient = sniperClient;
            this.logger = logger;
            constants = new(() => LoadConstants(config[ConstantsPathSetting] ?? DefaultConstantsPath));
        }

        /// <summary>
        /// Gets the pets whose clean price at level 100 is above their low level clean price, most profitable first.
        /// </summary>
        public async Task<IEnumerable<PetLevelingFlip>> GetFlips()
        {
            var prices = await sniperClient.GetCleanPrices();
            return prices.Keys
                .Select(key => CreateFlip(key, prices))
                .Where(flip => flip?.Profit > 0)
                .OrderByDescending(flip => flip.Profit)
                .ToList();
        }

        private PetLevelingFlip CreateFlip(string lowLevelKey, Dictionary<string, long> prices)
        {
            if (!TryParseLowLevelKey(lowLevelKey, out var tag, out var tier))
                return null;
            if (!prices.TryGetValue($"{tag}_{tier}_{TargetLevel}", out var sellPrice))
                return null;
            var petType = tag.Substring(PetPrefix.Length);
            if (LevelsBeyondTarget(petType))
                return null;
            var buyPrice = prices[lowLevelKey];
            var profit = sellPrice - buyPrice;
            var expRequired = ExpToTargetLevel(petType, tier);
            return new PetLevelingFlip
            {
                Tag = tag,
                Tier = tier,
                ExpType = constants.Value["pet_types"]?[petType]?.Value<string>(),
                TargetLevel = TargetLevel,
                BuyPrice = buyPrice,
                SellPrice = sellPrice,
                Profit = profit,
                ExpRequired = expRequired,
                CoinsPerExp = expRequired > 0 ? (double)profit / expRequired : 0
            };
        }

        /// <summary>
        /// Splits a clean price key like PET_SHEEP_LEGENDARY_0 into the pet tag and its rarity.
        /// </summary>
        private static bool TryParseLowLevelKey(string key, out string tag, out Tier tier)
        {
            tag = null;
            tier = default;
            if (!key.StartsWith(PetPrefix) || !key.EndsWith(LowLevelSuffix))
                return false;
            var withoutLevel = key.Substring(0, key.Length - LowLevelSuffix.Length);
            var tierStart = withoutLevel.LastIndexOf('_');
            if (tierStart <= PetPrefix.Length)
                return false;
            tag = withoutLevel.Substring(0, tierStart);
            return Enum.TryParse(withoutLevel.Substring(tierStart + 1), out tier);
        }

        /// <summary>
        /// Pets leveling past the target (eg. dragons up to 200) have their higher levels in the same
        /// clean price bucket, so that price is not the one of a level 100 pet.
        /// </summary>
        private bool LevelsBeyondTarget(string petType)
        {
            var maxLevel = constants.Value["custom_pet_leveling"]?[petType]?["max_level"]?.Value<int>();
            return maxLevel > TargetLevel;
        }

        private long ExpToTargetLevel(string petType, Tier tier)
        {
            var custom = constants.Value["custom_pet_leveling"]?[petType];
            var offset = (custom?["rarity_offset"] ?? constants.Value["pet_rarity_offset"])?[tier.ToString()]?.Value<int>();
            var levels = constants.Value["pet_levels"]?.Values<long>().ToList();
            var levelUps = TargetLevel - 1;
            if (offset == null || levels == null || levels.Count < offset + levelUps)
                return 0;
            var exp = levels.Skip(offset.Value).Take(levelUps).Sum();
            return (long)(exp / (custom?["xp_multiplier"]?.Value<double>() ?? 1));
        }

        private JObject LoadConstants(string path)
        {
            if (File.Exists(path))
                return JObject.Parse(File.ReadAllText(path));
            logger.LogWarning("Pet constants not found at {path}, pet leveling flips lack exp data", path);
            return new JObject();
        }
    }
}

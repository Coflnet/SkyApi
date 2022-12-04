using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services
{
    public class AuctionConverter
    {
        Dictionary<string, Enchantment.EnchantmentType> EnchantLookup = new();

        public AuctionConverter()
        {
            foreach (var item in Enum.GetValues<Enchantment.EnchantmentType>())
            {
                EnchantLookup["!ench" + item.ToString().ToLower()] = item;
            }
        }

        /// <summary>
        /// Returns the header for the CSV file
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public string GetHeader(IEnumerable<string> keys)
        {
            return string.Join(',', ColumnKeys(keys.Select(k => k.StartsWith("!ench") ? k.Substring(5) : k))) + "\n";
        }

        /// <summary>
        /// filters the keys to only include the ones we care about
        /// </summary>
        /// <param name="datakeys"></param>
        /// <returns></returns>
        public IEnumerable<string> ColumnKeys(IEnumerable<string> datakeys)
        {
            return (new string[] { "uuid", "item_id", "sold_for", "count", }).Concat(datakeys.Where(k => IncludeColumn(k))).ToList();
        }

        private static bool IncludeColumn(string k)
        {
            return !new string[] { "118", "119", "120", "121" }.Contains(k) && !k.EndsWith(".uuid");
        }

        /// <summary>
        /// Converts a single auction to a csv line
        /// </summary>
        /// <param name="auction"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public string Transform(SaveAuction auction, IEnumerable<string> keys)
        {
            var itemTag = auction.Tag;
            if (Regex.IsMatch(itemTag, @"^PET_(?!ITEM)(?!SKIN)\w+"))
            {
                itemTag = "PET";
            }
            else if (itemTag.StartsWith("RUNE_"))
            {
                itemTag = "RUNE";
            }
            else if (itemTag.StartsWith("POTION_"))
            {
                itemTag = "POTION";
            }
            var flattened = auction.FlatenedNBT;
            var enchants = auction.Enchantments.ToDictionary(e => e.Type, e => e.Level);
            var values = keys.Select(item =>
            {
                if (flattened.TryGetValue(item, out string value))
                {
                    value = value.Replace("\n", "");
                    if (value.Contains(","))
                    {
                        value = "[\"" + value.Replace(",", "\",\"") + "\"]";
                    }
                    if (value.Contains('"'))
                        return "\"" + value.Replace("\"", "\"\"") + "\"";
                    return value;
                }
                if (item.StartsWith("!ench"))
                {
                    if (!EnchantLookup.TryGetValue(item.ToLower(), out var ench))
                        return string.Empty;
                    return enchants.GetValueOrDefault(ench).ToString();
                }
                return item switch
                {
                    "reforge" => auction.Reforge.ToString(),
                    "count" => auction.Count.ToString(),
                    "item_id" => itemTag,
                    "uuid" => auction.Uuid,
                    "sold_for" => auction.HighestBidAmount.ToString(),
                    _ => string.Empty
                };
            });
            var builder = new StringBuilder(1000);
            builder.AppendJoin(',', values);
            builder.AppendLine();
            return builder.ToString();
        }
    }
}
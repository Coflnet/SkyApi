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
        public static string[] ignoreColumns = new string[] {
            "!ench113", "!ench115", "!ench116", "!ench118", "!ench119", "!ench120", "!ench121", "!ench122", "!ench123",
            "builder's_wand_data", "frosty_the_snow_blaster_data", "frosty_the_snow_cannon_data", "greater_backpack_data", "jumbo_backpack_data", "large_backpack_data", "medium_backpack_data", "new_year_cake_bag_data"
         };

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
            return string.Join(',', ColumnKeys(keys)) + "\n";
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
            // ignore all inalid enchants (with number instead of enum)
            return !ignoreColumns.Contains(k) && !k.EndsWith(".uuid") && !(k.StartsWith("!ench") && int.TryParse(k.Replace("!ench",""), out _));
        }

        /// <summary>
        /// Converts a single auction to a csv line
        /// </summary>
        /// <param name="auction"></param>
        /// <param name="keys"></param>
        /// <returns></returns>
        public string Transform(SaveAuction auction, IEnumerable<string> keys)
        {
            var flattened = auction.FlatenedNBT;
            var enchants = auction.Enchantments.ToDictionary(e => e.Type, e => e.Level);
            var values = keys.Select(item =>
            {
                if (flattened.TryGetValue(item, out string value))
                {
                    return QuoteJson(value);
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
                    "item_id" => auction.Tag,
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

        private static string QuoteJson(string value)
        {
            value = value.Replace("\n", "");
            if (value.Contains('"'))
                value = "\"" + value.Replace("\"", "\"\"") + "\"";
            if (value.Contains(","))
            {
                value = "\"[" + value + "]\"";
            }

            return value;
        }

        public string MakeSample(int i, string itemId, string[] keys, Dictionary<string, List<string>> values)
        {
            var builder = new StringBuilder(1000);
            builder.AppendJoin(',', keys.Select(k => k switch
            {
                "item_id" => itemId,
                "sold_for" => "0",
                "count" => "0",
                _ => GetValue(values, k, i)
            }));
            builder.AppendLine();
            return builder.ToString();
        }

        private string GetValue(Dictionary<string, List<string>> values, string k, int i)
        {
            if (values.TryGetValue(k, out var list))
            {
                return QuoteJson(list[i % list.Count]);
            }
            return string.Empty;
        }
    }
}

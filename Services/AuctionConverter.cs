using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Mayor.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services;
public class AuctionConverter
{
    Dictionary<string, Enchantment.EnchantmentType> EnchantLookup = new();

    IElectionPeriodsApi mayorService;
    private ILogger<AuctionConverter> logger;
    private readonly Dictionary<int, string> YearToMayorName = new();
    public static HashSet<string> ignoreColumns = [
            "builder's_wand_data", "frosty_the_snow_blaster_data", "frosty_the_snow_cannon_data",
            "uniqueId", "uuid", "hideInfo", "hideRightClick", "noMove", "active", "abr", "name",
            "greater_backpack_data", "jumbo_backpack_data", "large_backpack_data", "medium_backpack_data", "new_year_cake_bag_data"
         ];

    public AuctionConverter(IElectionPeriodsApi mayorService, ILogger<AuctionConverter> logger)
    {
        this.logger = logger;
        foreach (var item in Enum.GetValues<Enchantment.EnchantmentType>())
        {
            EnchantLookup["!ench" + item.ToString().ToLower()] = item;
        }

        this.mayorService = mayorService;
        _ = InitMayors();
    }

    public string CurrentEvent(DateTime time)
    {
        var currentDay = GetCurrentDay(time);
        var eventList = new List<(int, int, Events)>()
        {
            (12*31-3,12*31, Events.NewYear),
            (03*31,03*31+3, Events.TravelingZoo),
            (9*31,9*31+3, Events.TravelingZoo),
            (7*31+28,8*31, Events.SpookyFestival),
            (11*31+23,11*31+26, Events.SeasonOfJerry)
        };
        var currentEvent = eventList.FirstOrDefault(e => currentDay >= e.Item1 && currentDay <= e.Item2).Item3;

        return currentEvent.ToString();
    }

    private int GetCurrentDay(DateTime Now)
    {
        return (int)(Constants.SkyblockYear(Now) * 31 * 12) % (31 * 12);
    }


    public async Task InitMayors()
    {
        if (YearToMayorName.Count > 0)
            return;
        List<Mayor.Client.Model.ModelElectionPeriod> mayors = null;
        try
        {
            mayors = await mayorService.ElectionPeriodRangeGetAsync(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load mayors");
        }
        if (mayors == null)
            return;
        foreach (var mayor in mayors)
        {
            if (mayor == null || mayor.Winner == null)
                continue;
            YearToMayorName[mayor.Year] = mayor.Winner.Name;
        }
        logger.LogInformation("Loaded " + mayors.Count + " mayors");
        logger.LogInformation("Current mayor is " + GetMayor(DateTime.UtcNow));
    }


    public string GetMayor(DateTime time)
    {
        if (YearToMayorName.TryGetValue(ElectionYear(time), out var name))
            return name;
        return "Unknown";
    }
    private static int ElectionYear(DateTime time)
    {
        return (int)(Constants.SkyblockYear(time) - 0.2365635);
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
        return (new string[] { "auctionuuid", "item_id", "sold_for", "count", "ACTIVE_mayor", "ACTIVE_event" }).Concat(datakeys.Where(k => IncludeColumn(k))).ToList();
    }

    private static bool IncludeColumn(string k)
    {
        // ignore all inalid enchants (with number instead of enum)
        return !ignoreColumns.Contains(k) && !k.EndsWith("uid") && !(k.StartsWith("!ench") && int.TryParse(k.Replace("!ench", "").Trim('-'), out _));
    }

    /// <summary>
    /// Converts a single auction to a csv line
    /// </summary>
    /// <param name="auction"></param>
    /// <param name="keys"></param>
    /// <returns></returns>
    public string Transform(SaveAuction auction, IEnumerable<string> keys)
    {
        IEnumerable<string> values = GetValues(auction, keys);
        var builder = new StringBuilder(1000);
        builder.AppendJoin(';', values);
        builder.AppendLine();
        return builder.ToString();
    }

    private IEnumerable<string> GetValues(SaveAuction auction, IEnumerable<string> keys)
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
                "auctionuuid" => auction.Uuid,
                "tier" => auction.Tier.ToString(),
                "sold_for" => auction.HighestBidAmount.ToString(),
                "ACTIVE_mayor" => GetMayor(auction.End),
                "ACTIVE_event" => CurrentEvent(auction.End),
                "upgrade_level" => auction.FlatenedNBT.GetValueOrDefault("dungeon_item_level"),
                _ => string.Empty
            };
        });
        return values;
    }

    private static string QuoteJson(string value)
    {
        value = value.Replace("\n", "");
        if (value.Contains('"'))
            value = "\"" + value.Replace("\"", "\"\"") + "\"";

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

    public float[] MapToFloats(List<string> lines, List<string> keys, Dictionary<string, List<string>> itemModifiers)
    {
        List<string> columns = Createmap(keys, itemModifiers);
        var lookup = lines.Zip(keys);
        var values = new Dictionary<string, float>();
        foreach (var item in lookup)
        {
            if (int.TryParse(item.First, out var val) && val <= 20)
            {
                values[$"{item.Second}:{val}"] = 1;
                continue;
            }
            if (float.TryParse(item.First, out var f))
            {
                var max = itemModifiers.GetValueOrDefault(item.Second, new List<string>()).Max(v => float.TryParse(v, out var val) ? val : 0);
                values[item.Second] = f / max;
                continue;
            }
            if (itemModifiers.TryGetValue(item.Second, out var list))
            {
                var allValues = list.SelectMany(v => v.Split(',', ' ')).Distinct().Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                for (int i = 0; i < allValues.Count; i++)
                {
                    if (allValues[i] == item.First)
                    {
                        values[$"{item.Second}:{allValues[i]}"] = 1;
                        break;
                    }
                }
            }
        }
        Console.WriteLine("Columns: " + string.Join(',', columns));
        return columns.Select(c => values.GetValueOrDefault(c, 0)).ToArray();
    }

    public List<string> Createmap(List<string> keys, Dictionary<string, List<string>> itemModifiers)
    {
        itemModifiers["ACTIVE_mayor"] = YearToMayorName.Values.ToHashSet().OrderByDescending(v => v).ToList();
        itemModifiers["ACTIVE_event"] = Enum.GetNames<Events>().ToList();
        itemModifiers["sold_for"] = ["1.0"];
        var columns = keys.SelectMany(k =>
        {
            if (!itemModifiers.TryGetValue(k, out var list))
            {
                logger.LogError("No item modifiers for " + k);
                return new List<string>();
            }
            if (list.All(v => float.TryParse(v, out _)))
            {
                if (list.All(v => int.TryParse(v, out var val) && val <= 20))
                {
                    return list.Select(v => $"{k}:{v}");
                }
                return new List<string>() { k };
            }
            Console.WriteLine("Mapping " + k);
            var allValues = list.SelectMany(v => v.Split(',', ' ')).Distinct().Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            return allValues.Select(v => $"{k}:{v}");
        }).ToList();
        return columns;
    }

    private string GetValue(Dictionary<string, List<string>> values, string k, int i)
    {
        if (values.TryGetValue(k, out var list))
        {
            return QuoteJson(list[i % list.Count]);
        }
        return string.Empty;
    }

    public IEnumerable<SaveAuction> FromitemRepresent(ItemRepresent[] items)
    {
        return items.Select(i =>
        {
            var auction = new SaveAuction()
            {
                Count = i.Count,
                Tag = i.Tag,
                ItemName = i.ItemName,

            };
            auction.Enchantments = i.Enchantments?.Select(e => new Enchantment()
            {
                Type = Enum.TryParse<Enchantment.EnchantmentType>(e.Key, out var type) ? type : Enchantment.EnchantmentType.unknown,
                Level = e.Value
            }).ToList() ?? new();
            auction.Tier = Enum.TryParse<Tier>(i.ExtraAttributes.FirstOrDefault(a => a.Key == "tier").Value?.ToString() ?? "", out var tier) ? tier : Tier.UNKNOWN;
            auction.Reforge = Enum.TryParse<ItemReferences.Reforge>(i.ExtraAttributes.FirstOrDefault(a => a.Key == "modifier").Value?.ToString() ?? "", out var reforge) ? reforge : ItemReferences.Reforge.Unknown;
            auction.SetFlattenedNbt(NBT.FlattenNbtData(NBT.FromDeserializedJson(i.ExtraAttributes)));
            return auction;
        });
    }

    internal string MapAsFrame(SaveAuction item, List<string> keys, Dictionary<string, List<string>> itemModifiers)
    {
        IEnumerable<string> values = GetValues(item, keys);
        var mapped = MapToFloats(values.ToList(), keys, itemModifiers);
        var builder = new StringBuilder(1000);
        builder.AppendJoin(';', mapped);
        builder.AppendLine();
        return builder.ToString();
    }

    public enum Events
    {
        None,
        TravelingZoo,
        SpookyFestival,
        DarkAuction,
        NewYear,
        SeasonOfJerry
    }
}


public class ItemRepresent : Item
{
}
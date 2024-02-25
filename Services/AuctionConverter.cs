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
    public static string[] ignoreColumns = new string[] {
            "builder's_wand_data", "frosty_the_snow_blaster_data", "frosty_the_snow_cannon_data", "greater_backpack_data", "jumbo_backpack_data", "large_backpack_data", "medium_backpack_data", "new_year_cake_bag_data"
         };

    public AuctionConverter(IElectionPeriodsApi mayorService)
    {
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
        if(YearToMayorName.Count > 0)
            return;
        List<Mayor.Client.Model.ModelElectionPeriod> mayors = null;
        try
        {
            mayors = await mayorService.ElectionPeriodRangeGetAsync(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to load mayors");
        }
        foreach (var mayor in mayors)
        {
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
        return (new string[] { "uuid", "item_id", "sold_for", "count", "ACTIVE_mayor", "ACTIVE_event" }).Concat(datakeys.Where(k => IncludeColumn(k))).ToList();
    }

    private static bool IncludeColumn(string k)
    {
        // ignore all inalid enchants (with number instead of enum)
        return !ignoreColumns.Contains(k) && !k.EndsWith(".uuid") && !(k.StartsWith("!ench") && int.TryParse(k.Replace("!ench", ""), out _));
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
                "ACTIVE_mayor" => GetMayor(auction.End),
                "ACTIVE_event" => CurrentEvent(auction.End),
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

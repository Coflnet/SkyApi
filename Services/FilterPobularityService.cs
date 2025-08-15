using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api;

public class FilterPobularityService
{
    private IDistributedCache _cache;
    private ILogger<FilterPobularityService> _logger;

    public FilterPobularityService(IDistributedCache cache)
    {
        _cache = cache;

        _ = Task.Run(async () =>
        {
            try
            {
                var cacheData = await _cache.GetAsync("filterPopularity");
                if (cacheData != null)
                {
                    _itemPopularity = MessagePack.MessagePackSerializer.Deserialize<ConcurrentDictionary<string, ConcurrentDictionary<string, int>>>(cacheData);
                }
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(50));
                    if (_itemPopularity.Count < 10)
                        continue;
                    var serialized = MessagePack.MessagePackSerializer.Serialize(_itemPopularity);
                    await _cache.SetAsync("filterPopularity", serialized, new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(10)
                    });
                    if (Random.Shared.Next(0, 100) < 5)
                    {
                        ReducePopularity();
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while loading filter popularity from cache");
            }

            void ReducePopularity()
            {
                foreach (var item in _itemPopularity)
                {
                    foreach (var filter in item.Value.ToList())
                    {
                        item.Value[filter.Key] = (int)(filter.Value * 0.95);
                        if (item.Value[filter.Key] == 0)
                            _itemPopularity[item.Key].TryRemove(filter.Key, out _);
                    }
                }
            }
        });
    }
    private ConcurrentDictionary<string, ConcurrentDictionary<string, int>> _itemPopularity = [];

    public void AddFilterUse(string itemTag, string filterName)
    {
        if (string.IsNullOrEmpty(itemTag))
            return;
        if (!_itemPopularity.ContainsKey(itemTag))
        {
            _itemPopularity[itemTag] = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        _itemPopularity[itemTag].AddOrUpdate(filterName, 1, (key, oldValue) => oldValue + 1);
    }

    public int GetFilterUseCount(string itemTag, string filterName)
    {
        if (string.IsNullOrEmpty(itemTag))
            return 0;

        if (_itemPopularity.TryGetValue(itemTag, out var filters))
        {
            if (filters.TryGetValue(filterName, out var count))
            {
                return count;
            }
        }
        return 0;
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using fNbt.Tags;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services;

public interface IItemSkinHandler
{
    void StoreIfNeeded(string tag, NbtCompound combound);
}
public class ItemSkinHandler : BackgroundService, IItemSkinHandler
{
    private Sky.Items.Client.Api.IItemsApi itemsApi;
    private ConcurrentDictionary<string, bool> skinTags = new();
    private ActivitySource activitySource;

    public ItemSkinHandler(IItemsApi itemsApi)
    {
        this.itemsApi = itemsApi;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = activitySource?.StartActivity("UpdateSkins");
                var response = await itemsApi.ItemsNoiconGetWithHttpInfoAsync();
                var items = JsonConvert.DeserializeObject<List<Sky.Items.Client.Model.Item>>(response.RawContent.Replace("SUPREME", "DIVINE"));
                foreach (var item in items)
                {
                    skinTags.TryAdd(item.Tag, false);
                }
                Console.WriteLine($"found {skinTags.Count} items without skins");
                items.Clear();
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "updating skins");
            }
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }


    public void StoreIfNeeded(string tag, NbtCompound compound)
    {
        if (tag == null)
            return;
        if (tag == "ATTRIBUTE_SHARD")
        {
            var name = NBT.GetName(compound).Substring(2).Replace(" Shard", "");
            // this is a new attribute shard, we need to set the tag
            if (Constants.ShardNames.TryGetValue(name, out var shardTag))
                tag = "SHARD_" + shardTag.ToUpper();
            else
            {
                Console.WriteLine($"unknown shard name {name} for {tag}");
                var closestDistance = Constants.ShardNames
                    .Select(s => (s, Distance: Fastenshtein.Levenshtein.Distance(name, s.Key)))
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault();
                if (closestDistance.Distance < 3)
                {
                    tag = "SHARD_" + closestDistance.s.Value.ToUpper();
                    Console.WriteLine($"using closest shard name {tag} for {name}");
                    Constants.ShardNames[name] = closestDistance.s.Value;
                }
                else
                {
                    Console.WriteLine($"unknown shard name {name} for {tag}, not using it");
                    return;
                }
            }
        }
        if (!skinTags.TryGetValue(tag, out var saved) || saved)
            return;
        skinTags[tag] = true;
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine($"loading skin for {tag}");
                var skullUrl = NBT.SkullUrl(compound);
                if (skullUrl == null)
                {
                    Console.WriteLine($"no skin found for {tag} {compound.ToString()}");
                    //skinNames[tag] = false;
                    return;
                }
                await itemsApi.ItemItemTagTexturePostAsync(tag, skullUrl);
                Console.WriteLine($"updated skin for {tag} to {skullUrl}");
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "loading skin for " + tag);
            }
        });
    }
}

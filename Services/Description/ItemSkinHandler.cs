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
            var name = NBT.GetName(compound);
            // this is a new attribute shard, we need to set the tag
            if (!ModDescriptionService.TryGetShardTagFromName(name, out tag))
                return;
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
                    if (compound.TryGet<NbtString>("id", out var idTag)
                        && idTag.Value != "minecraft:player_head" && idTag.Value != "minecraft:skull"
                        && idTag.Value.StartsWith("minecraft:"))
                    {
                        var itemName = idTag.Value.Substring(10);
                        if (itemName.EndsWith("_head")) // special case for mob heads
                            skullUrl = "https://mc-heads.net/head/MHF_" + itemName.Substring(0, itemName.Length - 5);
                        else
                            skullUrl = "https://sky.coflnet.com/static/icon/" + itemName.ToUpper();
                        Console.WriteLine($"found item url {skullUrl} for {tag} from id tag");
                    }
                    else
                    {
                        Console.WriteLine($"no skin found for {tag} {compound.ToString()}");
                        //skinNames[tag] = false;
                        return;
                    }
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

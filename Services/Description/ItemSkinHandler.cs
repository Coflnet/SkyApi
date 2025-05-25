using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using fNbt.Tags;
using Microsoft.Extensions.Hosting;

namespace Coflnet.Sky.Api.Services;

public interface IItemSkinHandler
{
    void StoreIfNeeded(string tag, NbtCompound combound);
}
public class ItemSkinHandler : BackgroundService, IItemSkinHandler
{
    private Sky.Items.Client.Api.IItemsApi itemsApi;
    private ConcurrentDictionary<string, bool> skinNames = new();
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
                var noIconResponse = await itemsApi.ItemsNoiconGetAsync(stoppingToken);
                if (!noIconResponse.TryOk(out var items))
                {
                    Console.WriteLine("Failed to get items without skins");
                    return;
                }
                foreach (var item in items)
                {
                    skinNames.TryAdd(item.Tag, false);
                }
                Console.WriteLine($"found {skinNames.Count} items without skins");
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
        if (!skinNames.TryGetValue(tag, out var saved) || saved)
            return;
        skinNames[tag] = true;
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

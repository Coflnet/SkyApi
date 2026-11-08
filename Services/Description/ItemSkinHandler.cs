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

/// <summary>Defines item skin storage operations.</summary>
public interface IItemSkinHandler
{
    /// <summary>Stores an item skin when it has not been saved yet.</summary>
    void StoreIfNeeded(string tag, NbtCompound combound);
}
/// <summary>Caches and stores item skins.</summary>
public class ItemSkinHandler : BackgroundService, IItemSkinHandler
{
    private static readonly ActivitySource ActivitySource = new(nameof(ItemSkinHandler));
    private readonly Sky.Items.Client.Api.IItemsApi itemsApi;
    private readonly ConcurrentDictionary<string, bool> skinTags = new();

    /// <summary>Initializes a new instance of the <see cref="ItemSkinHandler"/> class.</summary>
    public ItemSkinHandler(IItemsApi itemsApi)
    {
        this.itemsApi = itemsApi;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = ActivitySource.StartActivity("UpdateSkins");
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


    /// <inheritdoc/>
    public void StoreIfNeeded(string tag, NbtCompound compound)
    {
        if (tag == null)
            return;
        // Minecraft-formatted (color coded) display name of the item, e.g. from a bazaar order
        // screen ("§6§lSELL §9Enchanted Redstone"); SkyItems sanitizes/validates it before storing.
        var displayName = NBT.GetName(compound);
        if (tag == "ATTRIBUTE_SHARD")
        {
            // this is a new attribute shard, we need to set the tag
            if (!ModDescriptionService.TryGetShardTagFromName(displayName, out tag))
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
                        if (idTag?.Value == "minecraft:skull")
                            skinTags[tag] = false; // retry once the real skin texture is resolved
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            Console.WriteLine($"no skin found for {tag} {compound}");
                            return;
                        }
                        // no icon (yet), but we do have a usable display name - still worth sending
                    }
                }
                await itemsApi.ItemItemTagTexturePostAsync(tag, skullUrl, displayName);
                Console.WriteLine($"updated skin/name for {tag} to {skullUrl}/{displayName}");
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "loading skin for " + tag);
            }
        });
    }
}

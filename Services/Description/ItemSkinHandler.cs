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
    private readonly ConcurrentDictionary<string, SkinState> skinTags = new();

    private enum SkinState { Eligible, NameAttempted, TextureAttempted }

    /// <summary>Initializes a new instance of the <see cref="ItemSkinHandler"/> class.</summary>
    public ItemSkinHandler(IItemsApi itemsApi)
    {
        this.itemsApi = itemsApi;
    }

    /// <summary>How often the list of items without icon is reloaded; also the minimum gap between two pushes for the same tag.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync();
            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Reloads the items without icon. A failed refresh keeps the previous list.
    /// </summary>
    internal async Task RefreshAsync()
    {
        try
        {
            using var activity = ActivitySource.StartActivity("UpdateSkins");
            var response = await itemsApi.ItemsNoiconGetWithHttpInfoAsync();
            var items = JsonConvert.DeserializeObject<List<Sky.Items.Client.Model.Item>>(response.RawContent.Replace("SUPREME", "DIVINE"));
            ApplyNoIconList(items.Select(i => i.Tag));
            Console.WriteLine($"found {skinTags.Count} items without skins");
        }
        catch (Exception e)
        {
            dev.Logger.Instance.Error(e, "updating skins");
        }
    }

    /// <summary>
    /// Makes every tag of the list eligible for one push again and forgets tags that are no longer listed.
    /// </summary>
    internal void ApplyNoIconList(IEnumerable<string> tags)
    {
        var current = tags.Where(t => t != null).ToHashSet();
        foreach (var known in skinTags.Keys)
        {
            if (!current.Contains(known))
                skinTags.TryRemove(known, out _);
        }
        foreach (var tag in current)
        {
            skinTags.AddOrUpdate(tag, SkinState.Eligible, (_, _) => SkinState.Eligible);
        }
    }


    /// <inheritdoc/>
    public void StoreIfNeeded(string tag, NbtCompound compound)
    {
        _ = TryStore(tag, compound);
    }

    /// <summary>
    /// Claims the tag and starts the fire-and-forget push. Returns null when nothing was claimed, otherwise the push task.
    /// </summary>
    internal Task TryStore(string tag, NbtCompound compound)
    {
        if (tag == null)
            return null;
        // Minecraft-formatted (color coded) display name of the item, e.g. from a bazaar order
        // screen ("§6§lSELL §9Enchanted Redstone"); SkyItems sanitizes/validates it before storing.
        var displayName = NBT.GetName(compound);
        var nameIsExactShard = false;
        if (tag == "ATTRIBUTE_SHARD")
        {
            // this is a new attribute shard, we need to set the tag
            if (displayName == null || !ModDescriptionService.TryGetExactShardTagFromName(displayName, out tag))
                return null;
            nameIsExactShard = true;
        }
        if (!skinTags.TryGetValue(tag, out var state) || state == SkinState.TextureAttempted)
            return null;
        var skullUrl = ResolveTextureUrl(compound);
        if (skullUrl == null)
        {
            // name only: once per interval, a later sighting with a texture can still follow
            if (state == SkinState.NameAttempted || !skinTags.TryUpdate(tag, SkinState.NameAttempted, SkinState.Eligible))
                return null;
            if (string.IsNullOrWhiteSpace(displayName))
                return Task.CompletedTask;
        }
        else if (!skinTags.TryUpdate(tag, SkinState.TextureAttempted, SkinState.Eligible)
            && !skinTags.TryUpdate(tag, SkinState.TextureAttempted, SkinState.NameAttempted))
            return null;
        if (!nameIsExactShard && displayName != null && tag.StartsWith("SHARD_", StringComparison.Ordinal)
            && !ModDescriptionService.TryGetExactShardTagFromName(displayName, out _)
            && ModDescriptionService.TryGetShardTagFromName(displayName, out var fuzzyTag) && fuzzyTag == tag)
        {
            return Task.CompletedTask; // the tag was guessed from a similar shard name, its texture could belong to another shard
        }
        return Task.Run(async () =>
        {
            try
            {
                await itemsApi.ItemItemTagTexturePostAsync(tag, skullUrl, displayName);
                Console.WriteLine($"updated skin/name for {tag} to {skullUrl}/{displayName}");
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "loading skin for " + tag);
            }
        });
    }

    private static string ResolveTextureUrl(NbtCompound compound)
    {
        try
        {
            var skullUrl = NBT.SkullUrl(compound);
            if (skullUrl != null)
                return skullUrl;
            if (compound.TryGet<NbtString>("id", out var idTag)
                && idTag.Value != "minecraft:player_head" && idTag.Value != "minecraft:skull"
                && idTag.Value.StartsWith("minecraft:"))
            {
                var itemName = idTag.Value.Substring(10);
                if (itemName.EndsWith("_head")) // special case for mob heads
                    return "https://mc-heads.net/head/MHF_" + itemName.Substring(0, itemName.Length - 5);
                return "https://sky.coflnet.com/static/icon/" + itemName.ToUpper();
            }
        }
        catch (Exception e)
        {
            dev.Logger.Instance.Error(e, "resolving skin texture");
        }
        return null;
    }
}

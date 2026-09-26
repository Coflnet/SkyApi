using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Items.Client.Api;
using fNbt.Tags;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class ItemSkinHandlerTests
{
    // StoreIfNeeded only acts on tags the background refresh loop already flagged as "needs an
    // icon" (skinTags[tag] == false); mirror that here via reflection since there is no public hook.
    private static void MarkNeedsIcon(ItemSkinHandler handler, string tag)
    {
        var field = typeof(ItemSkinHandler).GetField("skinTags", BindingFlags.Instance | BindingFlags.NonPublic);
        var skinTags = (ConcurrentDictionary<string, bool>)field.GetValue(handler);
        skinTags[tag] = false;
    }

    private static NbtCompound WithDisplayName(string rawName, params NbtTag[] extraRootTags)
    {
        var display = new NbtCompound("display") { new NbtString("Name", rawName) };
        var tagCompound = new NbtCompound("tag") { display };
        var root = new NbtCompound(string.Empty) { tagCompound };
        foreach (var extra in extraRootTags)
            root.Add(extra);
        return root;
    }

    // Regression: the mod sends item NBT (incl. bazaar order screens) to StoreIfNeeded, which used
    // to only ever forward the resolved icon url and dropped the item's display name entirely -
    // items like FACTION_RABBIT_MOCKTAIL therefore never got a real name in SkyItems.
    [Test]
    public async Task StoreIfNeededSendsDisplayNameAlongsideResolvedIcon()
    {
        var (itemsApi, called) = SetupCapturingApi();
        var handler = new ItemSkinHandler(itemsApi.Object);
        MarkNeedsIcon(handler, "FACTION_RABBIT_MOCKTAIL");

        var compound = WithDisplayName("§aFaction Rabbit Mocktail", new NbtString("id", "minecraft:stone"));
        handler.StoreIfNeeded("FACTION_RABBIT_MOCKTAIL", compound);

        var call = await AwaitCall(called);
        Assert.Multiple(() =>
        {
            Assert.That(call.tag, Is.EqualTo("FACTION_RABBIT_MOCKTAIL"));
            Assert.That(call.texture, Is.EqualTo("https://sky.coflnet.com/static/icon/STONE"));
            Assert.That(call.name, Is.EqualTo("§aFaction Rabbit Mocktail"));
        });
    }

    // Regression: previously, an item with no resolvable icon at all (no skull texture, no usable
    // "id" tag) returned without sending anything - even when the NBT did carry a real display
    // name. Now the name alone is still forwarded so SkyItems can fill in a missing/tag-like name.
    [Test]
    public async Task StoreIfNeededStillSendsNameWhenNoIconCanBeResolved()
    {
        var (itemsApi, called) = SetupCapturingApi();
        var handler = new ItemSkinHandler(itemsApi.Object);
        MarkNeedsIcon(handler, "FACTION_RABBIT_CHASM");

        // deliberately no "id" tag at all, and no SkullOwner/profile texture data
        var compound = WithDisplayName("§aFaction Rabbit Chasm");
        handler.StoreIfNeeded("FACTION_RABBIT_CHASM", compound);

        var call = await AwaitCall(called);
        Assert.Multiple(() =>
        {
            Assert.That(call.tag, Is.EqualTo("FACTION_RABBIT_CHASM"));
            Assert.That(call.texture, Is.Null);
            Assert.That(call.name, Is.EqualTo("§aFaction Rabbit Chasm"));
        });
    }

    private static (Mock<IItemsApi> api, TaskCompletionSource<(string tag, string texture, string name)> called) SetupCapturingApi()
    {
        var tcs = new TaskCompletionSource<(string, string, string)>();
        var itemsApi = new Mock<IItemsApi>();
        itemsApi.Setup(a => a.ItemItemTagTexturePostAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, CancellationToken>((tag, texture, name, _, _) => tcs.TrySetResult((tag, texture, name)))
            .Returns(Task.CompletedTask);
        return (itemsApi, tcs);
    }

    private static async Task<(string tag, string texture, string name)> AwaitCall(TaskCompletionSource<(string, string, string)> tcs)
    {
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.That(completed, Is.EqualTo(tcs.Task), "ItemItemTagTexturePostAsync was never called");
        return await tcs.Task;
    }
}

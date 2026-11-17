using System;
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
        handler.ApplyNoIconList(["FACTION_RABBIT_MOCKTAIL"]);

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
        handler.ApplyNoIconList(["FACTION_RABBIT_CHASM"]);

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

    private static NbtCompound WithTexture(string rawName) => WithDisplayName(rawName, new NbtString("id", "minecraft:stone"));

    private static void VerifyPushes(Mock<IItemsApi> api, string tag, int times) =>
        api.Verify(a => a.ItemItemTagTexturePostAsync(
            tag, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(times));

    private static void VerifyTexturePushes(Mock<IItemsApi> api, string tag, int times) =>
        api.Verify(a => a.ItemItemTagTexturePostAsync(
            tag, It.IsNotNull<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(times));

    // Regression: a tag was flagged done after its first push for the whole process lifetime, even when
    // the push failed or carried no icon, so a new item never got its icon after a bad first sighting.
    [Test]
    public async Task TagIsPushedAgainAfterRefreshWhenFirstPushFailed()
    {
        var itemsApi = new Mock<IItemsApi>();
        itemsApi.SetupSequence(a => a.ItemItemTagTexturePostAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SkyItems unavailable"))
            .Returns(Task.CompletedTask);
        var handler = new ItemSkinHandler(itemsApi.Object);
        var compound = WithTexture("§aFlora");

        handler.ApplyNoIconList(["SHARD_FLORA"]);
        await handler.TryStore("SHARD_FLORA", compound);
        Assert.That(handler.TryStore("SHARD_FLORA", compound), Is.Null); // bounded: no second attempt before the next refresh
        handler.ApplyNoIconList(["SHARD_FLORA"]); // next refresh still lists it
        await handler.TryStore("SHARD_FLORA", compound);

        VerifyPushes(itemsApi, "SHARD_FLORA", 2);
    }

    [Test]
    public void RefreshDropsTagsThatAreNoLongerListed()
    {
        var handler = new ItemSkinHandler(new Mock<IItemsApi>().Object);
        handler.ApplyNoIconList(["A", "B"]);
        handler.ApplyNoIconList(["B"]);

        Assert.That(handler.TryStore("A", WithTexture("§aA")), Is.Null);
        Assert.That(handler.TryStore("B", WithTexture("§aB")), Is.Not.Null);
    }

    [Test]
    public async Task FailedRefreshKeepsPreviousList()
    {
        var itemsApi = new Mock<IItemsApi>();
        itemsApi.Setup(a => a.ItemsNoiconGetWithHttpInfoAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SkyItems unavailable"));
        var handler = new ItemSkinHandler(itemsApi.Object);
        handler.ApplyNoIconList(["SHARD_FLORA"]);

        await handler.RefreshAsync();

        Assert.That(handler.TryStore("SHARD_FLORA", WithTexture("§aFlora")), Is.Not.Null);
    }

    [Test]
    public async Task ConcurrentUploadsOfOneTagPushOnlyOnce()
    {
        var itemsApi = new Mock<IItemsApi>();
        var handler = new ItemSkinHandler(itemsApi.Object);
        handler.ApplyNoIconList(["SHARD_FLORA"]);
        var compound = WithTexture("§aFlora");
        var claims = new System.Collections.Concurrent.ConcurrentBag<Task>();

        Parallel.For(0, 64, _ =>
        {
            var task = handler.TryStore("SHARD_FLORA", compound);
            if (task != null)
                claims.Add(task);
        });
        await Task.WhenAll(claims);

        Assert.That(claims, Has.Count.EqualTo(1));
        VerifyPushes(itemsApi, "SHARD_FLORA", 1);
    }

    [Test]
    public async Task TextureSightingFollowsAnEarlierNameOnlySighting()
    {
        var itemsApi = new Mock<IItemsApi>();
        var handler = new ItemSkinHandler(itemsApi.Object);
        handler.ApplyNoIconList(["X"]);
        var nameOnly = WithDisplayName("§aX");

        await handler.TryStore("X", nameOnly);
        Assert.That(handler.TryStore("X", nameOnly), Is.Null); // second name-only sighting does nothing
        await handler.TryStore("X", WithTexture("§aX"));
        Assert.That(handler.TryStore("X", WithTexture("§aX")), Is.Null); // second texture sighting does nothing
        Assert.That(handler.TryStore("X", nameOnly), Is.Null);

        VerifyPushes(itemsApi, "X", 2);
        VerifyTexturePushes(itemsApi, "X", 1);
    }

    // Regression: a shard name within Levenshtein distance 2 of a known shard resolved to that other
    // shard's tag, so its texture could be stored for the wrong item.
    [Test]
    public async Task FuzzyShardNameMatchDoesNotPushAnIcon()
    {
        var itemsApi = new Mock<IItemsApi>();
        var handler = new ItemSkinHandler(itemsApi.Object);
        handler.ApplyNoIconList(["SHARD_ANT"]);
        var compound = WithTexture("§aAnts Shard");
        Assert.That(ModDescriptionService.TryGetShardTagFromName("§aAnts Shard", out var fuzzy), Is.True);
        Assert.That(fuzzy, Is.EqualTo("SHARD_ANT"));

        Assert.That(handler.TryStore("ATTRIBUTE_SHARD", compound), Is.Null);
        var first = handler.TryStore("SHARD_ANT", compound); // tag as parsed from the same fuzzy name
        Assert.That(first, Is.Not.Null.And.Property("IsCompleted").True); // claimed, nothing sent
        Assert.That(handler.TryStore("SHARD_ANT", compound), Is.Null); // no repeated fuzzy work in this interval
        VerifyPushes(itemsApi, "SHARD_ANT", 0);

        // retried after the next refresh
        handler.ApplyNoIconList(["SHARD_ANT"]);
        await handler.TryStore("SHARD_ANT", WithTexture("§aAnt Shard"));
        VerifyPushes(itemsApi, "SHARD_ANT", 1);
    }

    [Test]
    public async Task NullDisplayNameDoesNotThrowAndStillPushesRealShardTag()
    {
        var itemsApi = new Mock<IItemsApi>();
        var handler = new ItemSkinHandler(itemsApi.Object);
        handler.ApplyNoIconList(["SHARD_FLORA"]);
        var noName = new NbtCompound(string.Empty) { new NbtString("id", "minecraft:stone") };

        Assert.That(handler.TryStore("ATTRIBUTE_SHARD", noName), Is.Null);
        await handler.TryStore("SHARD_FLORA", noName);

        VerifyPushes(itemsApi, "SHARD_FLORA", 1);
    }

    [Test]
    public async Task ExactBazaarShardNamePushesAnIcon()
    {
        var itemsApi = new Mock<IItemsApi>();
        var handler = new ItemSkinHandler(itemsApi.Object);
        try
        {
            ModDescriptionService.UpdateBazaarShardTags(["SHARD_FUTURE_CREATURE"]);
            handler.ApplyNoIconList(["SHARD_FUTURE_CREATURE"]);

            await handler.TryStore("ATTRIBUTE_SHARD", WithTexture("§aFuture Creature Shard"));

            VerifyPushes(itemsApi, "SHARD_FUTURE_CREATURE", 1);
        }
        finally
        {
            ModDescriptionService.UpdateBazaarShardTags([]);
        }
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

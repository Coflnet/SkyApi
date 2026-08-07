using Coflnet.Sky.Api.Services;
using NUnit.Framework;

namespace SkyApi.Services.Description;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class ShardNameMappingTests
{
    [TestCase("Ant", "SHARD_ANT")]
    [TestCase("Wiki Tiki", "SHARD_WIKI_TIKI")]
    [TestCase("Beetle", "SHARD_CROPEETLE")]
    [TestCase("Earthworm", "SHARD_TERMITE")]
    [TestCase("Field Mouse", "SHARD_PEST")]
    [TestCase("Stridersurfer", "SHARD_STRIDER_SURFER")]
    [TestCase("Wither Spectre", "SHARD_WITHER_SPECTER")]
    [TestCase("Zealot Bruiser", "SHARD_BRUISER")]
    public void TryGetShardTagFromName_MapsCurrentShardNames(string name, string expected)
    {
        Assert.That(ModDescriptionService.TryGetShardTagFromName($"{name} Shard", out var tag), Is.True);
        Assert.That(tag, Is.EqualTo(expected));
    }

    [Test]
    public void TryGetShardTagFromName_MapsRegularNamesFromBazaarProducts()
    {
        try
        {
            ModDescriptionService.UpdateBazaarShardTags(["SHARD_FUTURE_CREATURE", "SHARD_BURNINGSOUL", "NOT_A_SHARD"]);

            Assert.That(ModDescriptionService.TryGetShardTagFromName("Future Creature Shard", out var tag), Is.True);
            Assert.That(tag, Is.EqualTo("SHARD_FUTURE_CREATURE"));
            Assert.That(ModDescriptionService.TryGetShardTagFromName("Burningsoul Shard", out _), Is.False);
        }
        finally
        {
            ModDescriptionService.UpdateBazaarShardTags([]);
        }
    }
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

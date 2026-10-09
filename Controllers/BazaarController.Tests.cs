using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

/// <summary>Contains bazaar controller tests.</summary>
[TestFixture]
public class BazaarControllerTests
{
    /// <summary>The snapshot defaults to "now" rounded to 20 seconds, a longer cache would serve a stale order book.</summary>
    [Test]
    public void SnapshotIsCachedNoLongerThanItsTwentySecondGranularity()
    {
        var cache = typeof(BazaarController).GetMethod(nameof(BazaarController.GetSnapshot))
            .GetCustomAttribute<ResponseCacheAttribute>();

        Assert.Multiple(() =>
        {
            Assert.That(cache.Duration, Is.EqualTo(20));
            Assert.That(cache.VaryByQueryKeys, Is.EquivalentTo(new[] { "timestamp" }));
        });
    }
}

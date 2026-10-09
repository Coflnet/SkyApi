using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

public class BazaarControllerTests
{
    [Test]
    public void DayHistoryIsCachedNoLongerThanItsFiveMinuteInterval()
    {
        var cache = (ResponseCacheAttribute)typeof(BazaarController).GetMethod(nameof(BazaarController.HistoryGraphDay))
            .GetCustomAttributes(typeof(ResponseCacheAttribute), false).Single();
        Assert.That(cache.Duration, Is.EqualTo(300));
    }
}

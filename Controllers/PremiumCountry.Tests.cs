using System.Linq;
using System.Reflection;
using Coflnet.Sky.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class PremiumCountryTests
{
    private static PremiumController ControllerWithCountryHeader(string header)
    {
        var http = new DefaultHttpContext();
        if (header != null)
            http.Request.Headers["cf-ipcountry"] = header;
        return new PremiumController(null, null, null, null, null, null, null, null, null)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    [TestCase("DE", "DE")]
    [TestCase("de", "DE")]
    [TestCase(" us ", "US")]
    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("XX", null)]
    [TestCase("T1", null)]
    [TestCase("DEU", null)]
    public void ReturnsTheCloudflareCountryOrNull(string header, string expected)
    {
        var controller = ControllerWithCountryHeader(header);

        var result = controller.GetCountry();

        Assert.That(result.Country, Is.EqualTo(expected));
    }

    [Test]
    public void ResponseIsNeverStoredByCdnsOrSharedCaches()
    {
        var controller = ControllerWithCountryHeader("DE");

        controller.GetCountry();

        Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("private, no-store"));
    }

    [Test]
    public void ControllerDefaultIsNoStoreSoTheResponseCacheMiddlewareKeepsOut()
    {
        var cache = typeof(PremiumController).GetCustomAttribute<ResponseCacheAttribute>();
        var method = typeof(PremiumController).GetMethod(nameof(PremiumController.GetCountry));

        Assert.That(cache.NoStore, Is.True);
        Assert.That(method.GetCustomAttributes<ResponseCacheAttribute>().Any(a => !a.NoStore), Is.False);
    }
}

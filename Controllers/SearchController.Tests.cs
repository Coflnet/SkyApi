using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class SearchControllerTests
{
    private Mock<IItemsApi> itemsApi;
    private Mock<ItemFilterSearchService> filterSearch;
    private SearchController controller;

    [SetUp]
    public void Setup()
    {
        itemsApi = new Mock<IItemsApi>();
        filterSearch = new Mock<ItemFilterSearchService>(MockBehavior.Strict, null, null, null, 10);
        controller = new SearchController(itemsApi.Object, new SearchService(null, null, itemsApi.Object), filterSearch.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private void ItemsReturn(string term, params SearchResult[] results)
        => itemsApi.Setup(a => a.ItemsSearchTermGetAsync(term, It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results.ToList());

    [Test]
    public async Task SingleWordMissNeverUsesFilterParser()
    {
        ItemsReturn("hyperion");
        (await controller.SearchItem("hyperion")).Should().BeEmpty();
        filterSearch.Verify(f => f.TryParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task MultiWordDirectHitNeverUsesFilterParser()
    {
        ItemsReturn("aspect of the end", new SearchResult { Tag = "ASPECT_OF_THE_END", Text = "Aspect of the End", Flags = ItemFlags.AUCTION, Tier = Items.Client.Model.Tier.RARE });
        var result = await controller.SearchItem("aspect of the end");
        result.Should().ContainSingle().Which.Id.Should().Be("ASPECT_OF_THE_END");
        result.Single().Tier.Should().Be(Core.Tier.RARE, "the tier SkyItems reports is passed through unshifted");
        filterSearch.Verify(f => f.TryParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task MultiWordMissUsesFilterParser()
    {
        ItemsReturn("hyperion sharp 7");
        var expected = new List<SearchResultItem> { new() { Type = "filter", Id = "HYPERION?sharpness=7" } };
        filterSearch.Setup(f => f.TryParseAsync("hyperion sharp 7", It.IsAny<CancellationToken>())).ReturnsAsync(new FilterSearchResult(expected));
        (await controller.SearchItem("Hyperion Sharp 7")).Should().BeSameAs(expected);
        controller.HttpContext.Response.Headers.CacheControl.ToString().Should().BeEmpty("the ResponseCache attribute decides");
    }

    [Test]
    public async Task TimedOutParseIsNotCacheable()
    {
        ItemsReturn("hyperion sharp 7");
        filterSearch.Setup(f => f.TryParseAsync("hyperion sharp 7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilterSearchResult(new List<SearchResultItem>(), true));
        (await controller.SearchItem("hyperion sharp 7")).Should().BeEmpty();
        controller.HttpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Test]
    public async Task SingleWordMissIsCachedForFiveMinutes()
    {
        ItemsReturn("nothing");
        (await controller.SearchItem("nothing")).Should().BeEmpty();
        controller.HttpContext.Response.Headers.CacheControl.ToString().Should().Be("public,max-age=300");
    }

    [Test]
    public async Task EmptyParseIsCachedForFiveMinutes()
    {
        ItemsReturn("zzz sharp 7");
        filterSearch.Setup(f => f.TryParseAsync("zzz sharp 7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilterSearchResult(new List<SearchResultItem>(), false));
        (await controller.SearchItem("zzz sharp 7")).Should().BeEmpty();
        controller.HttpContext.Response.Headers.CacheControl.ToString().Should().Be("public,max-age=300");
    }

    [Test]
    public async Task NonEmptyAnswersLeaveTheCacheHeaderToTheAttribute()
    {
        ItemsReturn("aspect of the end", new SearchResult { Tag = "ASPECT_OF_THE_END", Text = "Aspect of the End", Flags = ItemFlags.AUCTION, Tier = Items.Client.Model.Tier.RARE });
        await controller.SearchItem("aspect of the end");
        controller.HttpContext.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }

    [Test]
    public void ResponseCacheVariesByExpectedResults()
    {
        var attribute = typeof(SearchController).GetMethod(nameof(SearchController.SearchItem))
            .GetCustomAttributes(typeof(ResponseCacheAttribute), false).Cast<ResponseCacheAttribute>().Single();
        attribute.VaryByQueryKeys.Should().Contain("expectedResults");
    }
}

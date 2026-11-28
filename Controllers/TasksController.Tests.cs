using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Controller;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SkyApi.Controllers;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class TasksControllerTests
{
    private static TasksController Create(IPublicTaskService service) => new(service)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Test]
    public async Task GetAll_Unavailable_Returns503()
    {
        var service = new Mock<IPublicTaskService>();
        service.Setup(s => s.GetAllAsync(default)).ThrowsAsync(new CoflnetException("tasks_unavailable", "x"));
        var controller = Create(service.Object);
        var result = await controller.GetAll();
        var objectResult = (ObjectResult)result.Result;
        objectResult.StatusCode.Should().Be(503);
        objectResult.Value.Should().BeOfType<ErrorResponse>().Which.Slug.Should().Be("tasks_unavailable");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    private static readonly PublicTaskEstimate[] Mixed =
    [
        new() { Slug = "water-fishing", Available = true },
        new() { Slug = "diana", RequiredEvent = "Mythological Ritual", Available = false, AvailabilityNote = "Needs Diana" },
    ];

    [Test]
    public async Task GetAll_LeavesOutUnavailableTasks()
    {
        var service = new Mock<IPublicTaskService>();
        service.Setup(s => s.GetAllAsync(default)).ReturnsAsync(Mixed);

        var result = await Create(service.Object).GetAll();

        var tasks = ((OkObjectResult)result.Result).Value.Should().BeAssignableTo<IEnumerable<PublicTaskEstimate>>().Which;
        tasks.Select(t => t.Slug).Should().Equal("water-fishing");
    }

    [Test]
    public async Task GetAll_ListsAnUnavailableTask_OnceItsEventStartedBySchedule()
    {
        // the cached availability went stale (e.g. the upstream is down) but the event started on its own schedule
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 0, 0, 1, TimeSpan.Zero));
        var started = new PublicTaskEstimate { Slug = "winter-fishing", RequiredEvent = "Jerry's Workshop", Available = false,
            NextAvailableAt = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc) };
        var later = new PublicTaskEstimate { Slug = "spooky-fishing", RequiredEvent = "Spooky Festival", Available = false,
            NextAvailableAt = new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc) };
        var service = new Mock<IPublicTaskService>();
        service.Setup(s => s.GetAllAsync(default)).ReturnsAsync([started, later, Mixed[1]]);
        var controller = new TasksController(service.Object, clock) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var result = await controller.GetAll();

        var tasks = ((OkObjectResult)result.Result).Value.Should().BeAssignableTo<IEnumerable<PublicTaskEstimate>>().Which;
        tasks.Select(t => t.Slug).Should().Equal("winter-fishing");
    }

    [Test]
    public async Task GetAll_IncludeUnavailable_ListsEveryTask()
    {
        var service = new Mock<IPublicTaskService>();
        service.Setup(s => s.GetAllAsync(default)).ReturnsAsync(Mixed);

        var result = await Create(service.Object).GetAll(includeUnavailable: true);

        var tasks = ((OkObjectResult)result.Result).Value.Should().BeAssignableTo<IEnumerable<PublicTaskEstimate>>().Which;
        tasks.Select(t => t.Slug).Should().Equal("water-fishing", "diana");
    }

    [Test]
    public void GetAll_ResponseCache_VariesByIncludeUnavailable()
    {
        // the response caching middleware keys by path only unless the query key is listed
        var cache = typeof(TasksController).GetMethod(nameof(TasksController.GetAll))!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), false).Cast<ResponseCacheAttribute>().Single();
        cache.VaryByQueryKeys.Should().Contain("includeUnavailable");
    }

    [Test]
    public async Task GetBySlug_ServesAnUnavailableTask()
    {
        var service = new Mock<IPublicTaskService>();
        service.Setup(s => s.GetBySlugAsync("diana", default)).ReturnsAsync(Mixed[1]);

        var result = await Create(service.Object).GetBySlug("diana");

        ((OkObjectResult)result.Result).Value.Should().BeSameAs(Mixed[1]);
    }

    [Test]
    public async Task GetBySlug_Unknown_Returns404()
    {
        var service = new Mock<IPublicTaskService>();
        service.Setup(s => s.GetBySlugAsync("nope", default)).ReturnsAsync((PublicTaskEstimate)null);
        var controller = Create(service.Object);
        var result = await controller.GetBySlug("nope");
        var objectResult = (ObjectResult)result.Result;
        objectResult.StatusCode.Should().Be(404);
        var body = objectResult.Value.Should().BeOfType<ErrorResponse>().Which;
        body.Slug.Should().Be("task_not_found");
        body.Message.Should().Contain("nope");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Test]
    public void ErrorResponse_SerializesWithMvcDefaults_KeepsSlugAndEmitsNullTrace()
    {
        // AddNewtonsoftJson() without options uses these settings for every response body
        var settings = new MvcNewtonsoftJsonOptions().SerializerSettings;
        var json = JObject.Parse(JsonConvert.SerializeObject(
            new ErrorResponse { Slug = "task_not_found", Message = "m" }, settings));
        json["slug"].Value<string>().Should().Be("task_not_found");
        json["message"].Value<string>().Should().Be("m");
        json["trace"].Type.Should().Be(JTokenType.Null);
    }
}

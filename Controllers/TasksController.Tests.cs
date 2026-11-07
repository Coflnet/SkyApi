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

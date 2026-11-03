using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Api.Models.Mod;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace SkyApi.Services.Description;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class InstanceSettingsClientTests
{
    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage Request;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(respond(request));
        }
    }

    private static InstanceSettingsClient CreateClient(FakeHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://smc.test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("ModCommands")).Returns(http);
        return new InstanceSettingsClient(factory.Object, Mock.Of<ILogger<InstanceSettingsClient>>());
    }

    [Test]
    public async Task EncodesSessionIdInQuery()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await CreateClient(handler).GetDescription("ab/c+d==", default);

        handler.Request.RequestUri.AbsoluteUri.Should().Be("http://smc.test/api/instance/description?sessionId=ab%2Fc%2Bd%3D%3D");
    }

    [TestCase(HttpStatusCode.NoContent, InstanceSettingsStatus.None)]
    [TestCase(HttpStatusCode.NotFound, InstanceSettingsStatus.None)]
    [TestCase(HttpStatusCode.InternalServerError, InstanceSettingsStatus.Failed)]
    public async Task StatusMapsToResult(HttpStatusCode status, InstanceSettingsStatus expected)
    {
        var result = await CreateClient(new FakeHandler(_ => new HttpResponseMessage(status))).GetDescription("id", default);

        result.Status.Should().Be(expected);
        result.Setting.Should().BeNull();
    }

    [Test]
    public async Task OkDeserializesSetting()
    {
        var setting = new DescriptionSetting { Disabled = true, Fields = new() { new() { DescriptionField.LBIN } } };
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(setting)) });

        var result = await CreateClient(handler).GetDescription("id", default);

        result.Status.Should().Be(InstanceSettingsStatus.Found);
        result.Setting.Disabled.Should().BeTrue();
        result.Setting.Fields.Should().HaveCount(1);
    }

    [Test]
    public async Task ExceptionReturnsFailed()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("down"));

        var result = await CreateClient(handler).GetDescription("id", default);

        result.Status.Should().Be(InstanceSettingsStatus.Failed);
    }

    [Test]
    public async Task TimeoutReturnsFailed()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException());

        var result = await CreateClient(handler).GetDescription("id", default);

        result.Status.Should().Be(InstanceSettingsStatus.Failed);
    }
}

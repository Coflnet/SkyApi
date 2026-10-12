using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Models.Notifications;

#pragma warning disable CS1591
/// <summary>
/// Runs the real MVC validation (including the implicit [Required] for non-nullable
/// reference types) against bodies the website sends to POST /api/notifications/targets.
/// </summary>
public class NotificationTargetTests
{
    private static ModelStateDictionary Validate(string json)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddNewtonsoftJson();
        using var provider = services.BuildServiceProvider();

        var model = JsonConvert.DeserializeObject<NotificationTarget>(json)!;
        var actionContext = new ActionContext(
            new DefaultHttpContext { RequestServices = provider },
            new RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var validator = provider.GetRequiredService<IObjectModelValidator>();
        validator.Validate(actionContext, null!, string.Empty, model);
        return actionContext.ModelState;
    }

    [Test]
    public void InGameMarkerWithNullTargetIsValid()
    {
        var state = Validate("{\"type\":\"InGame\",\"when\":\"NEVER\",\"target\":null,\"name\":\"In-game disabled\",\"useCount\":0}");
        Assert.That(state.IsValid, Is.True, string.Join("; ", state.Select(c => c.Key + ":" + string.Join(",", c.Value!.Errors.Select(e => e.ErrorMessage)))));
    }

    [Test]
    public void NullOrOmittedNameIsValid()
    {
        Assert.That(Validate("{\"type\":\"InGame\",\"when\":\"NEVER\",\"target\":null,\"name\":null}").IsValid, Is.True);
        Assert.That(Validate("{\"type\":\"InGame\",\"when\":\"NEVER\"}").IsValid, Is.True);
    }

    [Test]
    public void DiscordWebhookBodyIsValid()
    {
        var state = Validate("{\"type\":\"DiscordWebhook\",\"when\":\"ALWAYS\",\"target\":\"https://discord.com/api/webhooks/1/abc\",\"name\":\"My hook\",\"useCount\":0}");
        Assert.That(state.IsValid, Is.True);
    }

    [Test]
    public void TooLongNameIsStillRejected()
    {
        var state = Validate("{\"type\":\"DiscordWebhook\",\"when\":\"ALWAYS\",\"target\":\"x\",\"name\":\"" + new string('a', 33) + "\"}");
        Assert.That(state.IsValid, Is.False);
        Assert.That(state.ContainsKey("Name"), Is.True);
    }
}

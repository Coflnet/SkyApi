using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Api.Services.Mod.Tests;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller.Tests;

/// <summary>Tests for the goal endpoints of <see cref="ModBridgeController"/>.</summary>
[TestFixture]
public class ModBridgeControllerGoalsTests
{
    private ModBridgeController controller;
    private GoalList list;
    private Mock<Coflnet.Sky.Crafts.Client.Api.ICraftsApi> crafts;
    private Mock<IModUserPublisher> publisher;

    /// <summary>Creates the controller with a goal service on mocks.</summary>
    [SetUp]
    public void Setup()
    {
        GoalService goals;
        (goals, publisher, _, crafts, _, list) = GoalServiceTests.Create();
        var resolver = new Mock<IModUserResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<ControllerBase>())).ReturnsAsync(new ModUser("u1", "uuid", "Steve"));
        controller = new ModBridgeController(resolver.Object, publisher.Object, Mock.Of<IBoosterCookieService>(),
            Mock.Of<IPendingBazaarSearchService>(), null, new MemoryCache(new MemoryCacheOptions()), goals);
    }

    private void PlanOrder()
    {
        var plan = new CraftAcquisitionPlan(itemId: "ROOT", quantity: 1, method: "craft", purchases: new(), ingredients: new()
        {
            new CraftAcquisitionPlan(itemId: "BZ", quantity: 4, method: "buy", purchases: new() { new AcquisitionFill(source: "order", quantity: 4) }, ingredients: new())
        });
        crafts.Setup(c => c.GetAcquisitionAsync("ROOT", It.IsAny<long?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(plan);
    }

    /// <summary>Kinds are mapped and the DTO serializes enums as camelCase strings.</summary>
    [Test]
    public async Task Create_Craft_ReturnsCamelCaseEnums()
    {
        PlanOrder();
        var result = await controller.CreateGoal(new ModGoalCreateRequest { Kind = "craft", ItemTag = "ROOT", Quantity = 1 });

        result.Root.Kind.Should().Be(StepKind.Craft);
        result.Root.Children.Single().Kind.Should().Be(StepKind.BuyOrder);
        result.Source.Should().Be("mod");
        var json = JsonConvert.SerializeObject(result, new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() });
        json.Should().Contain("\"kind\":\"buyOrder\"");
        json.Should().Contain("\"status\":\"todo\"");
        list.FocusId.Should().Be(result.Id);
    }

    /// <summary>GET returns the focus id and the goals.</summary>
    [Test]
    public async Task Get_ReturnsFocusId()
    {
        PlanOrder();
        var created = await controller.CreateGoal(new ModGoalCreateRequest { Kind = "craft", ItemTag = "ROOT" });
        var response = await controller.GetGoals();
        response.FocusId.Should().Be(created.Id);
        response.Goals.Should().ContainSingle().Which.Total.Should().BeGreaterThan(0);
    }

    /// <summary>Unknown kinds are rejected.</summary>
    [Test]
    public async Task Create_InvalidKind_Throws()
    {
        var act = () => controller.CreateGoal(new ModGoalCreateRequest { Kind = "mine", ItemTag = "ROOT" });
        (await act.Should().ThrowAsync<CoflnetException>()).Which.Slug.Should().Be("invalid_goal_kind");
    }

    /// <summary>A non forge item is reported as such.</summary>
    [Test]
    public async Task Create_NotForgeItem_Throws()
    {
        var act = () => controller.CreateGoal(new ModGoalCreateRequest { Kind = "forge", ItemTag = "NOPE" });
        (await act.Should().ThrowAsync<CoflnetException>()).Which.Slug.Should().Be("not_a_forge_item");
    }

    /// <summary>Delete returns 204 for known and 404 for unknown goals.</summary>
    [Test]
    public async Task Delete_KnownAndUnknown()
    {
        PlanOrder();
        var created = await controller.CreateGoal(new ModGoalCreateRequest { Kind = "craft", ItemTag = "ROOT" });
        (await controller.DeleteGoal("nope")).Should().BeOfType<NotFoundResult>();
        (await controller.DeleteGoal(created.Id)).Should().BeOfType<NoContentResult>();
    }
}

using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class ModControllerTests
{
    [Test]
    public async Task DonutServerRequestUsesHypixelDescriptionPath()
    {
        var descriptionService = new ModDescriptionService(null, null, null, null, null, null,
            Mock.Of<ILogger<ModDescriptionService>>(), Mock.Of<IConfiguration>(), null, null, null, null, null, null, null, null, null);
        var controller = CreateController(descriptionService);
        // "Game Menu" makes the Hypixel description path return one empty modification list per slot
        var inventory = new InventoryDataWithSettings { Server = "donut", ChestName = "Game Menu" };

        var result = await controller.ItemDescriptionModifications(inventory, null, null);

        var expectedSlots = descriptionService.ConvertToAuctions(inventory).Count;
        Assert.That(expectedSlots, Is.GreaterThan(0));
        Assert.That(result.Count(), Is.EqualTo(expectedSlots));
        Assert.That(result.All(slot => !slot.Any()));
    }

    /// <summary>
    /// Fills only the description service and logger so the test does not depend on the exact constructor signature
    /// </summary>
    private static ModController CreateController(ModDescriptionService descriptionService)
    {
        var constructor = typeof(ModController).GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(p => p.ParameterType switch
        {
            var t when t == typeof(ModDescriptionService) => descriptionService,
            var t when t == typeof(ILogger<ModController>) => Mock.Of<ILogger<ModController>>(),
            _ => (object)null
        }).ToArray();
        return (ModController)constructor.Invoke(arguments);
    }
}

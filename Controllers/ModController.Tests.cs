using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class ModControllerTests
{
    /// <summary>
    /// Creates a <see cref="ModController"/> with the given dependencies matched by type; every other dependency is null
    /// </summary>
    public static ModController CreateController(params object[] dependencies)
    {
        var constructor = typeof(ModController).GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(p => dependencies.FirstOrDefault(d => p.ParameterType.IsInstanceOfType(d)))
            .ToArray();
        return (ModController)constructor.Invoke(arguments);
    }

    [Test]
    public async Task DonutServerRequestUsesHypixelDescriptionPath()
    {
        var descriptionService = new ModDescriptionService(null, null, null, null, null, null,
            NullLogger<ModDescriptionService>.Instance, null, null, null, null, null, null, null, null, null, null);
        var controller = CreateController(descriptionService);

        var hypixel = await controller.ItemDescriptionModifications(GameMenu(null), null, null);
        IEnumerable<IEnumerable<DescModification>> donut = null;
        Assert.That(async () => donut = await controller.ItemDescriptionModifications(GameMenu("donut"), null, null), Throws.Nothing);

        Assert.That(hypixel.Count(), Is.GreaterThan(0));
        Assert.That(donut.Count(), Is.EqualTo(hypixel.Count()));
        Assert.That(donut.SelectMany(m => m), Is.Empty);
    }

    private static InventoryDataWithSettings GameMenu(string server) => new() { Server = server, ChestName = "Game Menu" };
}

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Items.Client.Model;
using Coflnet.Sky.Sniper.Client.Api;
using Coflnet.Sky.Sniper.Client.Model;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Tests for <see cref="CraftingController"/>, in particular the recursive craft-tree expansion done by
/// <see cref="CraftingController.GetInstructions"/>.
/// </summary>
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class CraftingControllerTests
{
    private Mock<ICraftsApi> craftsApi;
    private Mock<IAuctionApi> auctionApi;
    private Mock<IItemsApi> itemsApi;
    private CraftingController controller;

    [SetUp]
    public void Setup()
    {
        craftsApi = new Mock<ICraftsApi>();
        auctionApi = new Mock<IAuctionApi>();
        itemsApi = new Mock<IItemsApi>();

        // Nothing is a bazaar item and nothing has a lbin in these tests, so every ingredient falls back
        // to the "/ah" copy command - this keeps the tests independent of AuctionService/Cassandra.
        itemsApi.Setup(a => a.ItemsBazaarTagsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        itemsApi.Setup(a => a.ItemNamesGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemPreview>());
        auctionApi.Setup(a => a.ApiAuctionLbinsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ReferencePrice>());

        var pricesService = new PricesService(null, null, itemsApi.Object, null);
        controller = new CraftingController(Mock.Of<IConfiguration>(), pricesService, Mock.Of<IProfileClient>(), auctionApi.Object, craftsApi.Object);
    }

    private static Recipe MakeRecipe(params string[] ingredients)
    {
        string Get(int i) => i < ingredients.Length ? ingredients[i] : null;
        return new Recipe(a1: Get(0), a2: Get(1), a3: Get(2), b1: Get(3), b2: Get(4), b3: Get(5), c1: Get(6), c2: Get(7), c3: Get(8));
    }

    private void SetupRecipe(string tag, params string[] ingredients)
    {
        craftsApi.Setup(c => c.GetRecipeAsync(tag, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRecipe(ingredients));
    }

    [Test]
    public async Task NestedSubCraft_CopyCommandsIncludeDirectAndSubCraftIngredients()
    {
        SetupRecipe("TOP_ITEM", "MID_ITEM:1");
        SetupRecipe("MID_ITEM", "SUB_ITEM:2");
        craftsApi.Setup(c => c.GetRecipeAsync("SUB_ITEM", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Recipe)null); // SUB_ITEM is not craftable itself

        var result = await controller.GetInstructions("TOP_ITEM", itemsApi.Object);

        result.CopyCommands.Should().ContainKey("MID_ITEM", "it is a direct ingredient of the top level recipe");
        result.CopyCommands.Should().ContainKey("SUB_ITEM", "it is an ingredient of the MID_ITEM sub-craft");
        result.DetailsPath.Should().ContainKey("MID_ITEM");
        result.DetailsPath.Should().ContainKey("SUB_ITEM");
        // the top level 3x3 grid must stay untouched for backwards compatibility with the website
        result.Recipe["A1"].Should().Be("MID_ITEM:1");
    }

    [Test]
    public async Task RecipeCycle_TerminatesAndDoesNotLoopForever()
    {
        SetupRecipe("ITEM_A", "ITEM_B:1");
        SetupRecipe("ITEM_B", "ITEM_A:1");

        var task = controller.GetInstructions("ITEM_A", itemsApi.Object);
        var winner = await Task.WhenAny(task, Task.Delay(System.TimeSpan.FromSeconds(5)));

        winner.Should().Be(task, "a recipe cycle (A needs B, B needs A) must not hang the request");
        var result = await task;

        result.CopyCommands.Should().ContainKey("ITEM_B");
        // the visited-set must stop ITEM_B's recipe from being fetched more than once even though
        // it is reachable again through ITEM_A on the second expansion level
        craftsApi.Verify(c => c.GetRecipeAsync("ITEM_B", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ChainDeeperThanMaxDepth_StopsExpandingAtTheCap()
    {
        SetupRecipe("TAG0", "TAG1:1");
        SetupRecipe("TAG1", "TAG2:1");
        SetupRecipe("TAG2", "TAG3:1");
        SetupRecipe("TAG3", "TAG4:1");
        SetupRecipe("TAG4", "TAG5:1");
        SetupRecipe("TAG5", "TAG6:1");
        SetupRecipe("TAG6", "TAG7:1");

        var result = await controller.GetInstructions("TAG0", itemsApi.Object);

        // direct ingredient (TAG1) plus 5 further levels of expansion (TAG2..TAG6) are included
        foreach (var expectedTag in new[] { "TAG1", "TAG2", "TAG3", "TAG4", "TAG5", "TAG6" })
            result.CopyCommands.Should().ContainKey(expectedTag);
        result.CopyCommands.Should().NotContainKey("TAG7", "it sits one level deeper than the max craft tree depth");

        // TAG6's own recipe (which would reveal TAG7) must never be fetched once the depth cap is hit
        craftsApi.Verify(c => c.GetRecipeAsync("TAG6", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SubRecipeFetchThrows_DirectIngredientCommandsAreStillReturned()
    {
        SetupRecipe("TOP2", "MID2:1");
        craftsApi.Setup(c => c.GetRecipeAsync("MID2", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Exception("simulated recipe service failure"));

        var result = await controller.GetInstructions("TOP2", itemsApi.Object);

        result.CopyCommands.Should().ContainKey("MID2");
        result.DetailsPath.Should().ContainKey("MID2");
    }

    /// <summary>
    /// <see cref="CraftingController.GetProfitable"/> must map the raw <see cref="ProfitableCraft"/>
    /// list from SkyCrafts to the enriched <see cref="Coflnet.Sky.Api.Models.ProfitableCraftDto"/>,
    /// carrying the backend-computed craft-savings signal for every ingredient the crafting engine
    /// decided to subcraft instead of buy.
    /// </summary>
    [Test]
    public async Task GetProfitable_CraftedIngredientExposesCraftSavings()
    {
        var craftedIngredient = new Crafts.Client.Model.Ingredient(itemId: "ENCHANTED_IRON", count: 4, cost: 50, buyOrderCost: 100, craftCost: 50, type: "craft");
        var boughtIngredient = new Crafts.Client.Model.Ingredient(itemId: "IRON_INGOT", count: 32, cost: 10, buyOrderCost: 10, craftCost: 0, type: null);
        var craft = new ProfitableCraft(itemId: "TEST_ITEM", itemName: "Test Item", sellPrice: 1000, craftCost: 400,
            buyOrderCraftCost: 500, ingredients: new List<Crafts.Client.Model.Ingredient> { craftedIngredient, boughtIngredient });
        craftsApi.Setup(c => c.GetProfitableAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProfitableCraft> { craft });

        var result = (await controller.GetProfitable()).ToList();

        result.Should().HaveCount(1);
        var mapped = result[0].Ingredients;
        mapped.Should().HaveCount(2);

        var craftedResult = mapped.Single(i => i.ItemId == "ENCHANTED_IRON");
        craftedResult.IsSubcraft.Should().BeTrue();
        craftedResult.CraftSavings.Should().Be(50, "buying costs 100 but crafting only cost 50");
        craftedResult.CraftSavingsPercent.Should().Be(50);

        var boughtResult = mapped.Single(i => i.ItemId == "IRON_INGOT");
        boughtResult.IsSubcraft.Should().BeFalse();
        boughtResult.CraftSavings.Should().Be(0);
        boughtResult.CraftSavingsPercent.Should().Be(0);
    }
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

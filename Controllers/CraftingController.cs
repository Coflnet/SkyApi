using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Sniper.Client.Api;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Endpoints for crafting related data
/// </summary>
[ApiController]
[Route("api/craft")]
[ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
public class CraftingController : ControllerBase
{
    private IProfileClient profileClient;
    private string apiUrl;
    PricesService pricesService;
    ICraftsApi craftsApi;
    private IAuctionApi auctionApi;
    /// <summary>
    /// Creates a new instance of <see cref="CraftingController"/>
    /// </summary>
    /// <param name="config"></param>
    /// <param name="pricesService"></param>
    /// <param name="profileClient"></param>
    /// <param name="auctionApi"></param>
    /// <param name="craftsApi"></param>
    public CraftingController(IConfiguration config, PricesService pricesService, IProfileClient profileClient, IAuctionApi auctionApi, ICraftsApi craftsApi)
    {

        apiUrl = config["API_BASE_URL"];
        this.pricesService = pricesService;
        this.profileClient = profileClient;
        this.auctionApi = auctionApi;
        this.craftsApi = craftsApi;
    }

    /// <summary>
    /// Craft flips
    /// </summary>
    /// <param name="player"></param>
    /// <param name="profile"></param>
    /// <returns></returns>
    [Route("profit")]
    [HttpGet]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "player", "profile" })]
    public async Task<IEnumerable<ProfitableCraftDto>> GetProfitable(string player = null, string profile = null)
    {
        var crafts = await craftsApi.GetProfitableAsync();
        if (profile == null)
            return crafts.Select(ProfitableCraftDto.FromProfitableCraft);
        try
        {
            var filtered = await profileClient.FilterProfitableCrafts(Task.FromResult(crafts), player, profile);
            return filtered.Select(ProfitableCraftDto.FromProfitableCraft);
        }
        catch (System.Exception e)
        {
            dev.Logger.Instance.Error(e, "getting profile data for crafts");
            return crafts.Select(ProfitableCraftDto.FromProfitableCraft);
        }
    }

    /// <summary>
    /// Returns the crafting recipe for some item
    /// </summary>
    /// <param name="itemTag"></param>
    /// <returns></returns>
    [Route("recipe/{itemTag}")]
    [HttpGet]
    [ResponseCache(Duration = 3600 * 12, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<Recipe> GetRecipe(string itemTag)
    {
        return await craftsApi.GetRecipeAsync(itemTag);
    }

    /// <summary>
    /// Maximum number of sub-craft levels to expand into when collecting the full craft tree.
    /// Direct ingredients of the top level recipe count as level 0 and are always included;
    /// this bounds how many further levels of sub-recipes are fetched.
    /// </summary>
    private const int MaxCraftTreeDepth = 5;
    /// <summary>
    /// Runaway guard - stop expanding the craft tree once this many distinct ingredient tags were collected.
    /// </summary>
    private const int MaxCraftTreeTags = 200;

    /// <summary>
    /// Returns craft instructions and if lbin, for every ingredient in the whole craft tree
    /// (the top level recipe plus all of its sub-crafts, recursively)
    /// </summary>
    /// <param name="itemTag"></param>
    /// <returns></returns>
    [Route("{itemTag}/instructions")]
    [HttpGet]
    [ResponseCache(Duration = 3600 * 12, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<CraftInstruction> GetInstructions(string itemTag, [FromServices] Items.Client.Api.IItemsApi itemApi)
    {
        var itemNamesTask = itemApi.ItemNamesGetAsync();
        var response = await GetRecipe(itemTag);
        var recipe = JsonConvert.DeserializeObject<Dictionary<string, string>>(JsonConvert.SerializeObject(response) ?? "{}");
        var directIngredients = ExtractIngredients(recipe).ToList();
        var ids = (await ExpandIngredientTree(directIngredients)).ToList();

        var itemsOnBazaar = await pricesService.GetBazaarItems();
        var lbins = await auctionApi.ApiAuctionLbinsGetAsync();
        var nameLookup = (await itemNamesTask).ToDictionary(x => x.Tag, x => x.Name);
        var elements = await Task.WhenAll(ids.Select(async x =>
        {
            if (itemsOnBazaar.Contains(x))
                return (x, $"/item/{x}", $"/bz {BazaarUtils.GetSearchValue(x, nameLookup.GetValueOrDefault(x) ?? x)}");
            var lbin = lbins.GetValueOrDefault(x);
            if (lbin == null)
                return (x, $"/item/{x}?range=active", $"/ah");
            var auction = await AuctionService.Instance.GetAuctionAsync(AuctionService.Instance.GetUuid(lbin.AuctionId));
            return (x, $"/auction/{auction.Uuid}", $"/viewauction {auction.Uuid}");
        }));
        var commands = elements.ToDictionary(x => x.Item1, x => x.Item3);
        var path = elements.ToDictionary(x => x.Item1, x => x.Item2);
        return new CraftInstruction(itemTag, recipe, commands, path);
    }

    /// <summary>
    /// Recursively expands a set of root ingredient tags to also include the ingredients of any
    /// sub-crafts (and their sub-crafts, ...). Bounded by <see cref="MaxCraftTreeDepth"/> and
    /// <see cref="MaxCraftTreeTags"/> and guarded against cycles via a "recipe already fetched" set,
    /// so recursive/diamond shaped recipes can neither loop nor be fetched twice.
    /// Each depth level is fetched concurrently since this endpoint has to stay cheap even though it
    /// is response-cached for 12h.
    /// </summary>
    private async Task<HashSet<string>> ExpandIngredientTree(IEnumerable<string> rootIngredients)
    {
        var allTags = new HashSet<string>();
        var recipeAlreadyFetchedFor = new HashSet<string>();
        var frontier = new List<string>();

        foreach (var tag in rootIngredients)
        {
            if (allTags.Add(tag))
                frontier.Add(tag);
        }

        for (var depth = 0; depth < MaxCraftTreeDepth && frontier.Count > 0 && allTags.Count < MaxCraftTreeTags; depth++)
        {
            var toExpand = frontier.Where(t => recipeAlreadyFetchedFor.Add(t)).ToList();
            if (toExpand.Count == 0)
                break;

            var subRecipes = await Task.WhenAll(toExpand.Select(async tag =>
            {
                try
                {
                    return await craftsApi.GetRecipeAsync(tag);
                }
                catch (Exception e)
                {
                    // no recipe (not craftable) or a transient failure - just stop expanding this branch
                    dev.Logger.Instance.Info($"Could not load sub recipe for {tag} while expanding craft instructions for the tree: {e.Message}");
                    return null;
                }
            }));

            var nextFrontier = new List<string>();
            foreach (var subRecipe in subRecipes)
            {
                if (subRecipe == null || allTags.Count >= MaxCraftTreeTags)
                    continue;
                var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(JsonConvert.SerializeObject(subRecipe) ?? "{}");
                foreach (var childTag in ExtractIngredients(dict))
                {
                    if (allTags.Count >= MaxCraftTreeTags)
                        break;
                    if (allTags.Add(childTag))
                        nextFrontier.Add(childTag);
                }
            }
            frontier = nextFrontier;
        }

        return allTags;
    }

    /// <summary>
    /// Extracts the ingredient tags out of a flat recipe dictionary (slots A1..C3 plus "count"),
    /// whose values are shaped like "ITEM_TAG:quantity". Quantities are deliberately not returned:
    /// a tag can occupy several slots and appear under several parents, so no flat per-tag count is
    /// meaningful here - the website derives real totals from the craft tree it already has.
    /// </summary>
    private static IEnumerable<string> ExtractIngredients(Dictionary<string, string> recipe)
    {
        if (recipe == null)
            yield break;
        foreach (var slot in recipe)
        {
            if (slot.Key == "count" || string.IsNullOrEmpty(slot.Value))
                continue;
            var tag = slot.Value.Split(':').First();
            if (tag.Length < 3)
                continue;
            yield return tag;
        }
    }
}

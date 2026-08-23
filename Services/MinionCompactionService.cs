using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services;

/// <summary>
/// Works out what each minion product compacts into and how many it takes, by reading the recipes.
///
/// The obvious shortcut -- assume <c>ENCHANTED_X</c> is always 160 <c>X</c> -- is wrong for every
/// second-tier enchanted item. An Enchanted Blaze Rod is 160 Enchanted Blaze Powder, so it stands for
/// 25,600 blaze rods; pricing it as 160 reported a blaze minion at roughly a hundred times its real
/// output. Recipes rarely change, so the resolved map is cached rather than rebuilt per request.
/// </summary>
public class MinionCompactionService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(12);

    private readonly ICraftsApi crafts;
    private readonly ILogger<MinionCompactionService> logger;
    private readonly SemaphoreSlim building = new(1, 1);
    private Dictionary<string, MinionCompaction> cached = new();
    private DateTime builtAt = DateTime.MinValue;

    /// <summary>Creates a new instance of <see cref="MinionCompactionService"/></summary>
    public MinionCompactionService(ICraftsApi crafts, ILogger<MinionCompactionService> logger)
    {
        this.crafts = crafts;
        this.logger = logger;
    }

    /// <summary>The compaction step for each of <paramref name="productTags"/> that has one.</summary>
    public async Task<Dictionary<string, MinionCompaction>> GetAsync(
        IEnumerable<string> productTags, IEnumerable<string> compactableTags)
    {
        if (DateTime.UtcNow - builtAt < CacheFor)
            return cached;
        await building.WaitAsync();
        try
        {
            if (DateTime.UtcNow - builtAt < CacheFor)
                return cached;
            var recipes = new Dictionary<string, Dictionary<string, double>>();
            foreach (var tag in compactableTags.Distinct())
            {
                try
                {
                    var recipe = await crafts.GetRecipeAsync(tag);
                    if (recipe == null)
                        continue;
                    var totals = MinionCalculator.ParseGrid(new[]
                    {
                        recipe.A1, recipe.A2, recipe.A3,
                        recipe.B1, recipe.B2, recipe.B3,
                        recipe.C1, recipe.C2, recipe.C3
                    });
                    if (totals.Count > 0)
                        recipes[tag] = totals;
                }
                catch (Exception exception)
                {
                    // A recipe that cannot be read costs one compaction option, not the whole ranking.
                    logger.LogWarning(exception, "could not read the recipe for {tag}", tag);
                }
            }
            cached = MinionCalculator.ResolveCompaction(recipes, productTags);
            builtAt = DateTime.UtcNow;
            return cached;
        }
        finally
        {
            building.Release();
        }
    }
}

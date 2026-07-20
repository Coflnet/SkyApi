namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Copy commands/details paths for the whole craft tree (top level ingredients plus all sub crafts).
/// </summary>
/// <param name="ItemTag">Tag of the item the instructions were requested for</param>
/// <param name="Recipe">Flat top level 3x3 recipe grid, unchanged for backwards compatibility with the website</param>
/// <param name="CopyCommands">Copy command for every ingredient tag found anywhere in the craft tree</param>
/// <param name="DetailsPath">Details page path for every ingredient tag found anywhere in the craft tree</param>
public record CraftInstruction(string ItemTag, Dictionary<string, string> Recipe, Dictionary<string, string> CopyCommands, Dictionary<string, string> DetailsPath);
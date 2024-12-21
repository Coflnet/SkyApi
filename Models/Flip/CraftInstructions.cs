namespace Coflnet.Sky.Api.Models;

public record CraftInstruction(string ItemTag, Dictionary<string, string> Recipe, Dictionary<string,string> CopyCommands, Dictionary<string,string> DetailsPath);
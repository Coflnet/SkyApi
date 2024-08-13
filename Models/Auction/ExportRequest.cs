namespace Coflnet.Sky.Api.Models;

public class ExportRequestCreate
{
    public Sky.Auctions.Client.Model.ExportFlags Flags { get; set; }
    public Dictionary<string, string> Filters { get; set; }
    public string DiscordWebhookUrl { get; set; }
}
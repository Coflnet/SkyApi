namespace Coflnet.Sky.Api.Models;

/// <summary>Represents an export request create.</summary>
public class ExportRequestCreate
{
    /// <summary>Gets or sets the flags.</summary>
    public Sky.Auctions.Client.Model.ExportFlags Flags { get; set; }
    /// <summary>Gets or sets the filters.</summary>
    public Dictionary<string, string> Filters { get; set; }
    /// <summary>Gets or sets the discord webhook url.</summary>
    public string DiscordWebhookUrl { get; set; }
}

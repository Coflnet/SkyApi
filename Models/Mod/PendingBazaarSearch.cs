using System;

namespace Coflnet.Sky.Api.Models.Mod;

/// <summary>A bazaar search queued for display in the mod's bazaar side panel.</summary>
public class PendingBazaarSearch
{
    /// <summary>Search term / item name</summary>
    public string Name { get; set; }
    /// <summary>Item tag if known</summary>
    public string Tag { get; set; }
    /// <summary>Optional amount</summary>
    public long? Amount { get; set; }
    /// <summary>When the search was queued</summary>
    public DateTime CreatedAt { get; set; }
}

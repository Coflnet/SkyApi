using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models.Tasks;

/// <summary>
/// A community money making task estimate for the website
/// </summary>
public class PublicTaskEstimate
{
    /// <summary>Display name</summary>
    public string Name { get; set; }
    /// <summary>Url safe unique identifier</summary>
    public string Slug { get; set; }
    /// <summary>Category of the task</summary>
    public string Category { get; set; }
    /// <summary>Short description</summary>
    public string Description { get; set; }
    /// <summary>active, passive or limited</summary>
    public string Type { get; set; }
    /// <summary>Estimated coins per hour</summary>
    public double CoinsPerHour { get; set; }
    /// <summary>Where the estimate comes from (stat_bucket, global, formula)</summary>
    public string Source { get; set; }
    /// <summary>Hours of community tracking behind the estimate</summary>
    public double CommunityTrackedHours { get; set; }
    /// <summary>Number of players that contributed</summary>
    public int Contributors { get; set; }
    /// <summary>Players currently doing the task</summary>
    public int CurrentDoers { get; set; }
    /// <summary>Change of current doers over the last 20 minutes</summary>
    public int DoersChange20m { get; set; }
    /// <summary>Island the task takes place on</summary>
    public string Island { get; set; }
    /// <summary>Location description</summary>
    public string Where { get; set; }
    /// <summary>Warp command</summary>
    public string Warp { get; set; }
    /// <summary>Wiki page of the task</summary>
    public string WikiUrl { get; set; }
    /// <summary>Wiki page of the location</summary>
    public string WhereWikiUrl { get; set; }
    /// <summary>Steps to perform</summary>
    public List<PublicTaskStep> Steps { get; set; } = new();
    /// <summary>Items dropped</summary>
    public List<PublicTaskDrop> Drops { get; set; } = new();
    /// <summary>Best zones for this task</summary>
    public List<PublicTaskZone> BestZones { get; set; } = new();
}

/// <summary>A step of a task</summary>
public class PublicTaskStep
{
    /// <summary>Step number</summary>
    public int Number { get; set; }
    /// <summary>Instruction text</summary>
    public string Text { get; set; }
}

/// <summary>An item dropped by a task</summary>
public class PublicTaskDrop
{
    /// <summary>Item tag</summary>
    public string ItemTag { get; set; }
    /// <summary>Display name, null if unknown</summary>
    public string ItemName { get; set; }
    /// <summary>Drops per hour</summary>
    public double RatePerHour { get; set; }
    /// <summary>Price of one item</summary>
    public double PriceEach { get; set; }
    /// <summary>Coins per hour this drop contributes</summary>
    public double ContributionPerHour { get; set; }
}

/// <summary>A zone and its earnings</summary>
public class PublicTaskZone
{
    /// <summary>Zone name</summary>
    public string Zone { get; set; }
    /// <summary>Coins per hour in the zone</summary>
    public double CoinsPerHour { get; set; }
    /// <summary>Players tracked in the zone</summary>
    public int Players { get; set; }
}

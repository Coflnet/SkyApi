using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

#nullable enable annotations

namespace Coflnet.Sky.Api.Models.Notifications;

/// <summary>Represents a notification target.</summary>
public class NotificationTarget
{
    /// <summary>
    /// Primary Key for database
    /// </summary>
    /// <value></value>
    public int Id { get; set; }
    /// <summary>
    /// The target to send the notification to
    /// Depends on the <see cref="Type"/>
    /// When Type is WEBHOOK this is the url
    /// When Type is DISCORD this is the user id to be mentioned
    /// When Type is DISCORD_WEBHOOK this is the url
    /// When Type is FIREBASE this is the token
    /// When Type is EMAIL this is the email address
    /// </summary>
    public string Target { get; set; }
    /// <summary>Gets or sets the type.</summary>
    public TargetType Type { get; set; }
    /// <summary>Gets or sets the when.</summary>
    public NotifyWhen When { get; set; }
    /// <summary>Gets or sets the user id.</summary>
    [MaxLength(36)]
    public string? UserId { get; set; }
    /// <summary>
    /// User Given name of this target
    /// </summary>
    /// <value></value>
    [MaxLength(32)]
    public string Name { get; set; }
    /// <summary>Gets or sets the use count.</summary>
    public int UseCount { get; set; }
    // string enum
    /// <summary>Defines the available target type values.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TargetType
    {
        /// <summary>Represents the unkown option.</summary>
        UNKOWN,
        /// <summary>Represents the webhook option.</summary>
        WEBHOOK,
        /// <summary>Represents the discord option.</summary>
        DISCORD,
        /// <summary>Represents the discord webhook option.</summary>
        DiscordWebhook,
        /// <summary>Represents the firebase option.</summary>
        FIREBASE,
        /// <summary>Represents the email option.</summary>
        EMAIL,
        /// <summary>Represents the in game option.</summary>
        InGame
    }

    /// <summary>Defines the available notify when values.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotifyWhen
    {
        /// <summary>Represents the never option.</summary>
        NEVER,
        /// <summary>Represents the after fail option.</summary>
        AfterFail,
        /// <summary>Represents the always option.</summary>
        ALWAYS
    }
}

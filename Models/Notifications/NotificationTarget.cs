using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Coflnet.Sky.Api.Models.Notifications;

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
    public TargetType Type { get; set; }
    public NotifyWhen When { get; set; }
    [MaxLength(36)]
    public string? UserId { get; set; }
    /// <summary>
    /// User Given name of this target
    /// </summary>
    /// <value></value>
    [MaxLength(32)]
    public string Name { get; set; }
    public int UseCount { get; set; }
    // string enum
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TargetType
    {
        UNKOWN,
        WEBHOOK,
        DISCORD,
        DiscordWebhook,
        FIREBASE,
        EMAIL,
        InGame
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotifyWhen
    {
        NEVER,
        AfterFail,
        ALWAYS
    }
}
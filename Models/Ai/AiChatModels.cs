using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Coflnet.Sky.Api.Models.Ai;

/// <summary>Represents an AI chat request.</summary>
public class AiChatRequest
{
    /// <summary>Opaque server-issued conversation id. Omit it to start a new conversation.</summary>
    [RegularExpression("^[a-f0-9]{32}$")]
    public string ConversationId { get; set; }

    /// <summary>Gets or sets the message.</summary>
    [Required, MaxLength(6000)]
    public string Message { get; set; }

    /// <summary>The current site path, used only to keep answers relevant and linkable.</summary>
    [MaxLength(300)]
    public string Page { get; set; }
}

/// <summary>Represents an AI chat response.</summary>
public class AiChatResponse
{
    /// <summary>Gets or sets the answer.</summary>
    public string Answer { get; set; }
    /// <summary>Gets or sets the conversation id.</summary>
    public string ConversationId { get; set; }
    /// <summary>Gets or sets the trace id.</summary>
    public string TraceId { get; set; }
    /// <summary>Gets or sets the transcript bytes.</summary>
    public int TranscriptBytes { get; set; }
    /// <summary>Gets or sets the transcript limit.</summary>
    public int TranscriptLimit { get; set; }
    /// <summary>Gets or sets the requires new conversation.</summary>
    public bool RequiresNewConversation { get; set; }
    /// <summary>Gets or sets the requires bug report.</summary>
    public bool RequiresBugReport { get; set; }
    /// <summary>Gets or sets the error.</summary>
    public string Error { get; set; }
    /// <summary>Gets or sets the quota.</summary>
    public AiQuota Quota { get; set; }
    /// <summary>Gets or sets the data notice.</summary>
    public string DataNotice { get; set; }
}

/// <summary>Represents an AI usage quota.</summary>
public class AiQuota
{
    /// <summary>Gets or sets the limit.</summary>
    public int Limit { get; set; }
    /// <summary>Gets or sets the remaining.</summary>
    public int Remaining { get; set; }
    /// <summary>Gets or sets the resets at.</summary>
    public DateTimeOffset ResetsAt { get; set; }
    /// <summary>Gets or sets the tier.</summary>
    public string Tier { get; set; } = "anonymous";
}

/// <summary>Represents a knowledge result.</summary>
/// <param name="Title">The title.</param>
/// <param name="Url">The url.</param>
/// <param name="Content">The content.</param>
/// <param name="Source">The source.</param>
/// <param name="Score">The score.</param>
public record KnowledgeResult(string Title, string Url, string Content, string Source, double Score);

/// <summary>Represents an AI chat result.</summary>
/// <param name="Answer">The generated answer.</param>
/// <param name="ConversationId">The conversation ID.</param>
/// <param name="TranscriptBytes">The current transcript size in bytes.</param>
/// <param name="TranscriptLimit">The transcript size limit in bytes.</param>
/// <param name="RequiresNewConversation">Whether the caller must start a new conversation.</param>
/// <param name="RequiresBugReport">Whether the result should prompt a bug report.</param>
public record AiChatResult(
    string Answer,
    string ConversationId,
    int TranscriptBytes,
    int TranscriptLimit,
    bool RequiresNewConversation,
    bool RequiresBugReport);

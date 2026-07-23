using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Coflnet.Sky.Api.Models.Ai;

public class AiChatRequest
{
    /// <summary>Opaque server-issued conversation id. Omit it to start a new conversation.</summary>
    [RegularExpression("^[a-f0-9]{32}$")]
    public string ConversationId { get; set; }

    [Required, MaxLength(6000)]
    public string Message { get; set; }

    /// <summary>The current site path, used only to keep answers relevant and linkable.</summary>
    [MaxLength(300)]
    public string Page { get; set; }
}

public class AiChatResponse
{
    public string Answer { get; set; }
    public string ConversationId { get; set; }
    public string TraceId { get; set; }
    public int TranscriptBytes { get; set; }
    public int TranscriptLimit { get; set; }
    public bool RequiresNewConversation { get; set; }
    public bool RequiresBugReport { get; set; }
    public string Error { get; set; }
    public AiQuota Quota { get; set; }
    public string DataNotice { get; set; }
}

public class AiQuota
{
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public DateTimeOffset ResetsAt { get; set; }
    public string Tier { get; set; } = "anonymous";
}

public record KnowledgeResult(string Title, string Url, string Content, string Source, double Score);

public record AiChatResult(
    string Answer,
    string ConversationId,
    int TranscriptBytes,
    int TranscriptLimit,
    bool RequiresNewConversation,
    bool RequiresBugReport);

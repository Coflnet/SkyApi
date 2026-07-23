using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.DiscordBot.Client.Api;
using Coflnet.Sky.Api.Helper;
using Coflnet.Sky.Api.Models.Ai;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Providing general info about the project.
/// </summary>
[ApiController]
[Route("api/data")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class InfoController : ControllerBase
{
    private const string AiDataNotice = "AI conversations may be reviewed to improve the system and may be processed overseas. Do not share personal or sensitive information.";

    [HttpGet("updates/{year}/{month}")]
    [ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IEnumerable<DiscordBot.Client.Model.DiscordMessage>> GetUpdates(int year, int month, [FromServices] IMessageApi messageApi)
    {
        if (year < 2022 || year > DateTime.UtcNow.Year)
            throw new ArgumentOutOfRangeException(nameof(year), "Year must be between 2022 and the current year.");
        if (month < 1 || month > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

        var dateTime = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return await messageApi.GetMessagesAsync("devlog", dateTime);
    }

    /// <summary>
    /// Answers a conversational question using SkyCofl knowledge and read-only API tools.
    /// At most the five most recent user/assistant rounds are accepted.
    /// </summary>
    [HttpPost("ai")]
    [ServiceFilter(typeof(AiRateLimitFilter))]
    public async Task<ActionResult<AiChatResponse>> Chat(
        [FromBody] AiChatRequest request,
        [FromServices] DeepSeekChatService chatService,
        CancellationToken cancellationToken)
    {
        var quota = HttpContext.Items[AiRateLimitFilter.QuotaItemKey] as AiQuota;
        var owner = HttpContext.Items[AiRateLimitFilter.IdentityItemKey] as string ?? $"request:{HttpContext.TraceIdentifier}";
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        Response.Headers["X-Trace-Id"] = traceId;
        Activity.Current?.SetTag("ai.trace_id", traceId);
        Activity.Current?.SetTag("ai.tier", quota?.Tier ?? "anonymous");

        try
        {
            var result = await chatService.ChatAsync(request, owner, quota?.Tier ?? "anonymous", traceId, cancellationToken);
            Activity.Current?.SetTag("ai.conversation_id", result.ConversationId);
            Activity.Current?.SetTag("ai.transcript_bytes", result.TranscriptBytes);
            Activity.Current?.SetTag("ai.requires_bug_report", result.RequiresBugReport);
            if (result.RequiresBugReport)
                HttpContext.Items[AiRateLimitFilter.RefundItemKey] = true;
            return Ok(new AiChatResponse
            {
                Answer = result.Answer,
                ConversationId = result.ConversationId,
                TraceId = traceId,
                TranscriptBytes = result.TranscriptBytes,
                TranscriptLimit = result.TranscriptLimit,
                RequiresNewConversation = result.RequiresNewConversation,
                RequiresBugReport = result.RequiresBugReport,
                Quota = quota ?? new AiQuota(),
                DataNotice = AiDataNotice
            });
        }
        catch (AiConversationLimitException ex)
        {
            HttpContext.Items[AiRateLimitFilter.RefundItemKey] = true;
            return Conflict(new AiChatResponse
            {
                Error = "conversation_limit_reached",
                Answer = ex.Message,
                ConversationId = ex.ConversationId,
                TraceId = traceId,
                TranscriptBytes = ex.TranscriptBytes,
                TranscriptLimit = ex.Limit,
                RequiresNewConversation = true,
                Quota = quota ?? new AiQuota(),
                DataNotice = AiDataNotice
            });
        }
        catch (AiConversationBusyException ex)
        {
            HttpContext.Items[AiRateLimitFilter.RefundItemKey] = true;
            return Conflict(new AiChatResponse
            {
                Error = "conversation_busy",
                Answer = ex.Message,
                ConversationId = ex.ConversationId,
                TraceId = traceId,
                Quota = quota ?? new AiQuota(),
                DataNotice = AiDataNotice
            });
        }
        catch (AiConversationAccessException)
        {
            HttpContext.Items[AiRateLimitFilter.RefundItemKey] = true;
            return NotFound();
        }
    }

    [HttpDelete("ai/{conversationId}")]
    public async Task<IActionResult> ClearAiConversation(
        string conversationId,
        [FromServices] DeepSeekChatService chatService,
        [FromServices] PremiumTierService premiumTierService)
    {
        var access = await premiumTierService.GetUserAndTier(this);
        var owner = access.user != null
            ? $"user:{access.user.Id}"
            : $"ip:{AiRateLimitFilter.GetClientIp(HttpContext) ?? "unknown"}";
        try
        {
            await chatService.ClearAsync(conversationId, owner);
            return NoContent();
        }
        catch (AiConversationAccessException)
        {
            return NotFound();
        }
    }

    [HttpPost("ai/knowledge/refresh")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> RefreshKnowledge(
        [FromQuery] string revision,
        [FromHeader(Name = "X-Knowledge-Refresh-Token")] string token,
        [FromServices] IConfiguration configuration,
        [FromServices] KnowledgeService knowledge,
        CancellationToken cancellationToken)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(configuration["KNOWLEDGE_REFRESH_TOKEN"] ?? "");
        var suppliedBytes = Encoding.UTF8.GetBytes(token ?? "");
        if (expectedBytes.Length == 0 || expectedBytes.Length != suppliedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(revision) || !Regex.IsMatch(revision, "^[a-f0-9]{40}$", RegexOptions.IgnoreCase))
            return BadRequest("revision must be a full Git commit SHA");

        var normalizedRevision = revision.ToLowerInvariant();
        var indexed = await knowledge.RefreshAsync(normalizedRevision, cancellationToken);
        return Ok(new { indexed, revision = normalizedRevision });
    }

    /// <summary>
    /// Legacy single-question compatibility endpoint. New clients should use the POST conversation endpoint.
    /// </summary>
    [HttpGet("ai")]
    [ServiceFilter(typeof(AiRateLimitFilter))]
    [Obsolete("Use POST /api/data/ai")]
    public async Task<string> GetAiInfo(
        string prompt,
        [FromServices] DeepSeekChatService chatService,
        CancellationToken cancellationToken)
    {
        var quota = HttpContext.Items[AiRateLimitFilter.QuotaItemKey] as AiQuota;
        var owner = HttpContext.Items[AiRateLimitFilter.IdentityItemKey] as string ?? $"request:{HttpContext.TraceIdentifier}";
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var result = await chatService.ChatAsync(new AiChatRequest
        {
            Message = prompt
        }, owner, quota?.Tier ?? "anonymous", traceId, cancellationToken);
        if (result.RequiresBugReport)
            HttpContext.Items[AiRateLimitFilter.RefundItemKey] = true;
        return result.Answer;
    }
}

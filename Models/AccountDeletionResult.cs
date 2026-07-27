using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Response returned for a processed account deletion request.
/// Documents explicitly what was (and was not) erased, so callers never mistake
/// a partial cascade for full erasure.
/// </summary>
public class AccountDeletionResult
{
    /// <summary>
    /// The (now deleted) numeric/google id of the user the request was processed for.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Human readable summary of what happened.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Data that was deleted or anonymized as part of this request.
    /// </summary>
    public List<string> Erased { get; set; } = new();

    /// <summary>
    /// Data that is intentionally NOT covered by this endpoint, with the reason why.
    /// </summary>
    public List<string> Retained { get; set; } = new();
}

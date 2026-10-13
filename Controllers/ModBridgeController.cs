using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Runs allowlisted chat commands in the caller's connected minecraft mod
/// </summary>
[ApiController]
[Route("api/mod")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ModBridgeController : ControllerBase
{
    private const int RateLimitCount = 10;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(10);

    private readonly IModUserResolver resolver;
    private readonly IModUserPublisher publisher;
    private readonly IBoosterCookieService cookies;
    private readonly IPendingBazaarSearchService pending;
    private readonly SettingsService settingsService;
    private readonly IMemoryCache cache;
    private readonly GoalService goals;

    /// <summary>Creates a new instance.</summary>
    public ModBridgeController(IModUserResolver resolver, IModUserPublisher publisher, IBoosterCookieService cookies,
        IPendingBazaarSearchService pending, SettingsService settingsService, IMemoryCache cache, GoalService goals)
    {
        this.resolver = resolver;
        this.publisher = publisher;
        this.cookies = cookies;
        this.pending = pending;
        this.settingsService = settingsService;
        this.cache = cache;
        this.goals = goals;
    }

    /// <summary>
    /// Executes an allowlisted chat command in the minecraft mod of the authenticated user.
    /// Requires a google login or an api key. Without a booster cookie `/bz item` becomes a queued bazaar search.
    /// </summary>
    /// <param name="request">The command to run</param>
    /// <returns>Whether a mod session received the command</returns>
    [HttpPost("execute")]
    public async Task<ModExecuteResponse> Execute([FromBody] ModExecuteRequest request)
    {
        var user = await resolver.Resolve(this);
        EnforceRateLimit(user.UserId);

        if (!ModCommandAllowlist.TryNormalize(request?.Command, out var command, out var kind))
            throw new CoflnetException("command_not_allowed", "This command can't be executed remotely");

        string fallback = null;
        if (kind == CommandKind.Bazaar)
        {
            var setting = await settingsService.GetCurrentValue<DescriptionSetting>(user.UserId, "description", () => DescriptionSetting.Default);
            var state = await cookies.Get(user.MinecraftName);
            if (!BoosterCookieService.HasCookie(setting, state))
            {
                var name = ModCommandAllowlist.BazaarItemName(command);
                await pending.Add(user.UserId, name, null, null);
                command = $"/cofl bazaarsearch {name}";
                fallback = "no_cookie_pending_search";
            }
        }

        var receivers = await publisher.ExecuteForUser(user.UserId, command, null);
        if (receivers <= 0 && fallback == null)
            fallback = "not_connected";
        return new ModExecuteResponse { Executed = receivers > 0, Receivers = receivers, Command = command, Fallback = fallback };
    }

    /// <summary>
    /// Lists the goals of the authenticated user with their current progress.
    /// </summary>
    /// <returns>The goals and which one is focused</returns>
    [HttpGet("goals")]
    public async Task<ModGoalsResponse> GetGoals()
    {
        var user = await resolver.Resolve(this);
        var (list, progress) = await goals.GetWithProgress(user.UserId);
        return new ModGoalsResponse
        {
            FocusId = list.FocusId,
            Goals = list.Goals.Select(g => ToDto(g, progress.GetValueOrDefault(g.Id))).ToList()
        };
    }

    /// <summary>
    /// Creates a goal for crafting or forging an item. The first goal becomes the focused one.
    /// </summary>
    /// <param name="request">What to create</param>
    /// <returns>The created goal</returns>
    [HttpPost("goals")]
    public async Task<ModGoalProgress> CreateGoal([FromBody] ModGoalCreateRequest request)
    {
        var user = await resolver.Resolve(this);
        var kind = request?.Kind?.ToLowerInvariant() switch
        {
            "craft" => StepKind.Craft,
            "forge" => StepKind.Forge,
            _ => throw new CoflnetException("invalid_goal_kind", "Kind has to be craft or forge")
        };
        if (string.IsNullOrWhiteSpace(request.ItemTag))
            throw new CoflnetException("invalid_item_tag", "ItemTag is required");
        var quantity = Math.Clamp(request.Quantity, 1, 100_000);
        Goal goal;
        try
        {
            goal = await goals.Add(user.UserId, kind, request.ItemTag, quantity, request.Sell, "mod");
        }
        catch (ArgumentException e)
        {
            throw new CoflnetException("not_a_forge_item", $"{request.ItemTag} can't be forged: {e.Message}");
        }
        var (_, progress) = await goals.GetWithProgress(user.UserId);
        return ToDto(goal, progress.GetValueOrDefault(goal.Id));
    }

    /// <summary>Deletes a goal.</summary>
    /// <param name="id">Goal id</param>
    [HttpDelete("goals/{id}")]
    public async Task<IActionResult> DeleteGoal(string id)
    {
        var user = await resolver.Resolve(this);
        return await goals.Remove(user.UserId, id) ? NoContent() : NotFound();
    }

    /// <summary>Makes a goal the one shown in the in-game hud.</summary>
    /// <param name="id">Goal id</param>
    [HttpPost("goals/{id}/focus")]
    public async Task<IActionResult> FocusGoal(string id)
    {
        var user = await resolver.Resolve(this);
        return await goals.Focus(user.UserId, id) ? NoContent() : NotFound();
    }

    /// <summary>Clears the recorded progress of a goal.</summary>
    /// <param name="id">Goal id</param>
    [HttpPost("goals/{id}/reset")]
    public async Task<IActionResult> ResetGoal(string id)
    {
        var user = await resolver.Resolve(this);
        return await goals.Reset(user.UserId, id) ? NoContent() : NotFound();
    }

    private ModGoalProgress ToDto(Goal goal, GoalProgress progress)
    {
        progress ??= GoalProgressEvaluator.Evaluate(goal, new Dictionary<string, long>(), new Dictionary<string, long>(), new HashSet<string>(), DateTime.UtcNow);
        return new ModGoalProgress
        {
            Id = goal.Id,
            Title = goal.Title,
            RootItemTag = goal.RootItemTag,
            Quantity = goal.Quantity,
            CreatedAt = goal.CreatedAt,
            Source = goal.Source,
            Done = progress.Done,
            Total = progress.Total,
            Root = ToDto(goal.Root, progress)
        };
    }

    private ModGoalStep ToDto(GoalStep step, GoalProgress progress)
    {
        if (step == null)
            return null;
        return new ModGoalStep
        {
            Id = step.Id,
            Kind = step.Kind,
            ItemTag = step.ItemTag,
            ItemName = goals.GetName(step.ItemTag),
            Quantity = step.Quantity,
            Status = progress.Status.TryGetValue(step.Id, out var status) ? status : StepStatus.Todo,
            Children = step.Children.Select(c => ToDto(c, progress)).ToList()
        };
    }

    private void EnforceRateLimit(string userId)
    {
        var bucket = cache.GetOrCreate("modexec:" + userId, e =>
        {
            e.SlidingExpiration = RateLimitWindow * 2;
            return new Queue<DateTime>();
        });
        lock (bucket)
        {
            var now = DateTime.UtcNow;
            while (bucket.Count > 0 && now - bucket.Peek() > RateLimitWindow)
                bucket.Dequeue();
            if (bucket.Count >= RateLimitCount)
                throw new CoflnetException("rate_limited", $"At most {RateLimitCount} commands per {RateLimitWindow.TotalSeconds} seconds");
            bucket.Enqueue(now);
        }
    }
}

/// <summary>Request to run a command in the mod</summary>
public class ModExecuteRequest
{
    /// <summary>The chat command, e.g. `/bz Enchanted Diamond`. Only a small allowlist is accepted.</summary>
    public string Command { get; set; }
}

/// <summary>Result of a remote command execution</summary>
public class ModExecuteResponse
{
    /// <summary>True if at least one mod session received the command</summary>
    public bool Executed { get; set; }
    /// <summary>Number of receiving servers</summary>
    public long Receivers { get; set; }
    /// <summary>The command that was sent (may differ from the requested one, see fallback)</summary>
    public string Command { get; set; }
    /// <summary>`no_cookie_pending_search` when /bz was replaced by a queued search, `not_connected` when no mod session is online</summary>
    public string Fallback { get; set; }
}

/// <summary>Goals of a user</summary>
public class ModGoalsResponse
{
    /// <summary>Id of the goal shown in the hud</summary>
    public string FocusId { get; set; }
    /// <summary>All goals with their progress</summary>
    public List<ModGoalProgress> Goals { get; set; } = new();
}

/// <summary>Request to create a goal</summary>
public class ModGoalCreateRequest
{
    /// <summary>`craft` or `forge`</summary>
    public string Kind { get; set; }
    /// <summary>Tag of the item to obtain</summary>
    public string ItemTag { get; set; }
    /// <summary>How many to make</summary>
    public long Quantity { get; set; } = 1;
    /// <summary>Add a final step to sell the result</summary>
    public bool Sell { get; set; }
}

/// <summary>A goal with its progress</summary>
public class ModGoalProgress
{
    /// <summary>Goal id</summary>
    public string Id { get; set; }
    /// <summary>Display title</summary>
    public string Title { get; set; }
    /// <summary>Tag of the target item</summary>
    public string RootItemTag { get; set; }
    /// <summary>Target quantity</summary>
    public long Quantity { get; set; }
    /// <summary>When the goal was created</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>`website` or `mod`</summary>
    public string Source { get; set; }
    /// <summary>Completed steps</summary>
    public int Done { get; set; }
    /// <summary>Total steps</summary>
    public int Total { get; set; }
    /// <summary>Step tree</summary>
    public ModGoalStep Root { get; set; }
}

/// <summary>One step of a goal</summary>
public class ModGoalStep
{
    /// <summary>Step id</summary>
    public string Id { get; set; }
    /// <summary>What kind of step this is (camelCase: buyOrder, instaBuy, npc, auction, craft, forge, sell)</summary>
    public StepKind Kind { get; set; }
    /// <summary>Item tag</summary>
    public string ItemTag { get; set; }
    /// <summary>Item display name</summary>
    public string ItemName { get; set; }
    /// <summary>Needed amount</summary>
    public long Quantity { get; set; }
    /// <summary>Current status (camelCase: todo, ordered, ready, forging, done)</summary>
    public StepStatus Status { get; set; }
    /// <summary>Sub steps</summary>
    public List<ModGoalStep> Children { get; set; } = new();
}

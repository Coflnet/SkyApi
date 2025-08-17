using System.Linq;
using System.Threading.Tasks;
using Coflnet.Leaderboard.Client.Api;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName.Client.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller;

[ApiController]
[Route("api/leaderbaord")]
public class LeaderboardController : ControllerBase
{
    private readonly ILogger<LeaderboardController> logger;
    private readonly IScoresApi scoresApi;
    private readonly PremiumTierService premiumTierService;
    private readonly IPlayerNameApi playerNameApi;

    public LeaderboardController(ILogger<LeaderboardController> logger, IScoresApi scoresApi, IPlayerNameApi playerNameApi, PremiumTierService premiumTierService)
    {
        this.logger = logger;
        this.scoresApi = scoresApi;
        this.playerNameApi = playerNameApi;
        this.premiumTierService = premiumTierService;
    }

    /// <summary>
    /// Returns the leaderboard for the given week
    /// </summary>
    [Route("profit")]
    [HttpGet]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<List<LeaderboardEntry>> GetProfitLeaderboard(int weekOffset = 0)
    {
        if (!await premiumTierService.HasPremiumPlus(this))
            throw new CoflnetException("prem+_required", "This endpoint is only available for Premium+ users");
        var entries = await scoresApi.ScoresLeaderboardSlugGetAsync(GetBoardName(weekOffset), 0, 50);
        var names = await playerNameApi.PlayerNameNamesBatchPostAsync(entries.Select(e => e.UserId).ToList());
        return entries.Select(e => new LeaderboardEntry
        {
            PlayerUuid = e.UserId,
            PlayerName = names.FirstOrDefault(n => n.Key == e.UserId).Value,
            Score = e.Score,
            TimeStamp = e.TimeStamp,
        }).ToList();
    }

    string GetBoardName(int weekOffset)
    {
        return $"sky-flippers-{DateTime.UtcNow.AddDays(weekOffset * -7).RoundDown(TimeSpan.FromDays(7)):yyyy-MM-dd}";
    }

    public class LeaderboardEntry
    {
        public string PlayerUuid { get; set; }
        public string PlayerName { get; set; }
        public long Score { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}

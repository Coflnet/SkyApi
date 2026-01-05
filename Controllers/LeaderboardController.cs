using System.Linq;
using System.Threading.Tasks;
using Coflnet.Leaderboard.Client.Api;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName.Client.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller;

[ApiController]
[Route("api/leaderboard")]
public class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService scoresApi;
    private readonly PremiumTierService premiumTierService;

    public LeaderboardController(ILeaderboardService scoresApi,  PremiumTierService premiumTierService)
    {
        this.scoresApi = scoresApi;
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
            throw new CoflnetException("no_premium_plus", "This endpoint is only available for Premium+ users");
        var entries = await scoresApi.GetTopFlippers(GetBoardName(),DateTime.UtcNow.AddDays(weekOffset * -7) , 0, 50);
        return entries.Select(e => new LeaderboardEntry
        {
            PlayerUuid = e.PlayerId,
            PlayerName = e.PlayerName,
            Score = e.Score,
            TimeStamp = e.Timestamp,
        }).ToList();
    }

    string GetBoardName()
    {
        return $"sky-flippers";
    }

    public class LeaderboardEntry
    {
        public string PlayerUuid { get; set; }
        public string PlayerName { get; set; }
        public long Score { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}

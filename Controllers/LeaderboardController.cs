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

/// <summary>Provides leaderboard endpoints.</summary>
[ApiController]
[Route("api/leaderboard")]
public class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService scoresApi;
    private readonly PremiumTierService premiumTierService;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardController"/> class.</summary>
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
        // GetTopFlippers takes a page number, not a raw skip - offset is computed as page * count downstream
        int page = 0, take = 50;
        if (weekOffset == -1)
        {
            // public last week places 100-150 (page 2 * take 50 = offset 100)
            weekOffset = 1;
            page = 2; take = 50;
        }
        else if (!await premiumTierService.HasPremiumPlus(this))
            throw new CoflnetException("no_premium_plus", "This endpoint is only available for Premium+ users");
        var entries = await scoresApi.GetTopFlippers(GetBoardName(),DateTime.UtcNow.AddDays(weekOffset * -7) , page, take);
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

    /// <summary>Represents a leaderboard entry.</summary>
    public class LeaderboardEntry
    {
        /// <summary>Gets or sets the player uuid.</summary>
        public string PlayerUuid { get; set; }
        /// <summary>Gets or sets the player name.</summary>
        public string PlayerName { get; set; }
        /// <summary>Gets or sets the score.</summary>
        public long Score { get; set; }
        /// <summary>Gets or sets the time stamp.</summary>
        public DateTime TimeStamp { get; set; }
    }
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.DiscordBot.Client.Api;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Providing general info about the project
/// </summary>
[ApiController]
[Route("api/data")]
[ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.None, NoStore = true)]
public class InfoController : ControllerBase
{
    [HttpGet("updates/{year}/{month}")]
    public async Task<IEnumerable<DiscordBot.Client.Model.DiscordMessage>> GetUpdates(int year, int month, [FromServices] IMessageApi sniperApi)
    {
        if (year < 2022 || year > DateTime.UtcNow.Year)
            throw new ArgumentOutOfRangeException(nameof(year), "Year must be between 2022 and the current year.");
        if (month < 1 || month > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

        var dateTime = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return await sniperApi.GetMessagesAsync("devlog", dateTime);
    }
}

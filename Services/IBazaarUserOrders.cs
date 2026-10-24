using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;

namespace Coflnet.Sky.Api.Services;

/// <summary>Reads the tracked bazaar orders of a user.</summary>
public interface IBazaarUserOrders
{
    /// <summary>Returns only orders belonging to the user and Minecraft player.</summary>
    Task<List<BazaarPlayerOrder>> Get(string userId, string playerName);
}

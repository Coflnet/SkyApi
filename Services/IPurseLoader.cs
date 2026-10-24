using System;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Api.Services;

/// <summary>Loads the purse of a player.</summary>
public interface IPurseLoader
{
    /// <summary>Purse in coins, null when it is unknown.</summary>
    Task<double?> LoadPurse(string playerName);
}

/// <summary>Reads the purse from player state, a failed load is reported as unknown.</summary>
public class PurseLoader(IPlayerStateApi playerState) : IPurseLoader
{
    /// <inheritdoc/>
    public async Task<double?> LoadPurse(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return null;
        try
        {
            var currencies = await playerState.PlayerStatePlayerIdCurrenciesGetAsync(playerName);
            return currencies == null || currencies.Purse <= 0 ? null : (double)currencies.Purse;
        }
        catch (Exception)
        {
            // an unknown purse only means the caller has to ask for a budget
            return null;
        }
    }
}

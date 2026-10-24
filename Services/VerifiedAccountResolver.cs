using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName;

namespace Coflnet.Sky.Api.Services;

/// <summary>Finds the Minecraft account a user verified with the mod.</summary>
public interface IVerifiedAccountResolver
{
    /// <summary>Uuid of the most recently used verified account.</summary>
    /// <exception cref="CoflnetException">no_verified_account when the user has none</exception>
    Task<string> GetVerifiedUuid(GoogleUser user);

    /// <summary>Current name of the account with the given uuid, null when unknown.</summary>
    Task<string> GetName(string uuid);
}

/// <summary>Resolves the verified Minecraft account of a user through the connect service.</summary>
public class VerifiedAccountResolver : IVerifiedAccountResolver
{
    private readonly McConnect.Api.IConnectApi connectApi;
    private readonly PlayerNameService names;

    /// <summary>Creates a resolver, the name service is only needed for <see cref="GetName"/>.</summary>
    public VerifiedAccountResolver(McConnect.Api.IConnectApi connectApi, PlayerNameService names = null)
    {
        this.connectApi = connectApi;
        this.names = names;
    }

    /// <inheritdoc/>
    public async Task<string> GetVerifiedUuid(GoogleUser user)
    {
        var mcUser = await connectApi.ConnectUserUserIdGetAsync(user.Id.ToString());
        var uuid = mcUser.Accounts.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault()?.AccountUuid;
        if (uuid == null)
            throw new CoflnetException("no_verified_account", "You need to verify on a minecraft account with our mod first");

        return uuid.Trim('"');
    }

    /// <inheritdoc/>
    public Task<string> GetName(string uuid) => names == null ? Task.FromResult<string>(null) : names.GetName(uuid);
}

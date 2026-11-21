using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.McConnect.Api;
using Coflnet.Sky.McConnect.Model;
using Coflnet.Sky.PlayerName;

#nullable enable annotations

namespace Coflnet.Sky.Api.Services;

/// <summary>Resolves a short, privacy-safe account label for Play checkout.</summary>
public class PlayCheckoutAccountLabel
{
    private readonly Func<string, CancellationToken, Task<User>> getUser;
    private readonly Func<string, Task<string>> getName;
    private readonly TimeSpan timeout;

    /// <summary>Creates an account label resolver.</summary>
    public PlayCheckoutAccountLabel(IConnectApi connectApi, PlayerNameService playerNames)
        : this((userId, token) => connectApi.ConnectUserUserIdGetAsync(userId, cancellationToken: token),
            playerNames.GetName, TimeSpan.FromSeconds(2)) { }

    internal PlayCheckoutAccountLabel(Func<string, CancellationToken, Task<User>> getUser,
        Func<string, Task<string>> getName, TimeSpan timeout)
    {
        this.getUser = getUser;
        this.getName = getName;
        this.timeout = timeout;
    }

    /// <summary>Uses the latest verified Minecraft account, then masked email, then a generic label.</summary>
    public async Task<string> GetAsync(string userId, string? email)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            var name = await GetMinecraftName(userId, cancellation.Token).WaitAsync(cancellation.Token);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch (Exception)
        {
            // A display label must not prevent checkout when account services are unavailable.
        }
        var safeEmail = Clean(email, int.MaxValue);
        var at = safeEmail.IndexOf('@');
        return at > 0 && at < safeEmail.Length - 1 && at == safeEmail.LastIndexOf('@')
            ? Clean($"{Rune.GetRuneAt(safeEmail, 0)}***{safeEmail[at..]}")
            : "your account";
    }

    private async Task<string?> GetMinecraftName(string userId, CancellationToken cancellation)
    {
        var user = await getUser(userId, cancellation);
        cancellation.ThrowIfCancellationRequested();
        var uuid = user?.Accounts?.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt)
            .FirstOrDefault()?.AccountUuid?.Trim('"');
        return string.IsNullOrWhiteSpace(uuid) ? null : Clean(await getName(uuid));
    }

    private static string Clean(string? value, int maxLength = 80) => string.Concat((value ?? "").EnumerateRunes()
        .Where(rune => Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
        .Take(maxLength).Select(rune => rune.ToString())).Trim();
}

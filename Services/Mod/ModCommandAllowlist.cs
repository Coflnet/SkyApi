using System.Linq;
using System.Text.RegularExpressions;

namespace Coflnet.Sky.Api.Services.Mod;

/// <summary>The kind of an allowed chat command.</summary>
public enum CommandKind
{
    /// <summary>/viewauction uuid</summary>
    ViewAuction,
    /// <summary>/bz item</summary>
    Bazaar,
    /// <summary>/recipe item</summary>
    Recipe,
    /// <summary>/ah [player]</summary>
    AuctionHouse,
    /// <summary>/cofl bazaarsearch item</summary>
    BazaarSearch,
    /// <summary>/cofl bzorder [search] amount item</summary>
    BazaarOrder,
    /// <summary>/cofl goal ...</summary>
    Goal,
    /// <summary>/warp forge</summary>
    WarpForge
}

/// <summary>Strict allowlist of chat commands that may be executed in a user's mod.</summary>
public static class ModCommandAllowlist
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const string Name = @"[A-Za-z0-9'\- ]{1,48}";

    private static readonly (CommandKind Kind, Regex Pattern)[] Rules =
    {
        (CommandKind.ViewAuction, new Regex(@"^/viewauction ([0-9a-f]{32})$", Opt)),
        (CommandKind.Bazaar, new Regex($@"^/bz ({Name})$", Opt)),
        (CommandKind.Recipe, new Regex($@"^/recipe ({Name})$", Opt)),
        (CommandKind.AuctionHouse, new Regex(@"^/ah( [A-Za-z0-9_]{1,16})?$", Opt)),
        (CommandKind.BazaarSearch, new Regex($@"^/cofl bazaarsearch ({Name})$", Opt)),
        (CommandKind.BazaarOrder, new Regex($@"^/cofl bzorder( search)? ([0-9]{{1,6}}) ({Name})$", Opt)),
        (CommandKind.Goal, new Regex(@"^/cofl goal [a-z]{3,8}( [A-Za-z0-9_\-]{1,40})?( [0-9]{1,6})?$", Opt)),
        (CommandKind.WarpForge, new Regex(@"^/warp forge$", Opt)),
    };

    /// <summary>
    /// Validates <paramref name="raw"/> and returns the rebuilt canonical command, never the raw input.
    /// </summary>
    public static bool TryNormalize(string raw, out string command, out CommandKind kind)
    {
        command = null;
        kind = default;
        if (raw == null)
            return false;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 100)
            return false;
        if (trimmed.Any(ch => char.IsControl(ch) || ch == '§'))
            return false;
        var collapsed = Regex.Replace(trimmed, @" {2,}", " ");
        foreach (var (ruleKind, pattern) in Rules)
        {
            var match = pattern.Match(collapsed);
            if (!match.Success)
                continue;
            kind = ruleKind;
            command = Rebuild(ruleKind, match);
            return true;
        }
        return false;
    }

    /// <summary>Gets the item name of a normalized <see cref="CommandKind.Bazaar"/> command.</summary>
    public static string BazaarItemName(string normalizedCommand)
        => normalizedCommand.Length > 4 ? normalizedCommand.Substring(4).Trim() : null;

    private static string Rebuild(CommandKind kind, Match m)
    {
        var parts = m.Value.Split(' ');
        switch (kind)
        {
            case CommandKind.ViewAuction:
                return "/viewauction " + m.Groups[1].Value.ToLowerInvariant();
            case CommandKind.Bazaar:
                return "/bz " + m.Groups[1].Value.Trim();
            case CommandKind.Recipe:
                return "/recipe " + m.Groups[1].Value.Trim();
            case CommandKind.AuctionHouse:
                return "/ah" + m.Groups[1].Value;
            case CommandKind.BazaarSearch:
                return "/cofl bazaarsearch " + m.Groups[1].Value.Trim();
            case CommandKind.BazaarOrder:
                return "/cofl bzorder" + (m.Groups[1].Success ? " search" : "") + " " + m.Groups[2].Value + " " + m.Groups[3].Value.Trim();
            case CommandKind.Goal:
                return "/cofl goal " + string.Join(' ', parts.Skip(2).Select((p, i) => i == 0 ? p.ToLowerInvariant() : p));
            default:
                return "/warp forge";
        }
    }
}

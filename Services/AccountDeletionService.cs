using System;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services;

/// <summary>
/// Thin seam around <see cref="Commands.Shared.IndexerClient"/>'s static account deletion call,
/// so <see cref="AccountDeletionService"/> can be unit tested with a fake instead of hitting the network.
/// </summary>
public interface IAccountDeletionClient
{
    /// <summary>
    /// Triggers the cascading account deletion on SkyIndexer.
    /// </summary>
    /// <param name="email">the user's email</param>
    /// <param name="googleId">the user's google id</param>
    /// <returns>the id of the user that was deleted</returns>
    Task<string> DeleteUser(string email, string googleId);
}

/// <inheritdoc/>
public class IndexerAccountDeletionClient : IAccountDeletionClient
{
    /// <inheritdoc/>
    public Task<string> DeleteUser(string email, string googleId)
        => Commands.Shared.IndexerClient.DeleteUser(email, googleId);
}

/// <summary>
/// Handles account deletion requests for <see cref="Controller.UserController"/>.
///
/// SkyApi itself holds no user data outside of what is proxied through other Coflnet services, so this
/// service delegates the actual erasure to SkyIndexer's <c>DELETE /User/{email}</c> endpoint (via
/// <see cref="IAccountDeletionClient"/>), which already cascades into:
///  - the SkyIndexer user record itself (the google/minecraft account link, <c>HypixelContext.Users</c>)
///  - Coflnet.Sky.McConnect (connected minecraft accounts are unlinked/unverified)
///  - Coflnet.Sky.PlayerState (per connected minecraft account state)
///  - Coflnet.Sky.Settings (all settings incl. the privacy settings this controller exposes)
///
/// What is explicitly NOT covered by this call (and therefore not by this endpoint):
///  - Coflnet.Payments purchase/transaction history - retained, purchase records are kept where
///    legally required (invoicing/tax obligations)
///  - other microservices that key data by user id but are not wired into the SkyIndexer cascade yet
///    (e.g. flip tracking history, auctions/trade history, referral records, subscriptions, leaderboard
///    scores). Those still reference the (now anonymous) user id after this call returns.
///
/// Because of that gap this never claims full erasure: callers get a 202 Accepted with an explicit
/// breakdown of what was erased vs retained.
/// </summary>
public class AccountDeletionService
{
    private readonly IAccountDeletionClient deletionClient;

    /// <summary>
    /// Creates a new instance of <see cref="AccountDeletionService"/>
    /// </summary>
    /// <param name="deletionClient"></param>
    public AccountDeletionService(IAccountDeletionClient deletionClient)
    {
        this.deletionClient = deletionClient;
    }

    /// <summary>
    /// Processes an account deletion request for the given user.
    /// </summary>
    /// <param name="user">the authenticated user requesting deletion</param>
    /// <returns>a summary of what was erased and what was retained</returns>
    public async Task<AccountDeletionResult> DeleteAccount(GoogleUser user)
    {
        if (user == null)
            throw new ArgumentNullException(nameof(user));

        var deletedUserId = await deletionClient.DeleteUser(user.Email, user.GoogleId);

        return new AccountDeletionResult
        {
            UserId = deletedUserId,
            Message = "Your account deletion request has been processed. Some data is retained where " +
                "legally required or not yet covered by this endpoint - see 'retained' for details.",
            Erased =
            {
                "account record (google/minecraft account link)",
                "connected minecraft accounts (unlinked/unverified)",
                "player state of connected minecraft accounts",
                "settings, including privacy settings"
            },
            Retained =
            {
                "purchase/transaction history (Coflnet.Payments) - kept where legally required",
                "flip tracking, auction/trade, referral, subscription and leaderboard history in " +
                    "other services that are not yet wired into this deletion cascade"
            }
        };
    }
}

using System.Threading.Tasks;
using Coflnet.Sky.Api;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Coflnet.Sky.Api.Controller
{
#nullable enable
    /// <summary>
    /// Endpoints for flips
    /// </summary>
    [ApiController]
    [Route("api/user")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class UserController : ControllerBase
    {
        private GoogletokenService tokenService;
        private SettingsService settingsService;
        private AccountDeletionService accountDeletionService;

        /// <summary>
        /// Creates a new instance of <see cref="UserController"/>
        /// </summary>
        /// <param name="premiumService"></param>
        /// <param name="settingsService"></param>
        /// <param name="accountDeletionService"></param>
        public UserController(GoogletokenService premiumService, SettingsService settingsService, AccountDeletionService accountDeletionService)
        {
            this.tokenService = premiumService;
            this.settingsService = settingsService;
            this.accountDeletionService = accountDeletionService;
        }

        /// <summary>
        /// Get the users privacy settings (requires google token)
        /// </summary>
        /// <returns></returns>
        [Route("privacy")]
        [HttpGet]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<PrivacySettings>> GetPrivacySettings()
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            return await settingsService.GetCurrentValue(user.Id.ToString(), "privacySettings", () => new PrivacySettings()
            {
                CollectInventory = true,
                ExtendDescriptions = true,
                ChatRegex = "^(�r�eSell Offer|�r�6[Bazaar]|�r�cCancelled|�r�6Bazaar!|�r�eYou collected|�6[Auction]|�r�eBIN Auction started|�r�eYou �r�ccancelled|[Test]| - | + |Trade completed).*",
                CollectChat = true,
                CollectScoreboard = true,
                CollectChatClicks = true,
                CommandPrefixes = new string[] { "/cofl", "/colf", "/ch" },
                AutoStart = true
            });
        }

        /// <summary>
        /// Update users privacy settings (requires google token)
        /// </summary>
        /// <param name="settings">The new settings</param>
        /// <returns></returns>
        [Route("privacy")]
        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult> SetPrivacySettings(PrivacySettings settings)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            await settingsService.UpdateSetting(user.Id.ToString(), "privacySettings", settings);
            return Ok();
        }

        /// <summary>
        /// Deletes the caller's account (requires google token).
        /// See <see cref="AccountDeletionService"/> for exactly what this does and does not cover -
        /// it cascades what SkyApi and SkyIndexer can reach (account record, connected minecraft
        /// accounts, player state, settings incl. privacy settings), but purchase history is kept
        /// where legally required and some other Coflnet services are not wired into the cascade yet.
        /// Because of that this is never a guarantee of full erasure across every downstream service -
        /// the response lists what was erased and what is retained.
        /// </summary>
        /// <returns>202 Accepted with a breakdown of what was erased/retained</returns>
        [Route("me")]
        [HttpDelete]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<AccountDeletionResult>> DeleteAccount()
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            var result = await accountDeletionService.DeleteAccount(user);
            return Accepted(result);
        }

        private async Task<GoogleUser?> GetUserOrDefault()
        {
            if (!Request.Headers.TryGetValue("GoogleToken", out StringValues value)
                && !Request.Headers.TryGetValue("Authorization", out value))
                return null;
            return await tokenService.GetUserWithToken(value);
        }
    }
}


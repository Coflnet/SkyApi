using System;
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
        public UserController(
            GoogletokenService premiumService,
            SettingsService settingsService,
            AccountDeletionService accountDeletionService)
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
        /// Returns whether the authenticated user has accepted the current
        /// SkyCofl Agreement Root.
        /// </summary>
        [Route("terms")]
        [HttpGet]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<TermsStatus>> GetTermsStatus(
            [FromQuery] string locale = "en")
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            var acceptance = await GetCurrentAgreementAcceptance(user.Id);
            return Ok(TermsAcceptancePolicy.GetStatus(
                acceptance != null,
                acceptance?.AcceptedAtUtc,
                locale: locale));
        }

        /// <summary>
        /// Records an express acceptance of the exact current SkyCofl
        /// Agreement Root.
        /// </summary>
        [Route("terms")]
        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<TermsStatus>> AcceptTerms(
            AcceptTermsRequest request,
            [FromQuery] string locale = "en")
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            if (!TermsAcceptancePolicy.IsEffective())
                return Conflict(new
                {
                    slug = "terms_not_effective",
                    message = "This SkyCofl agreement cannot be accepted before its publication time.",
                    effectiveAtUtc = TermsAcceptancePolicy.CurrentVersionEffectiveAtUtc
                });
            var existing = await GetCurrentAgreementAcceptance(user.Id);
            if (!string.Equals(
                    request?.Hash,
                    TermsAcceptancePolicy.CurrentHash,
                    StringComparison.OrdinalIgnoreCase))
                return Conflict(new
                {
                    slug = "terms_version_changed",
                    message = "The SkyCofl agreement changed. Review the current version before accepting.",
                    current = TermsAcceptancePolicy.GetStatus(
                        existing != null,
                        existing?.AcceptedAtUtc,
                        locale: locale)
                });
            var source = $"web-premium-{TermsAcceptancePolicy.NormalizeLocale(locale)}";
            await UserService.Instance.AcceptAgreement(
                user.Id,
                TermsAcceptancePolicy.CurrentAgreementId,
                new TermsAcceptance(
                TermsAcceptancePolicy.CurrentVersion,
                TermsAcceptancePolicy.CurrentHash,
                DateTime.UtcNow,
                source));
            var acceptance = await GetCurrentAgreementAcceptance(user.Id);
            return Ok(TermsAcceptancePolicy.GetStatus(
                acceptance != null,
                acceptance?.AcceptedAtUtc,
                locale: locale));
        }

        private static Task<AgreementAcceptanceRecord?> GetCurrentAgreementAcceptance(int userId)
        {
            if (string.IsNullOrEmpty(TermsAcceptancePolicy.CurrentHash))
                return Task.FromResult<AgreementAcceptanceRecord?>(null);
            return UserService.Instance.GetAgreementAcceptance(
                userId,
                TermsAcceptancePolicy.CurrentAgreementId,
                TermsAcceptancePolicy.CurrentHash);
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

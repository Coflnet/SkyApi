using System.Threading.Tasks;
using Coflnet.Sky.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Coflnet.Sky.Api.Controller
{
    /// <summary>
    /// Endpoints for flips
    /// </summary>
    [ApiController]
    [Route("api/user")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class UserController : ControllerBase
    {
        private Sky.Api.PremiumService premiumService;
        private SettingsService settingsService;

        /// <summary>
        /// Creates a new instance of <see cref="UserController"/>
        /// </summary>
        /// <param name="premiumService"></param>
        /// <param name="settingsService"></param>
        public UserController(Sky.Api.PremiumService premiumService, SettingsService settingsService)
        {
            this.premiumService = premiumService;
            this.settingsService = settingsService;
        }

        /// <summary>
        /// Get the users privacy settings (requires google token)
        /// </summary>
        /// <returns></returns>
        [Route("privacy")]
        [HttpGet]
        public async Task<ActionResult<PrivacySettings>> GetPrivacySettings()
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");
            return await settingsService.GetCurrentValue<PrivacySettings>(user.Id.ToString(), "privacySettings", () => new PrivacySettings()
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
        public async Task<ActionResult> SetPrivacySettings(PrivacySettings settings)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");
            await settingsService.UpdateSetting<PrivacySettings>(user.Id.ToString(), "privacySettings", settings);
            return Ok();
        }

        private bool TryGetUser(out GoogleUser user)
        {
            user = default(GoogleUser);
            if (!Request.Headers.TryGetValue("GoogleToken", out StringValues value))
                return false;
            user = premiumService.GetUserWithToken(value);
            return true;
        }
    }
}


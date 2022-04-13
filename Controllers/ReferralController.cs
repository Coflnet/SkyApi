using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Referral.Client.Api;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Primitives;
using Coflnet.Sky.Api;
using HashidsNet;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Endpoints for related to paid services
    /// </summary>
    [ApiController]
    [Route("api")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class ReferralController : ControllerBase
    {
        private IReferralApi refApi;
        private PremiumService premiumService;
        Hashids hashids = new Hashids("simple salt", 6);

        public ReferralController(IReferralApi refApi, PremiumService premiumService)
        {
            this.refApi = refApi;
            this.premiumService = premiumService;
        }


        /// <summary>
        /// tells the backend that the user was referred by someone
        /// </summary>
        /// <returns></returns>
        [Route("referral/by")]
        [HttpGet]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> TopupOptions([FromBody] Argument args)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");
            await refApi.ReferralUserIdPostAsync(GetId(args.RefCode).ToString(), user.Id.ToString());
            return Ok();
        }

        public class Argument
        {
            public string RefCode { get; set; }
        }

        private int GetId(string referer)
        {
            return hashids.Decode(referer)[0];
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


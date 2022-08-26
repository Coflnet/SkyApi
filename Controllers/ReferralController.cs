using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Referral.Client.Api;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Primitives;
using Coflnet.Sky.Api;
using HashidsNet;
using System;
using System.Linq;
using Coflnet.Sky.Api.Models.Referral;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Referral.Client.Model;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Endpoints for related to paid services
    /// </summary>
    [ApiController]
    [Route("api/referral")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class ReferralController : ControllerBase
    {
        private IReferralApi refApi;
        private PremiumService premiumService;
        private HypixelContext db;
        Hashids hashids = new Hashids("simple salt", 6);

        /// <summary>
        /// Creates a new intance of <see cref="ReferralController"/>
        /// </summary>
        /// <param name="refApi"></param>
        /// <param name="premiumService"></param>
        /// <param name="db"></param>
        public ReferralController(IReferralApi refApi, PremiumService premiumService, HypixelContext db)
        {
            this.refApi = refApi;
            this.premiumService = premiumService;
            this.db = db;
            Console.WriteLine(GetId("ZQWKaQ"));
            Console.WriteLine(GetId("oD65nQ"));
            Console.WriteLine(GetId("9gKwV9"));
        }


        /// <summary>
        /// tells the backend that the user was referred by someone
        /// </summary>
        /// <returns></returns>
        [Route("referred/by")]
        [HttpPost]
        public async Task<IActionResult> TopupOptions([FromBody] ReferredBy args)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");
            await refApi.ReferralUserIdPostAsync(GetId(args.RefCode).ToString(), user.Id.ToString());
            return Ok();
        }

        /// <summary>
        /// Returns ReferralCode and refferal satistics for the user
        /// </summary>
        /// <returns></returns>
        [Route("info")]
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<ActionResult<ReferralInfo>> GetRefInfo()
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");
            var infoTask = refApi.ReferralUserIdGetAsync(user.Id.ToString());
            var oldInfo = await GetOldRefInfo(user);
            RefInfo info;
            try
            {
                info = await infoTask;
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "getting ref info");
                info = new RefInfo(new ReferralElement(), new List<ReferralElement>());
            }
            string refedBy = null;
            if (!string.IsNullOrEmpty(info?.Inviter?.Inviter))
            {
                var referrer = UserService.Instance.GetUserById(int.Parse(info.Inviter.Inviter));
                refedBy = UserService.Instance.AnonymiseEmail(referrer.Email);
            }
            return Ok(new ReferralInfo()
            {
                oldInfo = oldInfo,
                ReferredBy = refedBy,
                ReferedCount = info.Invited.Count,
                ValidatedMinecraft = info.Invited.Where(i => i.Flags.Value.HasFlag(ReferralFlags.NUMBER_1)).Count(),
                PurchasedCoins = info.Invited.Where(i => i.Flags.Value.HasFlag(ReferralFlags.NUMBER_2)).Count(),
            });
        }

        private async Task<OldRefInfo> GetOldRefInfo(GoogleUser user)
        {
            var referedUsers = await db.Users.Where(u => u.ReferedBy == user.Id).ToListAsync();
            var minDate = new DateTime(2020, 2, 2);
            var boni = await db.Boni.Where(b => b.UserId == user.Id).ToListAsync();
            var upgraded = boni.Where(b => b.UserId == user.Id && b.Type == Bonus.BonusType.REFERED_UPGRADE).ToList();
            var receivedHours = boni.Where(b => b.Type != Bonus.BonusType.PURCHASE).Sum(b => b.BonusTime.TotalHours);
            return new OldRefInfo()
            {
                RefId = hashids.Encode(user.Id),
                BougthPremium = upgraded.Count,
                ReceivedTime = TimeSpan.FromHours(receivedHours),
                ReceivedHours = (int)receivedHours,
                ReferCount = referedUsers.Count
            };
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


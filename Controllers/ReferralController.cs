using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Referral.Client.Api;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Primitives;
using HashidsNet;
using System.Linq;
using Coflnet.Sky.Api.Models.Referral;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Referral.Client.Model;
using Coflnet.Sky.PlayerName.Client.Api;

namespace Coflnet.Sky.Api.Controller
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
        private GoogletokenService tokenService;
        private HypixelContext db;
        private McConnect.Api.IConnectApi connectApi;
        private IPlayerNameApi playerNameApi;
        Hashids hashids = new Hashids("simple salt", 6);

        /// <summary>
        /// Creates a new intance of <see cref="ReferralController"/>
        /// </summary>
        /// <param name="refApi"></param>
        /// <param name="premiumService"></param>
        /// <param name="db"></param>
        /// <param name="connectApi"></param>
        public ReferralController(IReferralApi refApi, GoogletokenService premiumService, HypixelContext db, McConnect.Api.IConnectApi connectApi, IPlayerNameApi playerNameApi)
        {
            this.refApi = refApi;
            this.tokenService = premiumService;
            this.db = db;
            this.connectApi = connectApi;
            this.playerNameApi = playerNameApi;
        }


        /// <summary>
        /// tells the backend that the user was referred by someone
        /// </summary>
        /// <returns></returns>
        [Route("referred/by")]
        [HttpPost]
        public async Task<IActionResult> TopupOptions([FromBody] ReferredBy args)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            try
            {
                await refApi.ReferralUserIdPostAsync(GetId(args.RefCode).ToString(), user.Id.ToString());
                return Ok();
            }
            catch (Sky.Referral.Client.Client.ApiException e)
            {
                throw new CoflnetException("referral_error", e.Message.Substring(63).Trim('}', '"'));
            }
        }

        /// <summary>
        /// Returns ReferralCode and statistics for the user
        /// </summary>
        /// <returns></returns>
        [Route("info")]
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<ActionResult<ReferralInfo>> GetRefInfo()
        {
            var user = await GetUserOrDefault();
            if (user == default)
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
            var nameTask = GetInviterMinecraftName(info);
            string refedBy = null;
            if (!string.IsNullOrEmpty(info?.Inviter?.Inviter))
            {
                var referrer = UserService.Instance.GetUserById(int.Parse(info.Inviter.Inviter));
                refedBy = UserService.Instance.AnonymiseEmail(referrer.Email);
            }
            var name = await nameTask;
            return Ok(new ReferralInfo()
            {
                oldInfo = oldInfo,
                ReferredBy = refedBy,
                InviterMinecraftName = name,
                ReferedCount = info.Invited.Count,
                ValidatedMinecraft = info.Invited.Where(i => i.Flags.Value.HasFlag(ReferralFlags.NUMBER_1)).Count(),
                PurchasedCoins = info.Invited.Where(i => i.Flags.Value.HasFlag(ReferralFlags.NUMBER_2)).Count(),
                PurchasedCoinAmount = info.Invited.Sum(i => i.PurchaseAmount)
            });
        }

        private async Task<string> GetInviterMinecraftName(RefInfo info)
        {
            var minecraftAccounts = await connectApi.ConnectUserUserIdGetAsync(info.Inviter.ToString());
            var uuid = minecraftAccounts.Accounts.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault()?.AccountUuid;
            if(uuid == null)
                return null;
            var name = await playerNameApi.PlayerNameNameUuidGetAsync(uuid.Trim('"'));
            return name;
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

        private async Task<GoogleUser?> GetUserOrDefault()
        {
            if (!Request.Headers.TryGetValue("GoogleToken", out StringValues value)
                && !Request.Headers.TryGetValue("Authorization", out value))
                return null;
            return await tokenService.GetUserWithToken(value);
        }
    }
}


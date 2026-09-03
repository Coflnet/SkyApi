using System.Threading.Tasks;
#nullable enable
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
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;

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
        private readonly IConfiguration configuration;
        private readonly IHttpClientFactory clients;
        Hashids hashids = new Hashids("simple salt", 6);

        /// <summary>
        /// Creates a new intance of <see cref="ReferralController"/>
        /// </summary>
        /// <param name="refApi"></param>
        /// <param name="premiumService"></param>
        /// <param name="db"></param>
        /// <param name="connectApi"></param>
        /// <param name="playerNameApi"></param>
        /// <param name="configuration"></param>
        /// <param name="clients"></param>
        public ReferralController(
            IReferralApi refApi,
            GoogletokenService premiumService,
            HypixelContext db,
            McConnect.Api.IConnectApi connectApi,
            IPlayerNameApi playerNameApi,
            IConfiguration configuration,
            IHttpClientFactory clients)
        {
            this.refApi = refApi;
            this.tokenService = premiumService;
            this.db = db;
            this.connectApi = connectApi;
            this.playerNameApi = playerNameApi;
            this.configuration = configuration;
            this.clients = clients;
        }


        /// <summary>
        /// tells the backend that the user was referred by someone
        /// </summary>
        /// <returns></returns>
        [Route("referred/by")]
        [HttpPost]
    [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> TopupOptions([FromBody] ReferredBy args)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            var token = configuration["REFERRAL_MUTATION_TOKEN"];
            if (!Uri.TryCreate(configuration["REFERRAL_BASE_URL"],
                    UriKind.Absolute, out var baseUri)
                || token?.Length < 32)
                throw new CoflnetException(
                    "referral_unavailable",
                    "The referral service is temporarily unavailable.");
            var path = $"Referral/{Uri.EscapeDataString(GetId(args.RefCode).ToString())}"
                + $"?referedUser={Uri.EscapeDataString(user.Id.ToString())}"
                + $"&programVersion={Uri.EscapeDataString(args.ProgramVersion ?? "")}"
                + $"&locale={Uri.EscapeDataString(args.Locale ?? "")}";
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(new Uri(baseUri.ToString().TrimEnd('/') + "/"), path));
            request.Headers.Add("X-Referral-Mutation-Token", token);
            using var response = await clients.CreateClient()
                .SendAsync(request, HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
                throw new CoflnetException(
                    "referral_error",
                    "The referral could not be recorded. Review the current offer and try again.");
            return Ok();
        }

        /// <summary>
        /// Returns ReferralCode and statistics for the user
        /// </summary>
        /// <returns></returns>
        [Route("info")]
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Microsoft.AspNetCore.Authorization.Authorize]
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
            string? refedBy = null;
            if (!string.IsNullOrEmpty(info.Inviter?.Inviter))
            {
                var referrer = UserService.Instance.GetUserById(int.Parse(info.Inviter.Inviter));
                refedBy = UserService.Instance.AnonymiseEmail(referrer.Email);
            }
            var name = await nameTask;
            var invited = info.Invited ?? [];
            return Ok(new ReferralInfo()
            {
                oldInfo = oldInfo,
                ReferredBy = refedBy,
                InviterMinecraftName = name,
                ReferedCount = invited.Count,
                ValidatedMinecraft = invited.Count(i => i.Flags.GetValueOrDefault().HasFlag(ReferralFlags.NUMBER_1)),
                PurchasedCoins = invited.Count(i => i.Flags.GetValueOrDefault().HasFlag(ReferralFlags.NUMBER_2)),
                PurchasedCoinAmount = invited.Sum(i => i.PurchaseAmount)
            });
        }

        private async Task<string?> GetInviterMinecraftName(RefInfo info)
        {
            var inviterId = info?.Inviter?.Inviter;
            if(inviterId == null)
                return null;
            var minecraftAccounts = await connectApi.ConnectUserUserIdGetAsync(inviterId.ToString());
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

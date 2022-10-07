

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api;
using Coflnet.Sky.Api.Models;
using System.Threading;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Endpoints for related to paid services
    /// </summary>
    [ApiController]
    [Route("api")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class PremiumController : ControllerBase
    {
        private ProductsApi productsService;
        private TopUpApi topUpApi;
        private UserApi userApi;
        private PremiumService premiumService;

        /// <summary>
        /// Creates a new intance of <see cref="PremiumController"/>
        /// </summary>
        /// <param name="productsService"></param>
        /// <param name="topUpApi"></param>
        /// <param name="userApi"></param>
        /// <param name="premiumService"></param>
        public PremiumController(ProductsApi productsService, TopUpApi topUpApi, UserApi userApi, PremiumService premiumService)
        {
            this.productsService = productsService;
            this.topUpApi = topUpApi;
            this.userApi = userApi;
            this.premiumService = premiumService;
        }

        /// <summary>
        /// Products to top up
        /// </summary>
        /// <returns></returns>
        [Route("topup/options")]
        [HttpGet]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<Payments.Client.Model.TopUpProduct>> TopupOptions()
        {
            var products = await productsService.ProductsTopupGetAsync();
            return products;
        }

        /// <summary>
        /// Start a new topup session with stripe
        /// </summary>
        /// <returns></returns>
        [Route("topup/stripe/{productSlug}")]
        [HttpPost]
        public async Task<IActionResult> StartTopUp(string productSlug, [FromBody] TopUpArguments args)
        {
            foreach (var item in Request.Headers)
            {
                Console.WriteLine(item.Key + ": " + String.Join(", ", item.Value));
            }
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");

            var session = await topUpApi.TopUpStripePostAsync(user.Id.ToString(), productSlug, new TopUpOptions()
            {
                UserEmail = user.Email,
                TopUpAmount = args.CoinAmount
            });
            return Ok(session);
        }
        /// <summary>
        /// Start a new topup session with paypal
        /// </summary>
        /// <returns></returns>
        [Route("topup/paypal/{productSlug}")]
        [HttpPost]
        public async Task<IActionResult> StartTopUpPaypal(string productSlug, [FromBody] TopUpArguments args)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");

            var session = await topUpApi.TopUpPaypalPostAsync(user.Id.ToString(), productSlug, new TopUpOptions()
            {
                UserEmail = user.Email,
                TopUpAmount = args.CoinAmount
            });
            return Ok(session);
        }


        /// <summary>
        /// Purchase a service 
        /// </summary>
        /// <returns></returns>
        [Route("service/purchase")]
        [HttpPost]
        public async Task<IActionResult> PurchaseService([FromBody] PurchaseArgs args)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");
            try
            {
                var reference = args.reference;
                var count = args.count == 0 ? 1 : args.count;
                if (string.IsNullOrEmpty(reference))
                    reference = "apiautofill" + DateTime.UtcNow;
                var purchaseResult = await userApi.UserUserIdServicePurchaseProductSlugPostAsync(user.Id.ToString(), args.slug, reference, count);
                return Ok(purchaseResult);
            }
            catch (Exception e)
            {
                throw new CoflnetException("payment_error", e.Message);
            }
        }

        /// <summary>
        /// Get adjusted prices
        /// </summary>
        /// <returns></returns>
        [Route("premium/prices/adjusted")]
        [HttpPost]
        public async Task<IActionResult> PurchaseService([FromBody] IEnumerable<string> slugs)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");
            try
            {
                var adjusted = await productsService.ProductsUserUserIdGetAsync(user.Id.ToString(), slugs.ToList());
                if (adjusted == null)
                    return NotFound();
                return Ok(adjusted);
            }
            catch (Exception e)
            {
                throw new CoflnetException("payment_error", e.Message);
            }
        }
        /// <summary>
        /// Get adjusted prices
        /// </summary>
        /// <returns></returns>
        [Route("premium/user/owns")]
        [HttpPost]
        public async Task<ActionResult<Dictionary<string, Sky.Api.Models.OwnerShip>>> GetOwnerShips([FromBody] List<string> slugsToTest)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");
            try
            {
                var cancelationSource = new CancellationTokenSource(10_000);
                var owns = await userApi.UserUserIdOwnsUntilPostAsync(user.Id.ToString(), slugsToTest, 0,cancelationSource.Token);
                if (owns == null)
                    return NotFound();
                return Ok(owns.Where(o => o.Value > DateTime.Now).ToDictionary(o => o.Key, o => new Sky.Api.Models.OwnerShip()
                {
                    ExpiresAt = o.Value
                }));
            }
            catch (Exception e)
            {
                throw new CoflnetException("payment_error", e.Message);
            }
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


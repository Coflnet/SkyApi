

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
        public async Task<IActionResult> StartTopUp(string productSlug)
        {
            foreach (var item in Request.Headers)
            {
                Console.WriteLine(item.Key + ": " + String.Join(", ", item.Value));
            }
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");

            var session = await topUpApi.TopUpStripePostAsync(user.Id.ToString(), productSlug);
            return Ok(session);
        }
        /// <summary>
        /// Start a new topup session with paypal
        /// </summary>
        /// <returns></returns>
        [Route("topup/paypal/{productSlug}")]
        [HttpPost]
        public async Task<IActionResult> StartTopUpPaypal(string productSlug)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");

            var session = await topUpApi.TopUpPaypalPostAsync(user.Id.ToString(), productSlug);
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
                var count =  args.count == 0 ? 1 : args.count;
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




using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Filter;
using hypixel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Coflnet.Payments.Client.Model;

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
        public async Task<IEnumerable<Payments.Client.Model.PurchaseableProduct>> TopupOptions()
        {
            var products = await productsService.ProductsGetAsync();
            return products.Where(p => p.Type == Coflnet.Payments.Client.Model.ProductType.NUMBER_4);
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

            var session = await topUpApi.TopUpStripePostAsync(productSlug, user.Id.ToString());
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

            var session = await topUpApi.TopUpPaypalPostAsync(productSlug, user.Id.ToString());
            return Ok(session);
        }


        /// <summary>
        /// Purchase a product 
        /// </summary>
        /// <returns></returns>
        [Route("purchase/{productSlug}")]
        [HttpPost]
        public async Task<IActionResult> Purchase(string productSlug)
        {
            if (!TryGetUser(out GoogleUser user))
                return Unauthorized("no googletoken header");

            var purchaseResult = await userApi.UserUserIdPurchaseProductSlugPostAsync(user.Id.ToString(), productSlug);
            return Ok(purchaseResult);
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


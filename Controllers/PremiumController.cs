using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Models;
using System.Threading;

namespace Coflnet.Sky.Api.Controller
{
    /// <summary>
    /// Endpoints for related to paid services
    /// </summary>
    [ApiController]
    [Route("api")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class PremiumController : ControllerBase
    {
        private ProductsApi productsService;
        private TopUpApi topUpApi;
        private UserApi userApi;
        private GoogletokenService tokenService;
        private ITransactionApi transactionApi;

        /// <summary>
        /// Creates a new intance of <see cref="PremiumController"/>
        /// </summary>
        /// <param name="productsService"></param>
        /// <param name="topUpApi"></param>
        /// <param name="userApi"></param>
        /// <param name="premiumService"></param>
        /// <param name="transactionApi"></param>
        public PremiumController(
            ProductsApi productsService,
            TopUpApi topUpApi,
            UserApi userApi,
            GoogletokenService premiumService,
            ITransactionApi transactionApi)
        {
            this.productsService = productsService;
            this.topUpApi = topUpApi;
            this.userApi = userApi;
            this.tokenService = premiumService;
            this.transactionApi = transactionApi;
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

            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");

            TopUpOptions options = GetOptions(args, user);
            try
            {
                var session = await topUpApi.TopUpStripePostAsync(user.Id.ToString(), productSlug, options);
                if (options.UserIp == "172.93.179.188")
                    throw new CoflnetException("blacklisted_ip", "You are banned from using this service");
                return Ok(session);
            }
            catch (Exception ex)
            {
                ForwardPaymentErrors(ex);
                throw;
            }
        }

        private static void ForwardPaymentErrors(Exception ex)
        {
            if (ex.Message.Contains("Message"))
                throw new CoflnetException("üayment_error", ex.Message.Substring(44).TrimEnd('}'));
        }

        private TopUpOptions GetOptions(TopUpArguments args, GoogleUser user)
        {
            var realIp = (Request.Headers.Where(h => h.Key.ToLower() == "x-original-forwarded-for" || h.Key.ToLower() == "cf-connecting-ip").Select(h => h.Value).First()).ToString();
            Console.WriteLine("RealIp: " + realIp);
            var fingerprint = GetBrowserFingerprint();
            Console.WriteLine("Fingerprint: " + fingerprint);
            string locale = GetLocale();
            var options = new TopUpOptions()
            {
                UserEmail = user.Email,
                TopUpAmount = args.CoinAmount,
                UserIp = realIp,
                Fingerprint = fingerprint,
                Locale = locale
            };
            return options;
        }

        private string GetLocale()
        {
            var locale = "de-DE";
            if (Request.Headers.TryGetValue("cf-ipcountry", out StringValues country))
            {
                locale = country.ToString();
            }
            else if (Request.Headers.TryGetValue("accept-language", out StringValues acceptLanguage))
            {
                locale = acceptLanguage.First().ToString();
            }

            return locale;
        }

        /// <summary>
        /// Start a new topup session with paypal
        /// </summary>
        /// <returns></returns>
        [Route("topup/paypal/{productSlug}")]
        [HttpPost]
        public async Task<IActionResult> StartTopUpPaypal(string productSlug, [FromBody] TopUpArguments args)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");

            try
            {
                var session = await topUpApi.TopUpPaypalPostAsync(user.Id.ToString(), productSlug, GetOptions(args, user));
                return Ok(session);
            }
            catch (System.Exception ex)
            {
                ForwardPaymentErrors(ex);
                throw;
            }
        }

        /// <summary>
        /// Start a new topup session with lemonsqueezy
        /// </summary>
        /// <returns></returns>
        [Route("topup/lemonsqueezy/{productSlug}")]
        [HttpPost]
        public async Task<IActionResult> StartTopUpLemonSqueezy(string productSlug, [FromBody] TopUpArguments args)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");

            try
            {
                var session = await topUpApi.TopUpLemonsqueezyPostAsync(user.Id.ToString(), productSlug, GetOptions(args, user));
                return Ok(session);
            }
            catch (System.Exception ex)
            {
                ForwardPaymentErrors(ex);
                throw;
            }
        }

        private string GetBrowserFingerprint()
        {
            var userAgent = this.Request.Headers["User-Agent"].ToString();
            var acceptLanguage = this.Request.Headers["Accept-Language"].ToString();
            var acceptEncoding = this.Request.Headers["Accept-Encoding"].ToString();
            var accept = this.Request.Headers["Accept"].ToString();
            var referer = this.Request.Headers["Referer"].ToString();
            var host = this.Request.Headers["Host"].ToString();
            var connection = this.Request.Headers["Connection"].ToString();
            var md5hash = System.Security.Cryptography.MD5.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(userAgent + acceptLanguage + acceptEncoding + accept + referer + host + connection));
            var hash = BitConverter.ToString(md5hash).Replace("-", "").ToLowerInvariant();
            return hash;
        }


        /// <summary>
        /// Purchase a service 
        /// </summary>
        /// <returns></returns>
        [Route("service/purchase")]
        [HttpPost]
        public async Task<IActionResult> PurchaseService([FromBody] PurchaseArgs args)
        {
            var user = await GetUserOrDefault(true);
            if (user == default)
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
        [Obsolete("endpoint will be removed no service has adusted pricing")]
        [HttpPost]
        public async Task<IActionResult> PurchaseService([FromBody] IEnumerable<string> slugs)
        {
            var user = await GetUserOrDefault();
            if (user == default)
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
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            try
            {
                var cancelationSource = new CancellationTokenSource(10_000);
                var owns = await userApi.UserUserIdOwnsUntilPostAsync(user.Id.ToString(), slugsToTest, 0, cancelationSource.Token);
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

        /// <summary>
        /// Get transaction history
        /// </summary>
        /// <returns></returns>
        [Route("premium/transactions")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CoinTransaction>>> GetTransactions()
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            try
            {
                var transactions = await transactionApi.TransactionUUserIdGetAsync(user.Id.ToString());
                if (transactions == null)
                    return NotFound();
                return Ok(transactions);
            }
            catch (Exception e)
            {
                throw new CoflnetException("payment_error", e.Message);
            }
        }

        private async Task<GoogleUser?> GetUserOrDefault(bool isPurchase = false)
        {
            if (!Request.Headers.TryGetValue("GoogleToken", out StringValues value)
                && !Request.Headers.TryGetValue("Authorization", out value))
                return null;
            return await tokenService.GetUserWithToken(value, isPurchase);
        }
    }
}


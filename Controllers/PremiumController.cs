using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Models;
using System.Threading;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;

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
        private ILogger<PremiumController> logger;
        private ISubscriptionApi subscriptionApi;

        /// <summary>
        /// Creates a new intance of <see cref="PremiumController"/>
        /// </summary>
        /// <param name="productsService"></param>
        /// <param name="topUpApi"></param>
        /// <param name="userApi"></param>
        /// <param name="premiumService"></param>
        /// <param name="transactionApi"></param>
        /// <param name="logger"></param>
        /// <param name="subscriptionApi"></param>
        public PremiumController(
            ProductsApi productsService,
            TopUpApi topUpApi,
            UserApi userApi,
            GoogletokenService premiumService,
            ITransactionApi transactionApi,
            ILogger<PremiumController> logger,
            ISubscriptionApi subscriptionApi)
        {
            this.productsService = productsService;
            this.topUpApi = topUpApi;
            this.userApi = userApi;
            this.tokenService = premiumService;
            this.transactionApi = transactionApi;
            this.logger = logger;
            this.subscriptionApi = subscriptionApi;
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
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<TopUpIdResponse>> StartTopUp(string productSlug, [FromBody] TopUpArguments args)
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
                Locale = locale,
                CreatorCode = args.CreatorCode,
                SuccessUrl = args.SuccessUrl,
                CancelUrl = args.CancelUrl,
                DiscountCode = args.Discountcode
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
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<TopUpIdResponse>> StartTopUpPaypal(string productSlug, [FromBody] TopUpArguments args)
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


        [Route("topup/playstore")]
        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<PlaystorTopup>> StartTopUpPlayStore()
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            return new PlaystorTopup()
            {
                UserId = user.Id.ToString()
            };
        }

        public class PlaystorTopup
        {
            public string UserId { get; set; }
        }

        [Route("topup/playstore/complete")]
        [HttpPost]
        public async Task<ActionResult<bool>> CompleteTopUpPlayStore([FromBody] GooglePlayPurchaseRequest args, [FromServices] IGooglePayApi googlePayApi)
        {
            var result = await googlePayApi.ApiGooglePayVerifyPostAsync(args);
            if (!result.IsValid)
            {
                logger.LogWarning("Invalid google play purchase for user {userId}: {errorMessage}", args.UserId, result.ErrorMessage);
                return BadRequest(result.ErrorMessage);
            }
            return result.IsValid;
        }

        [Route("topup/rates")]
        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<BatchProductPricingResponse>> GetPriceRate([FromBody] PricingRequest request,
            [FromServices] ICreatorCodeApi creatorCodeApi)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            var response = await creatorCodeApi.ApiCreatorCodePricingBatchPostAsync(new()
            {
                CountryCode = request.CountryCode,
                CreatorCode = request.CreatorCode,
                ProductSlugs = request.ProductSlugs
            });
            return response;
        }

        [Route("discount/{code}")]
        [HttpGet]
        public async Task<ValidatedDiscount> GetDiscountCodeDetails(string code)
        {
            return await topUpApi.TopUpDiscountValidateGetAsync(code);
        }

        public class PricingRequest
        {
            public List<string> ProductSlugs { get; set; }
            public string CountryCode { get; set; }
            public string? CreatorCode { get; set; }
        }

        /// <summary>
        /// Start a new topup session with lemonsqueezy
        /// </summary>
        /// <returns></returns>
        [Route("topup/lemonsqueezy/{productSlug}")]
        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<TopUpIdResponse>> StartTopUpLemonSqueezy(string productSlug, [FromBody] TopUpArguments args)
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


        [Route("linkvertise")]
        [HttpGet]
        public async Task<IActionResult> Linkvertise(string hash, string? email, [FromServices] HttpClient httpClient)
        {
            var user = await GetUserOrDefault();
            if (user == default && string.IsNullOrEmpty(hash))
                return Unauthorized("no auth header passed");
            var userId = Request.Cookies.Where(c => c.Key == "server-userId").FirstOrDefault().Value;
            if (string.IsNullOrEmpty(hash))
            {
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"https://sky.coflnet.com/api/linkvertise?user={user.Email}"));
                var redirectTo = $"https://link-to.net/1216620/{user.Email}/dynamic?r={base64}";
                // setcookie
                Response.Cookies.Append("server-userId", user.Id.ToString(), new() { Expires = DateTimeOffset.UtcNow.AddMinutes(30) });
                return Ok(redirectTo);
            }
            if (string.IsNullOrEmpty(userId))
            {
                userId = (await UserService.Instance.GetUserIdByEmail(email)).ToString();
            }
            var lastTransactionsTask = transactionApi.TransactionUUserIdGetAsync(userId, 2);
            var url = $"https://publisher.linkvertise.com/api/v1/anti_bypassing?token=c43268cacfa9a88da627b24876ee3dddbadd08292dc54e420d24b4d6510c6a9e&hash={hash}";
            var response = await httpClient.PostAsync(url, new StringContent(""));
            var responseString = await response.Content.ReadAsStringAsync();
            logger.LogInformation("Response user {userId}, has valid {hashResult}", userId, responseString);
            var transactions = await lastTransactionsTask;
            if (transactions.Any(t => t.ProductId == "compensation" && t.Reference.StartsWith("ad") && t.TimeStamp > DateTime.UtcNow.AddMinutes(-50)))
            {
                // don't recredit
                return Redirect("https://sky.coflnet.com/linkvertise/success");
            }

            if (responseString.ToLower().Contains("true"))
            {
                logger.LogInformation("successful");
                await topUpApi.TopUpCustomPostAsync(userId.ToString(), new CustomTopUp()
                {
                    Amount = 4,
                    ProductId = "compensation",
                    Reference = "ad-" + hash.Truncate(4)
                });
                await userApi.UserUserIdServicePurchaseProductSlugPostAsync(userId.ToString(), "starter_premium-hour", "ap-" + hash.Truncate(4), 1);
                // delete cookie 
                return Redirect("https://sky.coflnet.com/linkvertise/success");
            }
            return Redirect("https://sky.coflnet.com/linkvertise/fail");
        }

        /// <summary>
        /// Purchase a service 
        /// </summary>
        /// <returns></returns>
        [Route("service/purchase")]
        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
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
                await userApi.UserUserIdServicePurchaseProductSlugPostAsync(user.Id.ToString(), args.slug, reference, count);
                return Ok();
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
        [Microsoft.AspNetCore.Authorization.Authorize]
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
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<Dictionary<string, Models.OwnerShip>>> GetOwnerShips([FromBody] List<string> slugsToTest)
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
                return Ok(owns.Where(o => o.Value > DateTime.Now).ToDictionary(o => o.Key, o => new Models.OwnerShip()
                {
                    ExpiresAt = o.Value
                }));
            }
            catch (Exception e)
            {
                if (e.Message.Contains("The operation was canceled")) // timeout when db not reachable
                    return Ok(slugsToTest.ToDictionary(s => s, s => new Models.OwnerShip()
                    {
                        ExpiresAt = DateTime.Now.AddMinutes(10)
                    }));
                logger.LogError(e, "Error while checking ownership");
                return Ok(slugsToTest.Where(s => s == "premium" || s == "starter_premium").ToDictionary(s => s, s => new Models.OwnerShip()
                {
                    ExpiresAt = DateTime.Now.AddMinutes(5)
                }));
            }
        }

        /// <summary>
        /// Get transaction history
        /// </summary>
        /// <returns></returns>
        [Route("premium/transactions")]
        [HttpGet]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<IEnumerable<CoinTransaction>>> GetTransactions()
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            try
            {
                if (user.Id == 28258)
                    throw new CoflnetException("unavailable", "No transactions available for this user");
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
        /// <summary>
        /// Purchase a service 
        /// </summary>
        /// <returns></returns>
        [Route("premium/subscription/{subscriptionSlug}")]
        [HttpPost]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<TopUpIdResponse>> PurchaseServiceSubscription(string subscriptionSlug, string creatorCode = null, string discountcode = null)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            try
            {
                TopUpOptions options = GetOptions(new(), user);
                options.CreatorCode = creatorCode;
                options.EnableTrial = false;
                options.DiscountCode = discountcode;
                var link = await topUpApi.TopUpLemonsqueezySubscribePostAsync(user.Id.ToString(), subscriptionSlug, options);
                return Ok(link);
            }
            catch (Exception e)
            {
                throw new CoflnetException("payment_error", e.Message);
            }
        }
        [HttpGet]
        [Route("premium/subscription")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult<PremiumSubscription[]>> GetSubscription()
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");

            var subscriptions = await subscriptionApi.ApiSubscriptionUUserIdGetAsync(user.Id.ToString());
            var publicSubscriptions = subscriptions.Select(s => new PremiumSubscription
            {
                ExternalId = s.ExternalId,
                EndsAt = s.EndsAt,
                ProductName = s.Product?.Title,
                PaymentAmount = s.PaymentAmount,
                RenewsAt = s.RenewsAt,
                CreatedAt = s.CreatedAt
            }).ToArray();
            return Ok(publicSubscriptions);
        }

        [HttpDelete]
        [Route("premium/subscription/{externalId}")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult> CancelSubscription(string externalId)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");

            await subscriptionApi.ApiSubscriptionCancelSubscriptionIdDeleteAsync(externalId, user.Id.ToString());
            return Ok();
        }

        [HttpPut]
        [Route("premium/subscription/{externalId}/reactivate")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult> ReactivateSubscription(string externalId)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            try
            {
                await subscriptionApi.ApiSubscriptionResumeSubscriptionIdPostAsync(externalId, user.Id.ToString());
            }
            catch (Exception e)
            {
                throw new CoflnetException("reactivate_failed", e.Message);
            }
            return Ok();
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


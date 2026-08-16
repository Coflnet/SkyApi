using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services;
using System.Threading;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using StackExchange.Redis;

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
        private IConfiguration configuration;
        private LegalManifestService legalManifest;

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
        /// <param name="configuration"></param>
        /// <param name="legalManifest"></param>
        public PremiumController(
            ProductsApi productsService,
            TopUpApi topUpApi,
            UserApi userApi,
            GoogletokenService premiumService,
            ITransactionApi transactionApi,
            ILogger<PremiumController> logger,
            ISubscriptionApi subscriptionApi,
            IConfiguration configuration,
            LegalManifestService legalManifest)
        {
            this.productsService = productsService;
            this.topUpApi = topUpApi;
            this.userApi = userApi;
            this.tokenService = premiumService;
            this.transactionApi = transactionApi;
            this.logger = logger;
            this.subscriptionApi = subscriptionApi;
            this.configuration = configuration;
            this.legalManifest = legalManifest;
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
            if (await MustRejectNewContract(user.Id, configuration))
                return TermsAcceptanceRequired();

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

        internal static void ForwardPaymentErrors(Exception ex)
        {
            if (ex is not Coflnet.Payments.Client.Client.ApiException { ErrorContent: string errorContent })
                return;

            try
            {
                var message = JsonConvert.DeserializeObject<ErrorResponse>(errorContent)?.Message;
                if (!string.IsNullOrWhiteSpace(message))
                    throw new CoflnetException("payment_error", message);
            }
            catch (JsonReaderException)
            {
                // Preserve the original upstream exception when its body is not JSON.
            }
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
            if (await MustRejectNewContract(user.Id, configuration))
                return TermsAcceptanceRequired();

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
            if (await MustRejectNewContract(user.Id, configuration))
                return TermsAcceptanceRequired();
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
            [FromServices] ICreatorCodeApi creatorCodeApi, CancellationToken cancellationToken)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var response = await creatorCodeApi.ApiCreatorCodePricingBatchPostAsync(new()
            {
                CountryCode = request.CountryCode,
                CreatorCode = request.CreatorCode,
                ProductSlugs = request.ProductSlugs
            }, 0, cts.Token);
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
            if (await MustRejectNewContract(user.Id, configuration))
                return TermsAcceptanceRequired();

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
        /// Rewards a user for completing an ad-link offer.
        /// Called twice: once without a hash to obtain the link the user has to complete and once by
        /// the provider (linkvertise or lootlabs) when the user returns after finishing the offer.
        /// </summary>
        /// <param name="hash">anti-bypass token added on the return trip (linkvertise hash / lootlabs signed token)</param>
        /// <param name="state">one-time server state binding the callback to the user who started the offer</param>
        /// <param name="httpClient"></param>
        /// <param name="redis"></param>
        /// <param name="provider">ad provider to use, either "linkvertise" (default) or "lootlabs"</param>
        [Route("linkvertise")]
        [HttpGet]
        public async Task<IActionResult> Linkvertise(
            string hash,
            string? state,
            [FromServices] HttpClient httpClient,
            [FromServices] IConnectionMultiplexer redis,
            string provider = "linkvertise")
        {
            if (!TryNormalizeAdProvider(provider, out provider))
                return BadRequest("unsupported ad provider");
            var isLootlabs = provider == "lootlabs";
            var database = redis.GetDatabase();
            if (isLootlabs && string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(state))
            {
                if (!IsValidAdCompletionToken(state))
                {
                    logger.LogWarning(
                        "Lootlabs browser callback rejected due to invalid state length {stateLength}",
                        state?.Length ?? 0);
                    return Redirect("https://sky.coflnet.com/linkvertise/fail");
                }
                var result = await WaitForAdResult(database, GetAdResultKey(provider, state));
                return Redirect(result
                    ? "https://sky.coflnet.com/linkvertise/success"
                    : "https://sky.coflnet.com/linkvertise/fail");
            }
            if (string.IsNullOrEmpty(hash))
            {
                var user = await GetUserOrDefault();
                if (user == default)
                    return Unauthorized("no auth header passed");
                state = System.Security.Cryptography.RandomNumberGenerator.GetHexString(32).ToLowerInvariant();
                var stateKey = GetAdStateKey(provider, state);
                if (!await database.StringSetAsync(
                        stateKey,
                        user.Id.ToString(),
                        TimeSpan.FromHours(4),
                        When.NotExists))
                    throw new CoflnetException("ad_session_error", "Could not create an ad session, please try again");
                string redirectTo;
                if (isLootlabs)
                    redirectTo = await CreateLootlabsRedirect(state, httpClient);
                else
                {
                    var callback = $"https://sky.coflnet.com/api/linkvertise?provider={provider}&state={state}";
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(callback));
                    redirectTo = $"https://link-to.net/1216620/{user.Email}/dynamic?r={base64}";
                }
                return Ok(redirectTo);
            }
            if (!IsValidAdCompletionToken(hash) || !IsValidAdCompletionToken(state))
            {
                logger.LogWarning(
                    "Ad callback rejected for provider {provider} due to invalid token lengths hash={hashLength}, state={stateLength}",
                    provider,
                    hash?.Length ?? 0,
                    state?.Length ?? 0);
                return Redirect("https://sky.coflnet.com/linkvertise/fail");
            }
            bool completed;
            if (isLootlabs)
            {
                completed = VerifyLootlabsToken(state, hash, configuration["LOOTLABS_API_TOKEN"]);
                logger.LogInformation("Lootlabs callback has valid result {result}", completed);
            }
            else
            {
                var linkvertiseToken = configuration["LINKVERTISE_ANTI_BYPASS_TOKEN"];
                if (string.IsNullOrWhiteSpace(linkvertiseToken))
                    throw new CoflnetException("linkvertise_unconfigured", "linkvertise is not configured on this server");
                var url = $"https://publisher.linkvertise.com/api/v1/anti_bypassing?token={Uri.EscapeDataString(linkvertiseToken)}&hash={Uri.EscapeDataString(hash)}";
                var response = await httpClient.PostAsync(url, new StringContent(""));
                var responseString = await response.Content.ReadAsStringAsync();
                completed = response.IsSuccessStatusCode && IsSuccessfulLinkvertiseResponse(responseString);
                logger.LogInformation("Linkvertise callback has valid result {result}", completed);
            }
            var granted = await TryCompleteAdSession(
                completed,
                database,
                provider,
                state,
                hash,
                userId => GrantAdReward(userId, hash));
            if (granted)
                return Redirect("https://sky.coflnet.com/linkvertise/success");
            logger.LogWarning(
                "Ad callback did not claim a session for provider {provider}; providerConfirmed={providerConfirmed}",
                provider,
                completed);
            return Redirect("https://sky.coflnet.com/linkvertise/fail");
        }

        /// <summary>
        /// Receives LootLabs' server-to-server completion confirmation. The shared token is
        /// configured as part of the postback URL in the LootLabs dashboard.
        /// </summary>
        [Route("linkvertise/lootlabs/postback")]
        [HttpGet]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> LootlabsPostback(
            [FromQuery(Name = "token")] string token,
            [FromQuery(Name = "click_id")] string state,
            [FromQuery(Name = "unique_id")] string uniqueId,
            [FromServices] IConnectionMultiplexer redis)
        {
            if (!IsValidLootlabsPostbackToken(configuration["LOOTLABS_POSTBACK_TOKEN"], token))
            {
                logger.LogWarning("Rejected Lootlabs postback with invalid authentication");
                return Unauthorized();
            }
            var completionHash = GetLootlabsCompletionHash(uniqueId);
            if (!IsValidAdCompletionToken(state) || completionHash == null)
            {
                logger.LogWarning(
                    "Rejected Lootlabs postback with invalid identifiers stateLength={stateLength}, uniqueIdLength={uniqueIdLength}",
                    state?.Length ?? 0,
                    uniqueId?.Length ?? 0);
                return BadRequest();
            }

            var database = redis.GetDatabase();
            var resultKey = GetAdResultKey("lootlabs", state);
            if (await database.StringGetAsync(resultKey) == "success")
                return Ok();

            var granted = await TryCompleteLootlabsPostback(
                configuration["LOOTLABS_POSTBACK_TOKEN"],
                token,
                database,
                state,
                uniqueId,
                async (userId, hash) =>
                {
                    await GrantAdReward(userId, hash);
                    await database.StringSetAsync(resultKey, "success", TimeSpan.FromHours(4));
                });
            if (!granted)
            {
                if (await database.StringGetAsync(resultKey) == "success")
                    return Ok();
                logger.LogWarning("Lootlabs postback did not claim a pending session");
                return BadRequest();
            }
            logger.LogInformation("Lootlabs postback completed a pending ad session");
            return Ok();
        }

        internal static bool TryNormalizeAdProvider(string provider, out string normalized)
        {
            normalized = provider?.Trim().ToLowerInvariant();
            return normalized is "linkvertise" or "lootlabs";
        }

        internal static bool IsValidAdCompletionToken(string token) =>
            token?.Length == 64;

        internal static bool IsValidLootlabsPostbackToken(string expected, string supplied)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(expected ?? "");
            var suppliedBytes = Encoding.UTF8.GetBytes(supplied ?? "");
            return expectedBytes.Length >= 32
                && expectedBytes.Length == suppliedBytes.Length
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    expectedBytes,
                    suppliedBytes);
        }

        internal static string GetLootlabsCompletionHash(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId) || uniqueId.Length > 256)
                return null;
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(uniqueId)))
                .ToLowerInvariant();
        }

        internal static Task<bool> TryCompleteLootlabsPostback(
            string expectedToken,
            string suppliedToken,
            IDatabase database,
            string state,
            string uniqueId,
            Func<string, string, Task> grant)
        {
            var completionHash = GetLootlabsCompletionHash(uniqueId);
            if (!IsValidLootlabsPostbackToken(expectedToken, suppliedToken)
                || !IsValidAdCompletionToken(state)
                || completionHash == null)
                return Task.FromResult(false);
            return TryCompleteAdSession(
                true,
                database,
                "lootlabs",
                state,
                completionHash,
                userId => grant(userId, completionHash));
        }

        internal static bool IsSuccessfulLinkvertiseResponse(string response) =>
            string.Equals(response?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        internal static bool IsRecentAdReward(
            string productId,
            string reference,
            DateTime timestamp,
            DateTime now) =>
            productId is "compensation" or "starter_premium-hour"
            && (reference?.StartsWith("ad-", StringComparison.Ordinal) == true
                || reference?.StartsWith("ap-", StringComparison.Ordinal) == true)
            && timestamp > now.AddMinutes(-50);

        internal static RedisKey GetAdStateKey(string provider, string state) =>
            $"ad-link:state:{provider}:{state}";

        internal static RedisKey GetAdResultKey(string provider, string state) =>
            $"ad-link:result:{provider}:{state}";

        private static async Task<bool> WaitForAdResult(IDatabase database, RedisKey resultKey)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (await database.StringGetAsync(resultKey) == "success")
                    return true;
                await Task.Delay(500);
            }
            return false;
        }

        internal static async Task<bool> TryCompleteAdSession(
            bool providerConfirmed,
            IDatabase database,
            string provider,
            string state,
            string hash,
            Func<string, Task> grant)
        {
            if (!providerConfirmed)
                return false;
            var stateKey = GetAdStateKey(provider, state);
            var userId = await database.StringGetAsync(stateKey);
            if (userId.IsNull)
                return false;

            var completionKey = (RedisKey)$"ad-link:completion:{provider}:{hash}";
            var transaction = database.CreateTransaction();
            transaction.AddCondition(Condition.StringEqual(stateKey, userId));
            transaction.AddCondition(Condition.KeyNotExists(completionKey));
            _ = transaction.KeyDeleteAsync(stateKey);
            // Keep the completion claim permanently: provider hashes must never credit a second session.
            _ = transaction.StringSetAsync(completionKey, userId);
            if (!await transaction.ExecuteAsync())
                return false;

            try
            {
                await grant(userId.ToString());
                return true;
            }
            catch
            {
                var rollback = database.CreateTransaction();
                rollback.AddCondition(Condition.StringEqual(completionKey, userId));
                rollback.AddCondition(Condition.KeyNotExists(stateKey));
                _ = rollback.KeyDeleteAsync(completionKey);
                _ = rollback.StringSetAsync(stateKey, userId, TimeSpan.FromHours(4));
                await rollback.ExecuteAsync();
                throw;
            }
        }

        /// <summary>
        /// Encrypts the reward destination with the lootlabs api key and returns the anti-bypass
        /// content locker link the user has to complete before getting redirected back.
        /// </summary>
        private async Task<string> CreateLootlabsRedirect(string state, HttpClient httpClient)
        {
            var apiToken = configuration["LOOTLABS_API_TOKEN"];
            var lockerUrl = configuration["LOOTLABS_LOCKER_URL"];
            var postbackToken = configuration["LOOTLABS_POSTBACK_TOKEN"];
            if (string.IsNullOrEmpty(apiToken)
                || string.IsNullOrEmpty(lockerUrl)
                || Encoding.UTF8.GetByteCount(postbackToken ?? "") < 32)
                throw new CoflnetException("lootlabs_unconfigured", "lootlabs is not configured on this server");
            var destination = $"https://sky.coflnet.com/api/linkvertise?provider=lootlabs&state={state}";
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    destination_url = destination,
                    api_token = apiToken
                }),
                Encoding.UTF8,
                "application/json");
            var response = await httpClient.PostAsync(
                "https://creators.lootlabs.gg/api/public/url_encryptor",
                content);
            var responseString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Could not create lootlabs link {status} {body}", response.StatusCode, responseString);
                throw new CoflnetException("lootlabs_error", "Could not create lootlabs link, please try again later");
            }
            // the returned message is the aes encrypted destination and already url encoded
            var encrypted = System.Text.Json.JsonDocument.Parse(responseString).RootElement.GetProperty("message").GetString();
            return $"{lockerUrl}&puid={state}&data={encrypted}";
        }

        private async Task GrantAdReward(string userId, string hash)
        {
            var transactions = await transactionApi.TransactionUUserIdGetAsync(userId, 2);
            if (transactions.Any(t => IsRecentAdReward(
                    t.ProductId,
                    t.Reference,
                    t.TimeStamp,
                    DateTime.UtcNow)))
                return;

            logger.LogInformation("Granting ad reward to session user {userId}", userId);
            await topUpApi.TopUpCustomPostAsync(userId, new CustomTopUp()
            {
                Amount = 4,
                ProductId = "compensation",
                Reference = "ad-" + hash
            });
            await userApi.UserUserIdServicePurchaseProductSlugPostAsync(
                userId,
                "starter_premium-hour",
                "ap-" + hash,
                1);
        }

        /// <summary>
        /// Deterministic per session and hour token used to verify a lootlabs completion.
        /// The destination url carrying it is aes encrypted by lootlabs, so a user only learns a
        /// valid token after actually completing the offer and can not forge one for another account.
        /// </summary>
        private static string GetLootlabsToken(string state, long hourBucket, string apiToken)
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(apiToken));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{state}|{hourBucket}"));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static bool VerifyLootlabsToken(string state, string token, string apiToken)
        {
            if (string.IsNullOrEmpty(state) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(apiToken))
                return false;
            var currentBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600;
            // accept the current and the two previous hours so the user has time to finish the offer
            for (var bucket = currentBucket; bucket >= currentBucket - 2; bucket--)
            {
                var expected = GetLootlabsToken(state, bucket, apiToken);
                if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
                    return true;
            }
            return false;
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
            if (await MustRejectNewContract(user.Id, configuration))
                return TermsAcceptanceRequired();
            try
            {
                var reference = args.reference;
                var count = args.count == 0 ? 1 : args.count;
                if (string.IsNullOrEmpty(reference))
                    reference = "apiautofill" + DateTime.UtcNow;
                if (!UsesDeclaredPurchase(args))
                {
                    await userApi.UserUserIdServicePurchaseProductSlugPostAsync(
                        user.Id.ToString(),
                        args.slug,
                        reference,
                        count);
                    return Ok();
                }
                if (MustRejectDeclaredPurchase(
                        await HasCurrentAgreement(user.Id)))
                    return TermsAcceptanceRequired();
                var requestedLocale = string.IsNullOrWhiteSpace(
                    args.legalLocale)
                        ? GetLocale()
                        : args.legalLocale;
                var legalLocale = requestedLocale.StartsWith(
                    "de",
                    StringComparison.OrdinalIgnoreCase)
                        ? "de"
                        : "en";
                var declaration = legalManifest.PremiumEarlyStart
                    ?? throw new InvalidOperationException(
                        "The Premium declaration is unavailable.");
                var agreement = legalManifest.Agreement
                    ?? throw new InvalidOperationException(
                        "The SkyCofl agreement identity is unavailable.");
                var withdrawal = legalManifest.Withdrawal
                    ?? throw new InvalidOperationException(
                        "The withdrawal identity is unavailable.");
                if (!string.Equals(
                        args.declarationVersion,
                        declaration.Version,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The Premium declaration changed. Review it and try again.");
                await userApi
                    .UserUserIdServicePurchaseDeclaredProductSlugPostAsync(
                        user.Id.ToString(),
                        args.slug,
                        new ServicePurchaseRequest(
                            reference: reference,
                            count: count,
                            immediatePerformanceRequested:
                                args.immediatePerformanceRequested ?? false,
                            withdrawalConsequenceAcknowledged:
                                args.withdrawalConsequenceAcknowledged ?? false,
                            locale: legalLocale,
                            declarationVersion: declaration.Version,
                            declarationText: declaration.Locales[legalLocale],
                            declarationSha256: declaration.Sha256[legalLocale],
                            agreementId: agreement.Id,
                            agreementHash: agreement.Hash,
                            withdrawalVersion: withdrawal.Version,
                            withdrawalSha256: withdrawal.Sha256[legalLocale],
                            requestId: args.declarationRequestId));
                return Ok();
            }
            catch (Exception e)
            {
                throw new CoflnetException("payment_error", e.Message);
            }
        }

        internal static bool UsesDeclaredPurchase(PurchaseArgs args) =>
            !string.IsNullOrWhiteSpace(args?.declarationRequestId);

        internal static bool MustRejectDeclaredPurchase(bool hasCurrentAgreement) =>
            !TermsAcceptancePolicy.IsCurrent(hasCurrentAgreement);

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
            if (await MustRejectNewContract(user.Id, configuration))
                return TermsAcceptanceRequired();
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
            if (await MustRejectNewContract(user.Id, configuration))
                return TermsAcceptanceRequired();
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

        private ObjectResult TermsAcceptanceRequired()
        {
            if (!TermsAcceptancePolicy.IsEffective())
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    slug = "terms_publication_unavailable",
                    message = "New purchases and subscription changes are temporarily unavailable because the Terms publication time is not configured."
                });
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                slug = "terms_acceptance_required",
                message = "Accept the current SkyCofl Agreement at https://sky.coflnet.com/premium before starting a new purchase, top-up, upgrade or subscription reactivation.",
                agreementId = TermsAcceptancePolicy.CurrentAgreementId,
                version = TermsAcceptancePolicy.CurrentVersion,
                hash = TermsAcceptancePolicy.CurrentHash,
                agreementUrl = TermsAcceptancePolicy.CurrentAgreementUrl
            });
        }

        private static async Task<bool> MustRejectNewContract(
            int userId,
            IConfiguration configuration) =>
            MustRejectNewContract(
                await HasCurrentAgreement(userId),
                configuration);

        internal static bool MustRejectNewContract(
            bool hasCurrentAgreement,
            IConfiguration configuration) =>
            configuration?.GetValue<bool>(
                "LEGAL:ENFORCE_CURRENT_TERMS") == true
            && !TermsAcceptancePolicy.CanStartNewContract(hasCurrentAgreement);

        private static async Task<bool> HasCurrentAgreement(int userId)
        {
            if (string.IsNullOrEmpty(TermsAcceptancePolicy.CurrentHash))
                return false;
            return await UserService.Instance.GetAgreementAcceptance(
                userId,
                TermsAcceptancePolicy.CurrentAgreementId,
                TermsAcceptancePolicy.CurrentHash) != null;
        }

        [HttpPut]
        [Route("premium/subscription/{externalId}/switch")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<ActionResult> SwitchSubscriptionTier(string externalId, [FromQuery] string targetProductSlug)
        {
            var user = await GetUserOrDefault();
            if (user == default)
                return Unauthorized("no googletoken header");
            throw new CoflnetException("not_implemented", "Switching subscription tiers is not yet implemented");
            return Ok();
        }

        private async Task<GoogleUser?> GetUserOrDefault(bool isPurchase = false)
        {
            if (!Request.Headers.TryGetValue("GoogleToken", out StringValues value)
                && !Request.Headers.TryGetValue("Authorization", out value))
                return null;
            return await tokenService.GetUserWithToken(value, isPurchase);
        }

        /// <summary>
        /// Check if the caller's IP is currently blacklisted
        /// </summary>
        [HttpGet]
        [Route("blacklist/status")]
        public IActionResult GetBlacklistStatus([FromServices] IScrapingDetectionService scrapingDetector)
        {
            var ip = GetClientIp();
            var banned = !string.IsNullOrEmpty(ip) && scrapingDetector.IsIpBanned(ip);
            return Ok(new { ip, banned });
        }

        /// <summary>
        /// Unblock the caller's IP. Requires Premium+ subscription.
        /// </summary>
        [HttpPost]
        [Route("blacklist/unblock")]
        public async Task<IActionResult> UnblockIp([FromServices] IScrapingDetectionService scrapingDetector, [FromServices] PremiumTierService premiumTierService)
        {
            if (!await premiumTierService.HasPremiumPlus(this))
                return StatusCode(403, new { error = "premium_plus_required", message = "You need an active Premium+ subscription to unblock your IP.", premiumUrl = "https://sky.coflnet.com/premium" });

            var ip = GetClientIp();
            if (string.IsNullOrEmpty(ip))
                return BadRequest(new { error = "no_ip", message = "Could not determine your IP address." });

            var wasUnbanned = scrapingDetector.UnbanIp(ip);
            return Ok(new { ip, unblocked = true, wasBanned = wasUnbanned });
        }

        private string GetClientIp()
        {
            if (Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp))
                return cfIp.ToString().Split(',').First().Trim();
            if (Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
                return xff.ToString().Split(',').First().Trim();
            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }
    }
}

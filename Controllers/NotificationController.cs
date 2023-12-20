using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.EventBroker.Client.Api;
using Coflnet.Sky.EventBroker.Client.Model;
using Coflnet.Sky.Subscriptions.Client.Api;
using Coflnet.Sky.Subscriptions.Client.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller;

[ApiController]
[Route("api/notifications")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class NotificationController : ControllerBase
{
    private readonly ILogger<NotificationController> logger;
    private readonly GoogletokenService googletokenService;
    private readonly ISubscriptionsApi subscriptionsApi;
    private readonly ISubscriptionApi listenerApi;
    
    private readonly ITargetsApi targetsApi;

    /// <summary>
    /// Creates a new instance of <see cref="NotificationController"/>
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="googletokenService"></param>
    /// <param name="subscriptionsApi"></param>
    /// <param name="targetsApi"></param>
    /// <param name="listenerApi"></param>
    public NotificationController(ILogger<NotificationController> logger,
                           GoogletokenService googletokenService,
                           ISubscriptionsApi subscriptionsApi,
                           ITargetsApi targetsApi,
                           ISubscriptionApi listenerApi)
    {
        this.logger = logger;
        this.googletokenService = googletokenService;
        this.subscriptionsApi = subscriptionsApi;
        this.targetsApi = targetsApi;
        this.listenerApi = listenerApi;
    }

    /// <summary>
    /// Returns notification targets of the user
    /// </summary>
    [Route("targets")]
    [HttpGet]
    public async Task<List<NotificationTarget>> GetNotifications()
    {
        return await targetsApi.TargetsUserIdGetAsync(await googletokenService.GetUserId(this));
    }

    /// <summary>
    /// Returns notification subscriptions of the user
    /// </summary>
    [Route("subscriptions")]
    [HttpGet]
    public async Task<List<PublicSubscription>> GetSubscriptions()
    {
        return await subscriptionsApi.SubscriptionsGetAsync(await googletokenService.GetUserId(this));
    }

    /// <summary>
    /// Adds a new subscription
    /// </summary>
    /// <param name="subscription"></param>
    /// <returns></returns>
    [Route("subscriptions")]
    [HttpPost]
    public async Task<PublicSubscription> AddSubscription(PublicSubscription subscription)
    {
        await subscriptionsApi.SubscriptionsPostAsync(await googletokenService.GetUserId(this), subscription);
        return subscription; // TODO: return the created subscription
    }

    /// <summary>
    /// Removes a subscription
    /// </summary>
    /// <param name="subscription"></param>
    /// <returns></returns>
    [Route("subscriptions")]
    [HttpDelete]
    public async Task RemoveSubscription(PublicSubscription subscription)
    {
        await subscriptionsApi.SubscriptionsDeleteAsync(await googletokenService.GetUserId(this), subscription);
    }

    /// <summary>
    /// Adds a new target
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    [Route("targets")]
    [HttpPost]
    public async Task<NotificationTarget> AddTarget(NotificationTarget target)
    {
        await targetsApi.TargetsUserIdPostAsync(await googletokenService.GetUserId(this), target);
        return target; // TODO: return the created target
    }

    /// <summary>
    /// Removes a target
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    [Route("targets")]
    [HttpDelete]
    public async Task RemoveTarget(NotificationTarget target)
    {
        await targetsApi.TargetsUserIdDeleteAsync(await googletokenService.GetUserId(this), target);
    }

    /// <summary>
    /// Updates a target
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    [Route("targets")]
    [HttpPut]
    public async Task UpdateTarget(NotificationTarget target)
    {
        await targetsApi.TargetsUserIdPutAsync(await googletokenService.GetUserId(this), target);
    }

    /// <summary>
    /// Sends a test notification to the given target
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    [Route("targets/test")]
    [HttpPost]
    public async Task SendTestNotification(NotificationTarget target)
    {
        await targetsApi.TargetsUserIdTestPostAsync(await googletokenService.GetUserId(this), target);
    }

    /// <summary>
    /// Updates a subscription
    /// </summary>
    /// <param name="subscription"></param>
    /// <returns></returns>
    [Route("subscriptions")]
    [HttpPut]
    public async Task UpdateSubscription(PublicSubscription subscription)
    {
        await subscriptionsApi.SubscriptionsPutAsync(await googletokenService.GetUserId(this), subscription);
    }

    /// <summary>
    /// Adds a new listener
    /// </summary>
    /// <param name="listener"></param>
    /// <returns></returns>
    [Route("listeners")]
    [HttpPost]
    public async Task<Subscription> AddListener(Subscription listener)
    {
        return await listenerApi.SubscriptionUserIdSubPostAsync(await googletokenService.GetUserId(this), listener);
    }

    /// <summary>
    /// Lists all listeners
    /// </summary>
    /// <returns></returns>
    /// <response code="200">Returns the listeners</response>
    [Route("listeners")]
    [HttpGet]
    public async Task<List<Subscription>> ListListeners()
    {
        return (await listenerApi.SubscriptionUserIdGetAsync(await googletokenService.GetUserId(this))).Subscriptions;
    }

    /// <summary>
    /// Removes a listener
    /// </summary>
    /// <param name="listener"></param>
    /// <returns></returns>
    [Route("listeners")]
    [HttpDelete]
    public async Task RemoveListener(Subscription listener)
    {
        await listenerApi.SubscriptionUserIdSubDeleteAsync(await googletokenService.GetUserId(this), listener);
    }
}


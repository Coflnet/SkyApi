using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Notifications;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Core;
using Coflnet.Sky.EventBroker.Client.Api;
using Coflnet.Sky.EventBroker.Client.Model;
using Coflnet.Sky.Sniper.Client.Model;
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
    private readonly AutoMapper.IMapper mapper;

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
                           ISubscriptionApi listenerApi,
                           AutoMapper.IMapper mapper)
    {
        this.logger = logger;
        this.googletokenService = googletokenService;
        this.subscriptionsApi = subscriptionsApi;
        this.targetsApi = targetsApi;
        this.listenerApi = listenerApi;
        this.mapper = mapper;
    }

    /// <summary>
    /// Returns notification targets of the user
    /// </summary>
    [Route("targets")]
    [HttpGet]
    public async Task<List<Models.Notifications.NotificationTarget>> GetNotifications()
    {
        var result = await targetsApi.TargetsUserIdGetAsync(await googletokenService.GetUserId(this));
        return result.Select(x => mapper.Map<Models.Notifications.NotificationTarget>(x)).ToList();
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
    public async Task<PublicSubscription> AddSubscription(PublicSubscription subscription, [FromServices] PremiumTierService premiumTierService)
    {
        var userId = await googletokenService.GetUserId(this);
        var existing = await subscriptionsApi.SubscriptionsGetAsync(userId);
        var count = existing.Count;
        if (count >= 10 && !await premiumTierService.HasPremium(this))
        {
            throw new CoflnetException("subscription_limit_reached", "You have reached the limit of 10 subscriptions. Please upgrade to Premium to add more.");
        }
        if (count >= 3 && !await premiumTierService.HasStarterPremium(this))
        {
            throw new CoflnetException("subscription_limit_reached", "You have reached the limit of 3 subscriptions. Please upgrade to (starter) Premium to add more.");
        }
        return await subscriptionsApi.SubscriptionsPostAsync(userId, subscription);
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
    public async Task<Models.Notifications.NotificationTarget> AddTarget(Models.Notifications.NotificationTarget target)
    {
        var userId = await googletokenService.GetUserId(this);
        var mapped = mapper.Map<EventBroker.Client.Model.NotificationTarget>(target);
        if(mapped.Type == TargetType.FIREBASE)
        {
            // drop the old token if one exists
            await listenerApi.SubscriptionUserIdDeviceDeleteAsync(userId);
        }
        var result = await targetsApi.TargetsUserIdPostAsync(userId, mapped);
        return mapper.Map<Models.Notifications.NotificationTarget>(result);
    }

    /// <summary>
    /// Removes a target
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    [Route("targets")]
    [HttpDelete]
    public async Task RemoveTarget(Models.Notifications.NotificationTarget target)
    {
        var mapped = mapper.Map<EventBroker.Client.Model.NotificationTarget>(target);
        await targetsApi.TargetsUserIdDeleteAsync(await googletokenService.GetUserId(this), mapped);
    }

    /// <summary>
    /// Updates a target
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    [Route("targets")]
    [HttpPut]
    public async Task UpdateTarget(Models.Notifications.NotificationTarget target)
    {
        var mapped = mapper.Map<EventBroker.Client.Model.NotificationTarget>(target);
        await targetsApi.TargetsUserIdPutAsync(await googletokenService.GetUserId(this), mapped);
    }

    /// <summary>
    /// Sends a test notification to the given target
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    [Route("targets/test")]
    [HttpPost]
    public async Task SendTestNotification(Models.Notifications.NotificationTarget target)
    {
        var mapped = mapper.Map<EventBroker.Client.Model.NotificationTarget>(target);
        await targetsApi.TargetsUserIdTestPostAsync(await googletokenService.GetUserId(this), mapped);
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
    public async Task<Listener> AddListener(Listener listener)
    {
        var subscription = mapper.Map<Subscription>(listener);
        var result = await listenerApi.SubscriptionUserIdSubPostAsync(await googletokenService.GetUserId(this), subscription);
        return mapper.Map<Listener>(result);
    }

    /// <summary>
    /// Lists all listeners
    /// </summary>
    /// <returns></returns>
    /// <response code="200">Returns the listeners</response>
    [Route("listeners")]
    [HttpGet]
    public async Task<List<Listener>> ListListeners()
    {
        var result = (await listenerApi.SubscriptionUserIdGetAsync(await googletokenService.GetUserId(this))).Subscriptions;
        return result.Select(x => mapper.Map<Listener>(x)).ToList();
    }

    /// <summary>
    /// Removes a listener
    /// </summary>
    /// <param name="listener"></param>
    /// <returns></returns>
    [Route("listeners")]
    [HttpDelete]
    public async Task RemoveListener(Listener listener)
    {
        var mapped = mapper.Map<Subscription>(listener);
        await listenerApi.SubscriptionUserIdSubDeleteAsync(await googletokenService.GetUserId(this), mapped);
    }
}


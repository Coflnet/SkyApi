using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName;
using Coflnet.Sky.Proxy.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services;

public class AhListChecker
{
    private Coflnet.Sky.Proxy.Client.Api.IBaseApi proxyApi;
    private PlayerName.PlayerNameService playerNameService;
    private ILogger<AhListChecker> logger;

    public AhListChecker(IBaseApi proxyApi, PlayerNameService playerNameService, ILogger<AhListChecker> logger)
    {
        this.proxyApi = proxyApi;
        this.playerNameService = playerNameService;
        this.logger = logger;
    }

    public void CheckItems(IEnumerable<Item> items, string playerId)
    {
        foreach (var item in items)
        {
            if (item?.Description == null)
                continue;
            if (!item.Description.Contains("05h 59m 5") && !item.Description.Contains("Can buy in"))
                continue;
            var sellerName = item.Description.Split('\n')
                    .Where(x => x.StartsWith("§7Seller:"))
                    .FirstOrDefault()?.Replace("§7Seller: §7", "")
                    .Split(' ').Last(); // skip rank prefix
            if (sellerName == null)
            {
                continue;
            }
            if (sellerName == "Refreshing...")
            {
                logger.LogInformation($"got a refreshing item {item.ItemName} from {playerId}");
                continue;
            }
            Task.Run(async () =>
            {
                try
                {
                    var uuid = await playerNameService.GetUuid(sellerName);
                    Console.WriteLine("Checking listings for " + sellerName + " uuid " + uuid + " " + item.ItemName + " from: " + playerId);
                    var info = await proxyApi.BaseAhPlayerIdPostWithHttpInfoAsync(uuid, playerId.Contains(':') ? playerId : $"player: {playerId}");
                    if (info.StatusCode == 0)
                        logger.LogError($"Failed to check ah listings from {sellerName} because proxy unavailable");
                    if (info.StatusCode != System.Net.HttpStatusCode.OK)
                        logger.LogError($"Failed to check ah listings from {sellerName} because proxy returned {info.StatusCode}");
                }
                catch (System.Exception e)
                {
                    logger.LogError(e, $"Failed to check ah listings from {sellerName}");
                }
            });
        }
    }
}

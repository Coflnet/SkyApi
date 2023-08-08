using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.PlayerName;
using Coflnet.Sky.PlayerName.Client.Api;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using OpenTracing;
using System.IO;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerName.Client.Client;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Core;
using System.Linq;

namespace SkyApi.Services.Description
{
    [TestFixture]
    public class ModDescriptionServiceTests
    {

        [Test]
        async public Task AddCoinsPerBitValue_ValidPriceAndDescription_CorrectCoinsPerBitAdded()
        {
            // Arrange

            IConfiguration configuration = new Mock<IConfiguration>().Object;
            ISettingsApi settingsApi = new Mock<ISettingsApi>().Object;
            SettingsService settingsService = new(configuration, Mock.Of<ILogger<SettingsService>>(), settingsApi);

            ISynchronousClient synchronousClient = new Mock<ISynchronousClient>().Object;
            IAsynchronousClient asynchronousClient = new Mock<IAsynchronousClient>().Object;
            IReadableConfiguration readableConfiguration = new Mock<IReadableConfiguration>().Object;
            PlayerNameApi playerNameApi = new PlayerNameApi(synchronousClient, asynchronousClient, readableConfiguration);

            IItemsApi itemsApi = new Mock<IItemsApi>().Object;
            ItemSkinHandler itemSkinHandler = new ItemSkinHandler(itemsApi);

            PlayerNameService playerNameService = new(playerNameApi, Mock.Of<ILogger<PlayerNameService>>());

            var price = new Coflnet.Sky.Sniper.Client.Model.PriceEstimate { Median = 1000000, ItemKey = "item_key", MedianKey = "median_key" };

            var sniperClient = new Mock<ISniperClient>();
            sniperClient.Setup(s => s.GetPrices(It.IsAny<IEnumerable<SaveAuction>>())).Returns<IEnumerable<SaveAuction>>(s => Task.FromResult(Enumerable.Repeat(price, s.Count()).ToList()));

            ModDescriptionService modDescriptionService = new(Mock.Of<ICraftsApi>(), Mock.Of<ITracer>(), settingsService, Mock.Of<IdConverter>(), Mock.Of<IServiceScopeFactory>(),
                Mock.Of<BazaarApi>(), playerNameService, Mock.Of<ILogger<ModDescriptionService>>(), Mock.Of<IConfiguration>(), Mock.Of<IStateUpdateService>(), sniperClient.Object,
                null, itemSkinHandler);

            // Act
            var res = await modDescriptionService.GetModifications(GetMockInventory(), "test", "test");
            var result = res.ToList();

            // Assert
            var expectedResult = $"§7Coins per bit: §b~740.0 ";

            //At element 20 we have "KISMET_FEATHER" worth 1350 bits, expected value should be 100000/1350 = 740
            Assert.AreEqual(expectedResult, result[20].ElementAt(1).Value);
        }

        InventoryDataWithSettings GetMockInventory()
        {
            string mockInventoryFile = File.ReadAllText(@".\MockObjects\CommunityShop_InventoryDataWithSettings.json");
            return JsonConvert.DeserializeObject<InventoryDataWithSettings>(mockInventoryFile);
        }
    }
}

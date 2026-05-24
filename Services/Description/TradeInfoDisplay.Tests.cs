using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.PlayerName;
using Coflnet.Sky.PlayerName.Client.Api;
using Coflnet.Sky.PlayerName.Client.Client;
using Coflnet.Sky.Settings.Client.Api;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests;

public class TradeInfoDisplayTests
{
    [Test]
    public void Apply_LowballMode_BuildsReadableBreakdownWithAiEstimateAndSettingsCommands()
    {
        var modifier = new TradeInfoDisplay();
        var modService = CreateModService();

        var settings = new DescriptionSetting
        {
            DisableSuggestions = true,
            LowballMedUndercut = 10,
            LowballLbinUndercut = 10,
            LowballNonExactExtraPct = 4,
            LowballWorstCaseExtraPct = 6,
            LowballHideBreakdown = false,
            LowballHideWorstCase = false,
            Fields = DescriptionSetting.Default.Fields
        };

        var items = Enumerable.Range(0, 40)
            .Select(i => new Item
            {
                ItemName = $"\u00a7fItem {i}",
                Tag = $"ITEM_{i}",
                Count = 1
            })
            .ToList();

        items[5] = new Item { ItemName = "\u00a7aExact Blade", Tag = "EXACT_BLADE", Count = 1 };
        items[6] = new Item { ItemName = "\u00a7bFuzzy Bow", Tag = "FUZZY_BOW", Count = 1 };

        var prices = Enumerable.Repeat<Sniper.Client.Model.PriceEstimate>(null, 40).ToList();
        prices[5] = new Sniper.Client.Model.PriceEstimate
        {
            Median = 10_000_000,
            Volume = 2f,
            ItemKey = "EXACT_BLADE",
            MedianKey = "EXACT_BLADE",
            SelfLearningEstimatedValue = 12_345_678
        };
        prices[6] = new Sniper.Client.Model.PriceEstimate
        {
            Median = 8_000_000,
            Volume = 0.3f,
            ItemKey = "FUZZY_BOW",
            MedianKey = "SOME_OTHER_ITEM",
            SelfLearningEstimatedValue = 2_000_000
        };

        var data = new DataContainer
        {
            inventory = new InventoryDataWithSettings { ChestName = "You    test", Settings = settings },
            Items = items,
            PriceEst = prices,
            bazaarPrices = ImmutableDictionary<string, Coflnet.Sky.Bazaar.Client.Model.ItemPrice>.Empty,
            mods = Enumerable.Range(0, 40).Select(_ => new List<DescModification>()).ToList(),
            accountInfo = new AccountInfo
            {
                Tier = AccountTier.PREMIUM,
                ExpiresAt = DateTime.UtcNow.AddDays(2)
            }
        };
        data.modService = modService;

        modifier.Apply(data);

        var addedLowballPanel = data.mods.Last();
        addedLowballPanel.Should().NotBeEmpty();
        addedLowballPanel.Select(x => x.Value).Should().Contain(v => v.Contains("Looks like you are lowballing"));

        var recommendationHover = GetHoverByText(addedLowballPanel, "SkyCofl recommended");
        recommendationHover.Should().NotBeNullOrWhiteSpace();
        recommendationHover.Should().Contain("Calculation: price * (100 - undercut)%");
        recommendationHover.Should().Contain("Items valued: 2");
        recommendationHover.Should().Contain("Worst-case:");
        recommendationHover.Should().Contain("AI estimate:");
        recommendationHover.Should().Contain(ModDescriptionService.FormatPriceShort(14_345_678));
        recommendationHover.Should().Contain("Exact Blade");
        recommendationHover.Should().Contain("Fuzzy Bow");
        recommendationHover.Should().Contain("item(s) used AI due to no exact price match");
        recommendationHover.Should().Contain("~");

        var settingsHover = GetHoverByText(addedLowballPanel, "Lowball detail settings");
        settingsHover.Should().Contain("/cofl set lorelbNonExactPct <0-10>");
        settingsHover.Should().Contain("/cofl set lorelbWorstCasePct <0-15>");
        settingsHover.Should().Contain("/cofl set lorelbHideBreakdown true");
        settingsHover.Should().Contain("/cofl set lorelbHideWorstCase true");
    }

    private static string GetHoverByText(IEnumerable<DescModification> lines, string lineText)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Value) || !line.Value.StartsWith("["))
                continue;

            var components = JsonSerializer.Deserialize<List<LoreComponent>>(line.Value);
            var component = components?.FirstOrDefault(c => (c.Text?.Contains(lineText) ?? false));
            if (!string.IsNullOrWhiteSpace(component?.Hover))
                return component.Hover;
        }

        return string.Empty;
    }

    private static ModDescriptionService CreateModService()
    {
        var settingsService = new SettingsService(
            Mock.Of<IConfiguration>(),
            Mock.Of<ILogger<SettingsService>>(),
            Mock.Of<ISettingsApi>());

        var playerNameApi = new PlayerNameApi(
            Mock.Of<ISynchronousClient>(),
            Mock.Of<IAsynchronousClient>(),
            Mock.Of<IReadableConfiguration>());

        var playerNameService = new PlayerNameService(
            playerNameApi,
            Mock.Of<ILogger<PlayerNameService>>());

        var itemSkinHandler = new ItemSkinHandler(Mock.Of<IItemsApi>());

        return new ModDescriptionService(
            Mock.Of<ICraftsApi>(),
            settingsService,
            Mock.Of<IdConverter>(),
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<BazaarApi>(),
            playerNameService,
            Mock.Of<ILogger<ModDescriptionService>>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<IStateUpdateService>(),
            Mock.Of<ISniperClient>(),
            itemSkinHandler,
            new(null, null, null),
            null,
            null,
            null,
            null,
            null);
    }
}

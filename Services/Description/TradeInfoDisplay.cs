using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Compares value of receiving and sending in trade menu and suggests lowball prices
/// </summary>
public class TradeInfoDisplay : ICustomModifier
{
    /// <inheritdoc />
    public void Apply(DataContainer data)
    {
        var index = 0;
        long sendSum = 0L;
        long receiveSum = 0L;
        long receiveCount = 0L;
        var likelyLowballing = true;
        var lowballPrice = 0L;
        var worstCaseLowballPrice = 0L;
        long aiReceiveSum = 0L;
        var settings = data.inventory.Settings;
        var showBreakdown = !settings.LowballHideBreakdown;
        var showWorstCase = !settings.LowballHideWorstCase;
        var nonExactPct = (int)settings.LowballNonExactExtraPct;
        var worstCasePct = (int)settings.LowballWorstCaseExtraPct;
        var itemBreakdown = new List<LowballItemBreakdown>();
        foreach (var sniperPrice in data.PriceEst)
        {
            var i = index++;
            if (i >= 36)
                break;
            var column = i % 9;
            long value = 0;
            var item = data.Items[i];
            if (item.ItemName?.EndsWith(" coins") ?? false)
            {
                var name = item.ItemName;
                value = ParseCoinAmount(name.Substring(2, name.Length - 8));
            }
            else if (sniperPrice != null && sniperPrice.Median != 0)
            {
                value = sniperPrice.Median;
            }
            else if (item?.Tag != null && (data.bazaarPrices?.TryGetValue(item.Tag, out var price) ?? false))
            {
                value = (long)price.SellPrice * item.Count;
            }
            if (column < 4)
            {
                sendSum += value;
                if (sniperPrice?.Median > 0)
                    likelyLowballing = false;
            }
            else if (column > 4)
            {
                receiveSum += value;
                if (value > 0)
                    receiveCount++;
                var volume = sniperPrice?.Volume ?? 0;
                var aiVal = (long)(sniperPrice?.SelfLearningEstimatedValue ?? 0);
                aiReceiveSum += aiVal;
                var medianEstimate = GetAdjustedValueBreakdown(settings.LowballMedUndercut, sniperPrice?.Median ?? 0, volume,
                    IsExactMatch(sniperPrice?.MedianKey, sniperPrice?.ItemKey), nonExactPct, showWorstCase ? worstCasePct : 0);
                var lbinEstimate = GetAdjustedValueBreakdown(settings.LowballLbinUndercut, sniperPrice?.Lbin?.Price ?? 0, volume,
                    IsExactMatch(sniperPrice?.LbinKey, sniperPrice?.ItemKey), nonExactPct, showWorstCase ? worstCasePct : 0);

                var useLbin = lbinEstimate.Recommended > 0 && (medianEstimate.Recommended == 0 || lbinEstimate.Recommended < medianEstimate.Recommended);
                var selectedEstimate = useLbin ? lbinEstimate : medianEstimate;
                lowballPrice += selectedEstimate.Recommended;
                worstCaseLowballPrice += selectedEstimate.WorstCase;

                if (showBreakdown && selectedEstimate.Recommended > 0)
                {
                    itemBreakdown.Add(new LowballItemBreakdown(
                        Regex.Replace(item?.ItemName ?? item?.Tag ?? $"slot-{i}", "§.", string.Empty),
                        volume,
                        medianEstimate,
                        lbinEstimate,
                        useLbin ? "lbin" : "median",
                        aiVal));
                }
            }
        }
        Console.WriteLine($"trade warning send: {sendSum} receive: {receiveSum}");
        Console.WriteLine(JsonConvert.SerializeObject(data.Items));
        data.mods[39].Add(new($"Send value: {data.modService.FormatNumber(sendSum)}"));
        data.mods[39].Add(new($"Receive value: {data.modService.FormatNumber(receiveSum)}"));
        data.mods[39].Add(new($"CoflMod estimate, please report issues"));
        if (receiveSum < sendSum / 2)
        {
            data.mods[39].Insert(0, new(DescModification.ModType.REPLACE, 0, $"{McColorCodes.RED}You are sending way more coins"));
            data.mods[39].Insert(0, new(DescModification.ModType.INSERT, 1, $"{McColorCodes.RED}than you are receiving! {McColorCodes.OBFUSCATED}A"));
        }
        if (receiveSum == 0)
        {
            return;
        }
        if (data.inventory.Settings.DisableInfoIn?.Contains("Trade") ?? false)
            return;
        if (!likelyLowballing)
        {
            var youEarnPercent = (int)(100 * (receiveSum - sendSum) / (double)receiveSum);
            var valueDisplay = new List<DescModification>()
            {
                new ("SkyCofl price comparison"),
                new ($"Receive {McColorCodes.GOLD}{data.modService.FormatNumber(receiveSum)}{McColorCodes.GRAY} coins"),
                new ($"Send {McColorCodes.GOLD}{data.modService.FormatNumber(sendSum)}{McColorCodes.GRAY} coins"),
                new ($"You earn {McColorCodes.GREEN}{youEarnPercent}%{McColorCodes.GRAY}"),
                new ($"{McColorCodes.GRAY}Hover items to see their worth"),
            };
            data.mods.Add(valueDisplay);
            return;
        }
        var extraInfo = new List<DescModification>()
        {
            new ("Looks like you are lowballing")
        };
        data.mods.Add(extraInfo);
        if (data.inventory.Settings.LowballMedUndercut == 100)
        {
            extraInfo.Add(new($"{McColorCodes.GRAY}You disabled lowballing suggestions"));
            return;
        }
        if (data.accountInfo.ExpiresAt < DateTime.UtcNow || data.accountInfo.Tier < AccountTier.PREMIUM)
        {
            if(string.IsNullOrEmpty(data.accountInfo.UserId))
            {
                NoPremium(extraInfo);
                return;
            }
            Console.WriteLine($"Lowballcheck for {data.accountInfo.UserId} no premium {data.accountInfo.Tier} expires at {data.accountInfo.ExpiresAt}");

            var owns = DiHandler.GetService<IUserApi>().UserUserIdOwnsUntilPostAsync(data.accountInfo.UserId.ToString(), new List<string>() { "premium" }).Result;
            if (owns.TryGetValue("premium", out var time) && time > DateTime.Now)
            {
                Console.WriteLine($"User {data.accountInfo.UserId} actually has premium until {time} recovered from refresh");
                data.accountInfo.Tier = AccountTier.PREMIUM;
                data.accountInfo.ExpiresAt = time;
            }
            else
            {
                NoPremium(extraInfo);

                return;
            }
        }
        extraInfo.Add(new($"{McColorCodes.GREEN}For lowballing these {receiveCount} items we"));
        if (data.inventory.Settings.DisableSuggestions)
            extraInfo.Add(new($"{McColorCodes.GRAY}Recommend: {McColorCodes.AQUA}{ModDescriptionService.FormatPriceShort(lowballPrice)}"));
        else
            extraInfo.Add(new(DescModification.ModType.SUGGEST, 0, $"----------------: " + ModDescriptionService.FormatPriceShort(lowballPrice).ToLower()));
        var breakdownHover = BuildLowballBreakdownHover(itemBreakdown, lowballPrice, worstCaseLowballPrice,
            aiReceiveSum, receiveCount, showWorstCase, nonExactPct > 0, showBreakdown);
        extraInfo.Add(new LoreBuilder()
            .AddText($"{McColorCodes.GRAY}SkyCofl recommended", breakdownHover, "/cofl set")
            .BuildLine());
        if (data.inventory.Settings.LowballMedUndercut == 0)
        {
            extraInfo.Add(new($"{McColorCodes.GRAY}Adjust median and lbin undercut"));
            extraInfo.Add(new($"{McColorCodes.GRAY}percentage with these settings:"));
            extraInfo.Add(new($"{McColorCodes.GRAY}/cofl set medUndercut 10"));
            extraInfo.Add(new($"{McColorCodes.GRAY}/cofl set lbinUndercut 10"));
        }
        else
        {
            var medSetting = data.inventory.Settings.LowballMedUndercut;
            var lbinSetting = data.inventory.Settings.LowballLbinUndercut;
            extraInfo.Add(new LoreBuilder()
                .AddText($"{McColorCodes.GRAY}Undercut: med {medSetting}% / lbin {lbinSetting}%",
                    $"{McColorCodes.GRAY}Median undercut: {medSetting}%\n"
                  + $"{McColorCodes.GRAY}LBIN undercut: {lbinSetting}%\n\n"
                  + $"{McColorCodes.GRAY}Adjust with:\n"
                  + $"{McColorCodes.GRAY}/cofl set medUndercut <value>\n"
                  + $"{McColorCodes.GRAY}/cofl set lbinUndercut <value>", "/cofl set").BuildLine());
        }

        extraInfo.Add(new LoreBuilder()
            .AddText($"{McColorCodes.GRAY}Lowball detail settings",
                $"{McColorCodes.GRAY}Non-exact penalty: {McColorCodes.WHITE}{nonExactPct}%\n"
              + $"{McColorCodes.GRAY}  /cofl set lorelbNonExactPct <0-10>\n\n"
              + $"{McColorCodes.GRAY}Worst-case extra %: {McColorCodes.WHITE}{worstCasePct}%\n"
              + $"{McColorCodes.GRAY}  /cofl set lorelbWorstCasePct <0-15>\n\n"
              + $"{McColorCodes.GRAY}Hide per-item breakdown:\n"
                            + $"{McColorCodes.GRAY}  /cofl set lorelbHideBreakdown true\n\n"
              + $"{McColorCodes.GRAY}Hide worst-case total:\n"
              + $"{McColorCodes.GRAY}  /cofl set lorelbHideWorstCase true",
                "/cofl set")
            .BuildLine());

        static void NoPremium(List<DescModification> extraInfo)
        {
            var lines = new string[]
                            {
                "With premium we will suggest",
                "a lowball price automatically",
                "looks like you don't currently",
                "have SkyCofl premium :("
                            };
            foreach (var line in lines)
                extraInfo.Add(new LoreBuilder()
                    .AddText($"{McColorCodes.GRAY}{line}",
                        "Supporting us by buying premium\n"
                      + "helps us pay for upkeep and servers\n"
                      + "and gives you extra features\n"
                      + $"{McColorCodes.YELLOW}Click to see options\n"
                      + $"{McColorCodes.GRAY}/cofl set loreDisableIn Trade"
                      + $"{McColorCodes.GRAY}To disable this display", "/cofl buy").BuildLine());
        }
    }

    private static string BuildLowballBreakdownHover(
        List<LowballItemBreakdown> itemBreakdown,
        long lowballPrice,
        long worstCaseLowballPrice,
        long aiReceiveSum,
        long receiveCount,
        bool showWorstCase,
        bool nonExactPenaltyEnabled,
        bool showBreakdown)
    {
        var hover = new StringBuilder();
        hover.AppendLine($"{McColorCodes.GRAY}Calculation: price * (100 - undercut)%");
        hover.AppendLine($"{McColorCodes.GRAY}Items valued: {receiveCount}");
        hover.AppendLine($"{McColorCodes.GRAY}Recommended: {McColorCodes.WHITE}{ModDescriptionService.FormatPriceShort(lowballPrice)}");
        if (showWorstCase && worstCaseLowballPrice < lowballPrice)
            hover.AppendLine($"{McColorCodes.GRAY}Worst-case:  {McColorCodes.YELLOW}{ModDescriptionService.FormatPriceShort(worstCaseLowballPrice)}");
        if (aiReceiveSum > 0)
        {
            hover.AppendLine($"{McColorCodes.GRAY}AI estimate: {McColorCodes.AQUA}{ModDescriptionService.FormatPriceShort(aiReceiveSum)}");
            var nonExactCount = itemBreakdown.Count(b => !(b.SelectedSource == "lbin" ? b.Lbin : b.Median).ExactMatch);
            if (nonExactCount > 0)
                hover.AppendLine($"{McColorCodes.DARK_GRAY}{nonExactCount} item(s) used AI due to no exact price match");
        }
        else if (itemBreakdown.Any(b => !(b.SelectedSource == "lbin" ? b.Lbin : b.Median).ExactMatch))
        {
            hover.AppendLine($"{McColorCodes.DARK_GRAY}Some items have no exact price match");
            hover.AppendLine($"{McColorCodes.DARK_GRAY}Enable AI estimate: /cofl lore add AiEstimate");
        }
        hover.AppendLine();

        if (!showBreakdown)
        {
            hover.Append($"{McColorCodes.GRAY}Per-item breakdown hidden");
            return hover.ToString().TrimEnd();
        }

        if (itemBreakdown.Count == 0)
        {
            hover.Append($"{McColorCodes.GRAY}No priced receive-items found.");
            return hover.ToString().TrimEnd();
        }

        foreach (var bd in itemBreakdown.Take(6))
        {
            var chosen = bd.SelectedSource == "lbin" ? bd.Lbin : bd.Median;
            var exactMark = chosen.ExactMatch ? string.Empty : $" {McColorCodes.RED}~";
            hover.AppendLine($"{McColorCodes.WHITE}{bd.ItemName}{McColorCodes.GRAY} ({bd.SelectedSource}){exactMark}");
            hover.AppendLine($"{McColorCodes.GRAY}  vol {bd.Volume:0.##} | med {ModDescriptionService.FormatPriceShort(bd.Median.InputValue)}->{ModDescriptionService.FormatPriceShort(bd.Median.Recommended)} ({bd.Median.AppliedUndercut}%)");
            hover.AppendLine($"{McColorCodes.GRAY}  lbin {ModDescriptionService.FormatPriceShort(bd.Lbin.InputValue)}->{ModDescriptionService.FormatPriceShort(bd.Lbin.Recommended)} ({bd.Lbin.AppliedUndercut}%)");
            if (showWorstCase && chosen.WorstCase < chosen.Recommended)
                hover.AppendLine($"{McColorCodes.GRAY}  worst {McColorCodes.YELLOW}{ModDescriptionService.FormatPriceShort(chosen.WorstCase)}");
            if (bd.AiEstimate > 0)
                hover.AppendLine($"{McColorCodes.GRAY}  AI    {McColorCodes.AQUA}{ModDescriptionService.FormatPriceShort(bd.AiEstimate)}");
            hover.AppendLine();
        }

        if (itemBreakdown.Count > 6)
            hover.AppendLine($"{McColorCodes.GRAY}...and {itemBreakdown.Count - 6} more items");

        hover.AppendLine($"{McColorCodes.DARK_GRAY}Low vol (+3/+2%), price band (+/-2/3%), non-exact (+{(nonExactPenaltyEnabled ? "custom" : "0")}%)");
        return hover.ToString().TrimEnd();
    }

    private static AdjustedValueEstimate GetAdjustedValueBreakdown(
        short underCutPercentage, long medLowballValue, float volume,
        bool exactMatch, int nonExactPct, int worstCaseExtraPct)
    {
        var appliedUndercut = (int)underCutPercentage;
        var valueBandAdjustment = 0;
        var volumeAdjustment = 0;
        if (medLowballValue < 10_000_000)
        {
            appliedUndercut += 2;
            valueBandAdjustment += 2;
        }
        else if (medLowballValue > 100_000_000)
        {
            appliedUndercut -= 2;
            valueBandAdjustment -= 2;
        }
        if (medLowballValue > 1_000_000_000)
        {
            appliedUndercut -= 3;
            valueBandAdjustment -= 3;
        }
        if (volume <= 1)
        {
            appliedUndercut += 3;
            volumeAdjustment += 3;
        }
        if (volume <= 0.4f)
        {
            appliedUndercut += 2;
            volumeAdjustment += 2;
        }
        var nonExactAdjustment = (!exactMatch && nonExactPct > 0) ? nonExactPct : 0;
        appliedUndercut += nonExactAdjustment;

        var recommended = medLowballValue <= 0 ? 0L : (long)(medLowballValue * (100 - appliedUndercut) / 100.0);
        // worst-case: simulate volume dropping below 0.4 threshold when currently above it
        var worstCaseUndercut = appliedUndercut + (volume > 0.4f && worstCaseExtraPct > 0 ? worstCaseExtraPct : 0);
        var worstCase = medLowballValue <= 0 ? 0L : (long)(medLowballValue * (100 - worstCaseUndercut) / 100.0);

        return new AdjustedValueEstimate(medLowballValue, recommended, worstCase, exactMatch, appliedUndercut,
            valueBandAdjustment, volumeAdjustment, nonExactAdjustment);
    }

    private static bool IsExactMatch(string sourceKey, string itemKey)
    {
        return !string.IsNullOrEmpty(sourceKey)
            && !string.IsNullOrEmpty(itemKey)
            && sourceKey.Replace("&comb", string.Empty) == itemKey;
    }

    /// <inheritdoc />
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        return;
    }

    private static long ParseCoinAmount(string stringAmount)
    {
        stringAmount = stringAmount?.Trim().Replace(",", string.Empty);
        if (string.IsNullOrEmpty(stringAmount))
            return 0;

        var multiplier = 1d;
        if (stringAmount.EndsWith("B"))
        {
            multiplier = 1_000_000_000;
            stringAmount = stringAmount.TrimEnd('B');
        }
        else if (stringAmount.EndsWith("M"))
        {
            multiplier = 1_000_000;
            stringAmount = stringAmount.TrimEnd('M');
        }
        else if (stringAmount.EndsWith("k"))
        {
            multiplier = 1_000;
            stringAmount = stringAmount.TrimEnd('k');
        }

        if (!double.TryParse(stringAmount, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return 0;

        return (long)(parsed * multiplier);
    }

    private sealed record AdjustedValueEstimate(
        long InputValue,
        long Recommended,
        long WorstCase,
        bool ExactMatch,
        int AppliedUndercut,
        int ValueBandAdjustment,
        int VolumeAdjustment,
        int NonExactAdjustment);

    private sealed record LowballItemBreakdown(
        string ItemName,
        float Volume,
        AdjustedValueEstimate Median,
        AdjustedValueEstimate Lbin,
        string SelectedSource,
        long AiEstimate);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Guides the player through the next actionable step of their focused goal: outlines the slot to
/// click on the current screen, adds a "Step:" line to the side panel and offers the amount on sign
/// screens. Everything is read from the goal cache, <see cref="Apply"/> never touches the network.
/// </summary>
public class GoalGuideModifier : ICustomModifier
{
    private const string OutlineColor = "00FF00";
    private readonly Func<IGoalGuideSource> sourceFactory;

    /// <summary>Creates a modifier resolving the goal service lazily from DI.</summary>
    public GoalGuideModifier() : this(() => DiHandler.GetService<GoalService>())
    {
    }

    /// <summary>Creates a modifier with an explicit goal source (tests).</summary>
    public GoalGuideModifier(Func<IGoalGuideSource> sourceFactory)
    {
        this.sourceFactory = sourceFactory;
    }

    /// <inheritdoc/>
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        if (string.IsNullOrWhiteSpace(preRequest.mcName) || preRequest.userId == null)
            return;
        preRequest.ToLoad["goalGuide"] = EnsureLoaded(preRequest);
    }

    private async Task<string> EnsureLoaded(ModDescriptionService.PreRequestContainer preRequest)
    {
        try
        {
            var idTask = preRequest.userId;
            if (await Task.WhenAny(idTask, Task.Delay(TimeSpan.FromSeconds(3))) != idTask)
                return "";
            var userId = await idTask;
            if (!string.IsNullOrEmpty(userId))
                await sourceFactory().EnsureLoaded(userId, preRequest.mcName);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error loading goal guide: " + ex.Message);
        }
        return "";
    }

    /// <inheritdoc/>
    public void Apply(DataContainer data)
    {
        try
        {
            if (string.IsNullOrEmpty(data?.mcName) || data.mods == null)
                return;
            var ctx = sourceFactory()?.GetGuideContext(data.mcName);
            if (ctx == null)
                return;
            var screen = new Screen(data);
            var guide = ctx.Kind switch
            {
                StepKind.BuyOrder => BuyOrder(screen, ctx, data),
                StepKind.InstaBuy => InstaBuy(screen, ctx),
                StepKind.Auction => Auction(screen, ctx),
                StepKind.Npc => new Guide($"Buy {ctx.Quantity}x {ctx.ItemName} from the NPC" + (string.IsNullOrEmpty(ctx.Step.Note) ? "" : $" ({ctx.Step.Note})")),
                StepKind.Craft => Craft(screen, ctx),
                StepKind.Forge => Forge(screen, ctx),
                StepKind.Sell => Sell(screen, ctx),
                _ => null
            };
            if (guide == null)
                return;
            if (guide.Slot is int slot && slot >= 0 && slot < data.mods.Count)
                data.mods[slot].Add(new DescModification(DescModification.ModType.HIGHLIGHT, -1, OutlineColor));
            var lines = new List<DescModification>
            {
                new LoreBuilder().AddText($"{McColorCodes.GOLD}Step: {McColorCodes.WHITE}{guide.Hint}", guide.Hover, guide.OnClick).BuildLine()
            };
            if (guide.Fill != null)
                lines.Add(guide.Fill);
            data.mods.Add(lines);
            if (guide.MarkOrdered)
                _ = MarkOrdered(data.mcName, ctx.Step.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error applying goal guide: " + ex);
        }
    }

    private async Task MarkOrdered(string mcName, string stepId)
    {
        try
        {
            await sourceFactory().MarkOrdered(mcName, stepId);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error marking goal step ordered: " + ex.Message);
        }
    }

    private sealed record Guide(string Hint, int? Slot = null, string OnClick = null, string Hover = null, DescModification Fill = null, bool MarkOrdered = false);

    private static Guide BuyOrder(Screen s, GoalGuideContext ctx, DataContainer data)
    {
        var qty = ctx.Quantity;
        if (s.IsProductPage && s.IsProductOf(ctx))
            return new Guide($"Click Create Buy Order ({qty}x {ctx.ItemName})", s.Find("Create Buy Order", 15));
        if (s.Is("How many do you want?") && s.MatchesItem(ctx))
        {
            var slot = s.Find("Custom Amount", 16);
            return new Guide($"Custom Amount -> enter {qty}", slot, Fill: FillSign(data, s, qty, slot, $"{McColorCodes.GRAY}[set {McColorCodes.YELLOW}{qty:N0}{McColorCodes.GRAY}]"));
        }
        if (s.Is("How much do you want to pay?") && s.MatchesItem(ctx))
            return new Guide("Pick Top Order +0.1 to be first in line", s.Find("Top Order +0.1", 12));
        if (s.Is("Confirm Buy Order") && s.MatchesItem(ctx))
            return new Guide("Confirm the order", s.FindConfirm(), MarkOrdered: true);
        return ctx.HasCookie
            ? new Guide($"Open the bazaar page: /cofl bzorder {qty} {ctx.SearchName}", OnClick: $"/cofl bzorder {qty} {ctx.SearchName}", Hover: "Click to open the bazaar page")
            : new Guide("Open the bazaar and use the search", OnClick: $"/cofl bazaarsearch {ctx.SearchName}", Hover: $"Click to search {ctx.ItemName}");
    }

    private static Guide InstaBuy(Screen s, GoalGuideContext ctx)
    {
        var qty = ctx.Quantity;
        if (s.IsProductPage && s.IsProductOf(ctx))
            return new Guide($"Click Buy Instantly ({qty}x {ctx.ItemName})", s.Find("Buy Instantly", 10));
        if (s.IsInstantBuyAmount && s.IsProductOf(ctx))
        {
            var slot = s.Find("Custom Amount", 16);
            var cap = InstantBuyMaxAmount.ParseMaxAmountFromText(s.TextAt(slot));
            var amount = InstantBuyMaxAmount.CapAmount(qty, cap);
            return new Guide($"Custom Amount -> enter {amount}", slot,
                Fill: FillSign(s.Data, s, amount, slot, $"{McColorCodes.GRAY}[buy {McColorCodes.YELLOW}{amount:N0}{McColorCodes.GRAY}]"));
        }
        if (s.Is("Confirm Instant Buy"))
            return new Guide("Confirm the purchase", s.FindConfirm());
        var cmd = ctx.HasCookie ? $"/bz {ctx.SearchName}" : $"/cofl bazaarsearch {ctx.SearchName}";
        return new Guide($"Open the bazaar page of {ctx.ItemName} and buy {qty}x instantly", OnClick: cmd, Hover: "Click to open the bazaar page");
    }

    private static Guide Auction(Screen s, GoalGuideContext ctx)
    {
        if (s.Is("Confirm Purchase"))
            return new Guide("Confirm the purchase", s.Find("Confirm Purchase", 11));
        if (s.Is("BIN Auction View") && s.TagAt(13) == ctx.Step.ItemTag)
            return new Guide($"Buy {ctx.ItemName} now", s.Find("Buy Item Right Now", 31));
        return string.IsNullOrEmpty(ctx.LbinUuid)
            ? new Guide($"Buy {ctx.ItemName} on the auction house", OnClick: "/ah", Hover: "Click to open the auction house")
            : new Guide($"Buy {ctx.ItemName} on the auction house", OnClick: $"/viewauction {ctx.LbinUuid}", Hover: "Click to view the lowest bin");
    }

    private static Guide Craft(Screen s, GoalGuideContext ctx)
    {
        if (s.Is("Craft Item"))
        {
            var output = s.FindByTag(ctx.Step.ItemTag);
            if (output >= 0)
                return new Guide($"Take {ctx.ItemName} from the output", output);
            return new Guide("Place the ingredients in a crafting table (/craft)");
        }
        if (s.Contains("Recipe"))
            return new Guide("Place the ingredients in a crafting table (/craft)", OnClick: "/craft", Hover: "Click to open the crafting table");
        return new Guide($"Craft {ctx.ItemName}", OnClick: $"/recipe {ctx.ItemName}", Hover: "Click to view the recipe");
    }

    private static Guide Forge(Screen s, GoalGuideContext ctx)
    {
        if (s.Contains("The Forge") || s.Contains("Select Process"))
            return new Guide($"Select {ctx.ItemName}", s.FindOrNone(ctx.ItemName));
        return new Guide("Go to the Forge", OnClick: "/warp forge", Hover: "Click to warp to the forge");
    }

    private static Guide Sell(Screen s, GoalGuideContext ctx)
    {
        if (s.Is("Create BIN Auction"))
            return new Guide($"Set the price of {ctx.ItemName} (see the price suggestion)");
        var bazaar = s.Data.bazaarPrices?.ContainsKey(ctx.Step.ItemTag ?? "") ?? false;
        return bazaar && ctx.HasCookie
            ? new Guide($"List {ctx.ItemName}", OnClick: $"/bz {ctx.SearchName}", Hover: "Click to open the bazaar page")
            : new Guide($"List {ctx.ItemName}", OnClick: "/ah", Hover: "Click to open the auction house");
    }

    private static DescModification FillSign(DataContainer data, Screen s, long amount, int slot, string text)
    {
        var hover = $"{McColorCodes.GRAY}Fills the sign with {McColorCodes.YELLOW}{amount:N0}";
        var builder = new LoreBuilder();
        if (data.inventory != null && data.inventory.Version >= 4)
            builder.AddFillSign(text, "to order", amount.ToString(CultureInfo.InvariantCulture), s.NameAt(slot) ?? "Custom Amount", slot, hover);
        else
            builder.AddText(text, hover + $"\n{McColorCodes.AQUA}Click to copy, then paste it into Custom Amount", $"copy:{amount}");
        return builder.BuildLine();
    }

    /// <summary>Read access to the slots of the open screen, from the uploaded items.</summary>
    private sealed class Screen
    {
        public DataContainer Data { get; }
        private readonly string title;

        public Screen(DataContainer data)
        {
            Data = data;
            title = Strip(data.inventory?.ChestName ?? "");
        }

        private int Count => Math.Max(Data.Items?.Count ?? 0, Data.auctionRepresent?.Count ?? 0);

        public bool Is(string name) => title.Equals(name, StringComparison.OrdinalIgnoreCase);
        public bool Contains(string part) => title.Contains(part, StringComparison.OrdinalIgnoreCase);
        public bool IsInstantBuyAmount => title.Contains('➜') && title.TrimEnd().EndsWith("Instant Buy", StringComparison.OrdinalIgnoreCase);

        public bool IsProductPage => title.Contains('➜') && !IsInstantBuyAmount
            && (IndexOf("Buy Instantly") >= 0 || IndexOf("Create Buy Order") >= 0 || IndexOf("Sell Instantly") >= 0);

        public string NameAt(int i)
        {
            if (i < 0)
                return null;
            var name = i < (Data.Items?.Count ?? 0) ? Data.Items[i]?.ItemName : null;
            if (string.IsNullOrEmpty(name) && i < (Data.auctionRepresent?.Count ?? 0))
                name = Data.auctionRepresent[i].auction?.ItemName;
            return string.IsNullOrEmpty(name) ? null : name;
        }

        public string TagAt(int i)
        {
            if (i < 0)
                return null;
            var tag = i < (Data.Items?.Count ?? 0) ? Data.Items[i]?.Tag : null;
            if (string.IsNullOrEmpty(tag) && i < (Data.auctionRepresent?.Count ?? 0))
                tag = Data.auctionRepresent[i].auction?.Tag;
            return string.IsNullOrEmpty(tag) ? null : tag;
        }

        public string TextAt(int i)
        {
            if (i < 0)
                return "";
            var text = i < (Data.Items?.Count ?? 0) ? Data.Items[i]?.Description : null;
            if (string.IsNullOrEmpty(text) && i < (Data.auctionRepresent?.Count ?? 0))
                text = string.Join('\n', Data.auctionRepresent[i].desc ?? Array.Empty<string>());
            return (text ?? "") + "\n" + (NameAt(i) ?? "");
        }

        public int IndexOf(string name)
        {
            for (int i = 0; i < Count; i++)
                if (NameAt(i) is { } n && Strip(n).Contains(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>Slot by item name, else the known index (when it exists on this screen).</summary>
        public int Find(string name, int fallback)
        {
            var index = IndexOf(name);
            return index >= 0 ? index : fallback < Count ? fallback : -1;
        }

        public int FindOrNone(string name) => IndexOf(name);

        public int FindConfirm()
        {
            for (int i = 0; i < Count; i++)
                if (NameAt(i) is { } n && Strip(n) is var s && (s.Contains("Confirm", StringComparison.OrdinalIgnoreCase) || s.Contains("Buy Order", StringComparison.OrdinalIgnoreCase)))
                    return i;
            return 13 < Count ? 13 : -1;
        }

        public int FindByTag(string tag)
        {
            if (tag == null)
                return -1;
            for (int i = 0; i < Count; i++)
                if (TagAt(i) == tag)
                    return i;
            return -1;
        }

        /// <summary>Product page / instant buy screen of the item the step is about.</summary>
        public bool IsProductOf(GoalGuideContext ctx)
            => TagAt(13) == ctx.Step.ItemTag || title.Contains(Strip(ctx.ItemName ?? "~"), StringComparison.OrdinalIgnoreCase)
            || (Data.auctionRepresent?.Select(a => a.auction?.Tag).Where(t => !string.IsNullOrEmpty(t))
                .GroupBy(t => t).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key == ctx.Step.ItemTag && ctx.Step.ItemTag != null);

        /// <summary>
        /// Screens without the item in the title: true when the item is shown, or when the screen shows
        /// no other bazaar product (so it can't be a different order).
        /// </summary>
        public bool MatchesItem(GoalGuideContext ctx)
        {
            var otherProduct = false;
            for (int i = 0; i < Count; i++)
            {
                var tag = TagAt(i);
                if (tag != null && tag == ctx.Step.ItemTag)
                    return true;
                if (NameAt(i) is { } n && ctx.ItemName != null && Strip(n).Contains(Strip(ctx.ItemName), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (tag != null && (Data.bazaarPrices?.ContainsKey(tag) ?? false))
                    otherProduct = true;
            }
            return !otherProduct;
        }
    }

    private static string Strip(string input) => Regex.Replace(input ?? "", "§.", string.Empty);
}

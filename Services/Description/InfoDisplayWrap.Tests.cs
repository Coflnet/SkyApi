using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.MC;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests;

/// <summary>Tests for <see cref="InfoDisplayWrap"/>.</summary>
[TestFixture]
public class InfoDisplayWrapTests
{
    private const string Goal = "Step: Open the bazaar page of Enchanted Sea Lumies and buy 128x instantly";

    private static string Strip(string s) => Regex.Replace(s ?? "", "§.", "");
    private static List<LoreComponent> Parse(string v) => JsonSerializer.Deserialize<List<LoreComponent>>(v);
    private static int Visible(string value)
        => value.StartsWith('[') ? Parse(value).Sum(c => Strip(c.Text).Length) : Strip(value).Length;
    private static string VisibleText(string value)
        => value.StartsWith('[') ? string.Concat(Parse(value).Select(c => Strip(c.Text))) : Strip(value);

    /// <summary>The goal guidance line that ran off the screen is wrapped and stays clickable.</summary>
    [Test]
    public void GoalLine_IsWrappedKeepingHoverAndClick()
    {
        var line = new LoreBuilder()
            .AddText($"{McColorCodes.GOLD}Step: {McColorCodes.WHITE}Open the bazaar page of Enchanted Sea Lumies and buy 128x instantly", "hover", "/bz Sea Lumies")
            .BuildLine();
        var mods = new List<List<DescModification>> { new() { line } };

        InfoDisplayWrap.Apply(mods, 0);

        var lines = mods[0];
        lines.Count.Should().BeGreaterThan(1);
        lines.All(l => Visible(l.Value) <= InfoDisplayWrap.MaxVisibleLength).Should().BeTrue();
        string.Join(" ", lines.Select(l => VisibleText(l.Value))).Should().Be(Goal);
        foreach (var l in lines)
        {
            var parts = Parse(l.Value);
            parts.All(p => p.Hover == "hover" && p.OnClick == "/bz Sea Lumies").Should().BeTrue();
        }
        foreach (var l in lines.Skip(1))
            Parse(l.Value)[0].Text.Should().StartWith(McColorCodes.WHITE);
    }

    /// <summary>Plain text wraps and the active colour is repeated on the continuation.</summary>
    [Test]
    public void PlainText_WrapsAndCarriesColor()
    {
        var result = InfoDisplayWrap.WrapLine("§6§lone two three four five six seven eight nine ten");
        result.Count.Should().BeGreaterThan(1);
        result.All(l => Visible(l) <= InfoDisplayWrap.MaxVisibleLength).Should().BeTrue();
        result.Skip(1).All(l => l.StartsWith("§6§l")).Should().BeTrue();
        string.Join(" ", result.Select(Strip)).Should().Be("one two three four five six seven eight nine ten");
    }

    /// <summary>Short lines are returned as the very same string.</summary>
    [Test]
    public void ShortLine_IsUnchanged()
    {
        var text = "§7short line";
        var result = InfoDisplayWrap.WrapLine(text);
        result.Should().HaveCount(1);
        result[0].Should().BeSameAs(text);
    }

    /// <summary>A word longer than the limit is hard split.</summary>
    [Test]
    public void LongWord_IsHardSplit()
    {
        var max = InfoDisplayWrap.MaxVisibleLength;
        var word = new string('a', max * 2 + 10);
        var result = InfoDisplayWrap.WrapLine("x " + word);
        result.Should().Equal("x", new string('a', max), new string('a', max), new string('a', 10));
    }

    /// <summary>The break falls between two components.</summary>
    [Test]
    public void BreakBetweenComponents_KeepsOwners()
    {
        var value = new LoreBuilder()
            .AddText("aaaa bbbb cccc dddd eeee ffff gggg ", "h1", "c1")
            .AddText("hhhh iiii", "h2", "c2").Build();
        var result = InfoDisplayWrap.WrapLine(value);
        result.Should().HaveCount(2);
        var first = Parse(result[0]);
        first.Should().ContainSingle().Which.Hover.Should().Be("h1");
        first[0].Text.Should().Be("aaaa bbbb cccc dddd eeee ffff gggg");
        var second = Parse(result[1]);
        second.Should().ContainSingle().Which.Text.Should().Be("hhhh iiii");
        second[0].OnClick.Should().Be("c2");
    }

    /// <summary>The break falls inside a component, both halves stay clickable.</summary>
    [Test]
    public void BreakInsideComponent_SplitsIntoTwoClickableFragments()
    {
        var value = new LoreBuilder()
            .AddText("§aaaaa bbbb ", "h1", "c1")
            .AddText("cccc dddd eeee ffff gggg hhhh", "h2", "c2").Build();
        var result = InfoDisplayWrap.WrapLine(value);
        result.Should().HaveCount(2);
        var first = Parse(result[0]);
        first.Select(c => c.Hover).Should().Equal("h1", "h2");
        var second = Parse(result[1]);
        second.Should().ContainSingle();
        second[0].Hover.Should().Be("h2");
        second[0].OnClick.Should().Be("c2");
        second[0].Text.Should().NotStartWith("§a", "colour of the previous component does not carry over");
        result.All(l => Visible(l) <= InfoDisplayWrap.MaxVisibleLength).Should().BeTrue();
    }

    /// <summary>Only APPEND lines are touched.</summary>
    [Test]
    public void NonAppendEntries_AreUntouched()
    {
        var suggest = new DescModification(DescModification.ModType.SUGGEST, 0, "to order: 128 and a lot more text to be long");
        var highlight = new DescModification(DescModification.ModType.HIGHLIGHT, 3, "ff0000");
        var mods = new List<List<DescModification>>
        {
            new() { new("first"), suggest, new(new string('x', InfoDisplayWrap.MaxVisibleLength + 1) + " y"), highlight }
        };

        InfoDisplayWrap.Apply(mods, 0);

        var list = mods[0];
        list[0].Value.Should().Be("first");
        list[1].Should().BeSameAs(suggest);
        list[1].Value.Should().Be("to order: 128 and a lot more text to be long");
        list.Last().Should().BeSameAs(highlight);
        list.Count.Should().BeGreaterThan(4);
    }

    /// <summary>Invalid component JSON is left alone.</summary>
    [Test]
    public void InvalidJson_IsUntouched()
    {
        var text = "[not json " + new string('a', 60);
        InfoDisplayWrap.WrapLine(text).Should().Equal(text);
    }

    /// <summary>Lists below the start index and odd input are ignored without throwing.</summary>
    [Test]
    public void EarlierListsAndNullInput_AreSafe()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 20));
        var mods = new List<List<DescModification>> { new() { new(longText) }, null, new() { null, new(DescModification.ModType.APPEND, 0, null) } };
        InfoDisplayWrap.Apply(null, 0);
        InfoDisplayWrap.Apply(mods, 1);
        mods[0].Should().HaveCount(1);
    }

    /// <summary>The disable handle lands on the first wrapped line as the multi-line x.</summary>
    [Test]
    public void StampAfterWrap_PutsXOnFirstLine()
    {
        var data = new DataContainer { mods = new List<List<DescModification>>() };
        data.mods.Add(new() { new(Goal + " and a few more words") });
        InfoDisplayWrap.Apply(data.mods, 0);
        InfoDisplayDisable.StampDisableButton(data, 0, "goals");
        var first = Parse(data.mods[0][0].Value);
        first.Last().Text.Should().EndWith("x");
        data.mods[0].Count.Should().BeGreaterThan(1);
        data.mods[0][1].Value.Should().NotStartWith("[");
    }
}

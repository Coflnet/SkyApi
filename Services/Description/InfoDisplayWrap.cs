using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Word-wraps the lines of the inventory info display (the panel next to an open chest) so no line
/// runs off the screen. Applied once to everything a modifier added, so individual modifiers don't
/// have to care about line length.
/// </summary>
public static class InfoDisplayWrap
{
    /// <summary>Maximum number of visible characters (formatting codes excluded) per panel line.</summary>
    public const int MaxVisibleLength = 35;

    /// <summary>One visible character together with the component it came from and the codes preceding it.</summary>
    private readonly record struct Cell(int Component, string Codes, char Char, string StateBefore);

    /// <summary>
    /// Wraps every long APPEND line in the lists from <paramref name="fromIndex"/> on. Other
    /// modification types are left untouched and keep their position. Never throws.
    /// </summary>
    public static void Apply(List<List<DescModification>> mods, int fromIndex)
    {
        if (mods == null)
            return;
        for (int i = Math.Max(fromIndex, 0); i < mods.Count; i++)
        {
            var list = mods[i];
            if (list == null || list.Count == 0)
                continue;
            try
            {
                var result = new List<DescModification>(list.Count);
                var changed = false;
                foreach (var mod in list)
                {
                    if (mod == null || mod.Type != DescModification.ModType.APPEND || string.IsNullOrEmpty(mod.Value))
                    {
                        result.Add(mod);
                        continue;
                    }
                    var lines = WrapLine(mod.Value);
                    if (lines.Count == 1 && ReferenceEquals(lines[0], mod.Value))
                    {
                        result.Add(mod);
                        continue;
                    }
                    changed = true;
                    result.AddRange(lines.Select(l => new DescModification(mod.Type, mod.Line, l)));
                }
                if (changed)
                    mods[i] = result;
            }
            catch (Exception)
            {
                // wrapping is cosmetic, leave the list as it was
            }
        }
    }

    /// <summary>
    /// Wraps one line value (plain text or a component JSON array) at <see cref="MaxVisibleLength"/>
    /// visible characters. Returns the original string instance when nothing has to change.
    /// </summary>
    public static List<string> WrapLine(string value)
    {
        var unchanged = new List<string> { value };
        if (string.IsNullOrEmpty(value))
            return unchanged;
        try
        {
            List<LoreComponent> components;
            var isJson = value.StartsWith('[');
            if (isJson)
            {
                components = JsonSerializer.Deserialize<List<LoreComponent>>(value);
                if (components == null)
                    return unchanged;
            }
            else
            {
                components = new List<LoreComponent> { new LoreComponent { Text = value } };
            }
            var cells = ToCells(components);
            if (cells.Count <= MaxVisibleLength)
                return unchanged;

            var lines = Break(cells);
            return lines.Select(line => isJson
                ? JsonSerializer.Serialize(ToFragments(line, components))
                : BuildText(line)).ToList();
        }
        catch (Exception)
        {
            return unchanged;
        }
    }

    private static List<Cell> ToCells(List<LoreComponent> components)
    {
        var cells = new List<Cell>();
        for (int c = 0; c < components.Count; c++)
        {
            var text = components[c]?.Text ?? string.Empty;
            string color = "";
            var styles = "";
            var pending = new StringBuilder();
            var before = "";
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '§' && i + 1 < text.Length)
                {
                    var code = char.ToLowerInvariant(text[i + 1]);
                    if (pending.Length == 0)
                        before = color + styles;
                    pending.Append(text, i, 2);
                    if (code == 'r')
                    {
                        color = "";
                        styles = "";
                    }
                    else if ("0123456789abcdef".IndexOf(code) >= 0)
                    {
                        color = "§" + code;
                        styles = "";
                    }
                    else if ("lonmk".IndexOf(code) >= 0 && styles.IndexOf(code) < 0)
                    {
                        styles += "§" + code;
                    }
                    i++;
                    continue;
                }
                // without codes of its own the character is styled by whatever was active before it
                cells.Add(new Cell(c, pending.ToString(), text[i], pending.Length == 0 ? color + styles : before));
                pending.Clear();
            }
        }
        return cells;
    }

    private static List<List<Cell>> Break(List<Cell> cells)
    {
        var resolved = cells;
        var lines = new List<List<Cell>>();
        var current = new List<Cell>();
        var spaces = new List<Cell>();
        int pos = 0;
        while (pos < resolved.Count)
        {
            var isSpace = resolved[pos].Char == ' ';
            int end = pos;
            while (end < resolved.Count && (resolved[end].Char == ' ') == isSpace)
                end++;
            var token = resolved.GetRange(pos, end - pos);
            pos = end;
            if (isSpace)
            {
                // leading whitespace of the whole value is kept, at the start of a continuation it is dropped
                if (current.Count > 0 || lines.Count == 0)
                    spaces.AddRange(token);
                continue;
            }
            if (current.Count > 0 && current.Count + spaces.Count + token.Count > MaxVisibleLength)
            {
                lines.Add(current);
                current = new List<Cell>();
                spaces.Clear();
            }
            current.AddRange(spaces);
            spaces.Clear();
            foreach (var cell in token)
            {
                if (current.Count >= MaxVisibleLength)
                {
                    lines.Add(current);
                    current = new List<Cell>();
                }
                current.Add(cell);
            }
        }
        if (current.Count > 0)
            lines.Add(current);
        return lines;
    }

    private static string BuildText(List<Cell> line)
    {
        var sb = new StringBuilder();
        AppendFragment(sb, line, 0, line.Count);
        return sb.ToString();
    }

    private static List<LoreComponent> ToFragments(List<Cell> line, List<LoreComponent> source)
    {
        var fragments = new List<LoreComponent>();
        int start = 0;
        while (start < line.Count)
        {
            int end = start;
            while (end < line.Count && line[end].Component == line[start].Component)
                end++;
            var origin = source[line[start].Component];
            var sb = new StringBuilder();
            AppendFragment(sb, line, start, end);
            fragments.Add(new LoreComponent { Text = sb.ToString(), Hover = origin?.Hover, OnClick = origin?.OnClick });
            start = end;
        }
        return fragments;
    }

    /// <summary>Writes the cells, starting with the formatting active at the first one.</summary>
    private static void AppendFragment(StringBuilder sb, List<Cell> line, int start, int end)
    {
        // the state active before the first cell is restated so a continuation keeps its colour
        sb.Append(line[start].StateBefore);
        for (int i = start; i < end; i++)
            sb.Append(line[i].Codes).Append(line[i].Char);
    }
}

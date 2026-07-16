using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Description;
/// <summary>
/// Interface for custom description modifiers.
/// </summary>
public interface ICustomModifier
{
    /// <summary>
    /// Adds or modifies description lines in the response.
    /// </summary>
    /// <param name="data">Data container containing auction representations and price estimates.</param>
    void Apply(DataContainer data);
    /// <summary>
    /// Allows sheduling additional/changing request data before sniper prices are fetched.
    /// </summary>
    /// <param name="preRequest"></param>
    void Modify(ModDescriptionService.PreRequestContainer preRequest);
    /// <summary>
    /// Name this modifier is toggled by through <c>/cofl set loreDisableInfoIn &lt;name&gt;</c>. Defaults
    /// to the concrete type name so every modifier is disableable without extra code; override only to
    /// keep a previously documented key (e.g. <see cref="InventoryInfo"/> stays <c>"Crafting"</c>) or to
    /// opt out entirely by returning <c>null</c>. <see cref="ModDescriptionService"/> checks it in one
    /// place: it skips <see cref="Apply"/> (leaving only an invisible re-enable handle) when the player
    /// disabled it, and otherwise stamps the standardized disable button onto the display's first line.
    /// See <see cref="InfoDisplayDisable"/>.
    /// </summary>
    string DisableInfoName => GetType().Name;
}

/// <summary>
/// Shared helpers so every <see cref="ICustomModifier"/> checks its disabled state the same way
/// instead of repeating the <c>DisableInfoIn</c> lookup (which <see cref="BazaarInfo"/> and
/// <see cref="InventoryInfo"/> originally spelled out by hand).
/// </summary>
public static class CustomModifierExtensions
{
    /// <summary>
    /// Whether the player disabled this modifier's info display via
    /// <c>/cofl set loreDisableInfoIn &lt;name&gt;</c> (matched case-insensitively against
    /// <see cref="ICustomModifier.DisableInfoName"/>). Always false for modifiers that opt out by
    /// leaving <see cref="ICustomModifier.DisableInfoName"/> null.
    /// </summary>
    public static bool IsInfoDisabled(this ICustomModifier modifier, DataContainer data)
        => modifier.DisableInfoName != null
        && (data?.inventory?.Settings?.DisableInfoIn?.Contains(modifier.DisableInfoName, StringComparer.OrdinalIgnoreCase) ?? false);
}

/// <summary>
/// Builds the standardized "disable this info display" handle so every extra-lore info panel behaves
/// the same, driven centrally from <see cref="ModDescriptionService"/> rather than each modifier
/// wiring up its own <c>/cofl confirm</c> command (as <see cref="BazaarInfo"/> used to). The disable
/// handle sits at the end of the display's first line - an unobtrusive empty space for single-line
/// displays, or a visible "x" once the display spans multiple lines. When the player disabled a
/// display the modifier is skipped entirely and only an invisible re-enable handle is left behind.
/// </summary>
public static class InfoDisplayDisable
{
    /// <summary>Command (guarded by <c>/cofl confirm</c>) that turns a display off.</summary>
    public static string DisableCommand(string name) => $"/cofl confirm /cofl set loreDisableInfoIn {name}";
    /// <summary>Command (guarded by <c>/cofl confirm</c>) that turns a previously disabled display back on.</summary>
    public static string ReenableCommand(string name) => $"/cofl confirm /cofl set loreDisableInfoIn rm {name}";

    /// <summary>
    /// Appends the disable handle to the first line of whatever <paramref name="modifier"/>-owned info
    /// display was added to <paramref name="data"/> since <paramref name="addedFromIndex"/>. Renders as
    /// an empty space for a single-line display and as an "x" once the added display spans multiple
    /// lines. No-op when the modifier added nothing.
    /// </summary>
    public static void StampDisableButton(DataContainer data, int addedFromIndex, string disableInfoName)
    {
        if (data?.mods == null || disableInfoName == null)
            return;
        var added = new List<List<DescModification>>();
        for (int i = addedFromIndex; i < data.mods.Count; i++)
            if (data.mods[i] != null && data.mods[i].Count > 0)
                added.Add(data.mods[i]);
        if (added.Count == 0)
            return; // modifier produced no info display this run
        var firstDisplay = added[0];
        var multiLine = added.Count > 1 || firstDisplay.Count > 1;
        var firstLine = firstDisplay[0];
        firstLine.Value = AppendComponent(
            firstLine.Value,
            text: multiLine ? $" {McColorCodes.GRAY}x" : "  ",
            hover: "Disable this info display",
            onClick: DisableCommand(disableInfoName));
    }

    /// <summary>
    /// Adds the invisible (empty space) hover-to-reenable handle shown in place of a display the player
    /// turned off, so it stays discoverable without rendering any visible lore.
    /// </summary>
    public static void AddReenablePlaceholder(DataContainer data, string disableInfoName)
    {
        if (data?.mods == null || disableInfoName == null)
            return;
        var value = JsonSerializer.Serialize(new List<LoreComponent>
        {
            new LoreComponent
            {
                Text = "  ",
                Hover = $"{McColorCodes.GRAY}Click to re-enable this SkyCofl info display",
                OnClick = ReenableCommand(disableInfoName)
            }
        });
        data.mods.Add(new List<DescModification> { new DescModification(value) });
    }

    // Appends one interactive component to an existing info-display line. Lines built through
    // LoreBuilder are already a component-JSON array (append into it); plain-text lines are wrapped
    // into a text component first. The mod parses either form per line (see BazaarInfo, which mixes
    // both in one display), so promoting a plain line to component JSON here is safe.
    private static string AppendComponent(string existingValue, string text, string hover, string onClick)
    {
        var components = ToComponents(existingValue);
        components.Add(new LoreComponent { Text = text, Hover = hover, OnClick = StripFormatting(onClick) });
        return JsonSerializer.Serialize(components);
    }

    private static List<LoreComponent> ToComponents(string value)
    {
        if (!string.IsNullOrEmpty(value) && value.TrimStart().StartsWith('['))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<LoreComponent>>(value);
                if (parsed != null)
                    return parsed;
            }
            catch (JsonException)
            {
                // not component json after all - fall through and treat it as plain text
            }
        }
        return new List<LoreComponent> { new LoreComponent { Text = value ?? string.Empty } };
    }

    private static string StripFormatting(string input)
        => string.IsNullOrEmpty(input) ? input : Regex.Replace(input, "§.", string.Empty);
}

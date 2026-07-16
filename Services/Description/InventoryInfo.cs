using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Coflnet.Sky.Commands.MC;

namespace Coflnet.Sky.Api.Services.Description;

public class InventoryInfo : ICustomModifier
{
    string[] Texts = new[]{
        "You can toggle the flipper chat showing by running `/fc`",
        "`/cofl flip never` will prevent flips from being sent to you. Running `/cofl flip always` will make flips always show.",
        "`/cofl bazaar` shows you which flips are the best to buy from bazaar on average.",
        "`/cofl fusionflip` shows you the best flips utilizing the fusion machine on galatea",
        "`/cofl blocked` will tell you why you don't see auction house flips",
        "Ever took a look at where you make the most money with `/cofl task` before?",
        "This info display in your inventory can be disabled with `/cofl set loreDisableInfoIn Crafting`",
        "You can enable price paid on all non stackable items with `/cofl lore`",
        "Searching for a lowballer? \n`/cofl lowball` will help you find one",
        "Have a useful idea? Post a suggestion on our discord!",
        "Change your minimum profit for flips with `/cofl set minProfit 2m`",
        "You can search all your storage (with double chests) with `/cofl search <search term>`",
        "Found any weird/wrong thing? Post a bug-report on our discord! Also run `/cofl report <description` to help us fix it",
    };
    public void Apply(DataContainer data)
    {
        if (data.inventory.Settings.DisableInfoIn?.Contains("Crafting", StringComparer.OrdinalIgnoreCase) ?? false)
            return;
        if (Random.Shared.NextDouble() < 0.9 && data.accountInfo?.UserId != "7")
            return;

        var text = Texts[Random.Shared.Next(Texts.Length)];
        if (data.inventory.Version >= 3 && Random.Shared.NextDouble() < 0.1)
        {
            text = "You can drag the SkyCofl info display (this text) to somewhere else by holding `right-click` and moving the mouse";
        }
        var coloredText = Regex.Replace(text, @"`(/.*?)`", m => $"§b{m.Groups[1]}§r" + McColorCodes.GRAY);

        if (coloredText.Contains('\n') || coloredText.StartsWith('['))
        {
            data.mods.Add(coloredText.Split('\n').Select(line => new Models.Mod.DescModification(line)).ToList());
            return;
        }

        var words = coloredText.Split(' ');
        var result = new StringBuilder();
        var currentLine = new StringBuilder();
        var display = new List<Models.Mod.DescModification>();
        if (data.inventory.ChestName == null && data.inventory.Version <= 2) // 1.8.9 inventory
        {
            display.Add(new(McColorCodes.BLACK + "_______________________________§7___"));
        }
        currentLine.Insert(0, McColorCodes.GRAY);
        foreach (var word in words)
        {
            // Calculate visible length by removing color codes
            var visibleLength = Regex.Replace(currentLine.ToString(), "§.", "").Length;

            if (currentLine.Length > 0 && visibleLength + 1 + Regex.Replace(word, "§.", "").Length > 35 || IsStartOfcommand(word))
            {
                result.AppendLine(currentLine.ToString());
                display.Add(new(currentLine.ToString()));
                currentLine.Clear();
                currentLine.Insert(0, McColorCodes.GRAY);
            }

            if (currentLine.Length > 0)
            {
                currentLine.Append(' ');
            }
            currentLine.Append(word);
        }

        display.Add(new(new LoreBuilder()
                .AddText(currentLine.ToString(), "Drag by holding right click").Build()));

        data.mods.Add(display);

        static bool IsStartOfcommand(string word)
        { // the start of a command should be put at the start of the line as its bad to break a command up
            return word.StartsWith("§b");
        }
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // none
    }
}

public class LoreBuilder
{
    private List<LoreComponent> components = new List<LoreComponent>();

    public LoreBuilder AddText(string text, string hover = null, string onClick = null)
    {
        components.Add(new LoreComponent
        {
            Text = text,
            Hover = hover,
            OnClick = StripFormatting(onClick)
        });

        return this;
    }

    /// <summary>
    /// Adds a line that, when clicked on a mod reporting description version 4+, opens the matching
    /// sign, types <paramref name="value"/> into its first line, and submits it. The click carries a
    /// structured <c>fillsign:{json}</c> payload the mod parses (see the mod's <c>armSignFillAndOpen</c>
    /// / <c>handleSignFill</c>): <paramref name="signLine"/> is the sign's 4th line the mod matches on
    /// as a guard, and <paramref name="buttonName"/>/<paramref name="buttonSlot"/> let it pick the
    /// right button to open when several exist. Keeping the payload shape in one place here means the
    /// API and mod can't drift on the JSON keys.
    /// </summary>
    public LoreBuilder AddFillSign(string text, string signLine, string value, string buttonName, int buttonSlot, string hover = null)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new FillSignPayload
        {
            Line = signLine,
            Value = value,
            Name = StripFormatting(buttonName),
            Slot = buttonSlot
        });
        return AddText(text, hover, onClick: $"fillsign:{payload}");
    }

    private string StripFormatting(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, "§.", string.Empty);
    }

    public string Build()
    {
        return System.Text.Json.JsonSerializer.Serialize(components);
    }
    public Models.Mod.DescModification BuildLine()
    {
        return new(System.Text.Json.JsonSerializer.Serialize(components));
    }
}

/// <summary>
/// The click payload the mod's fillsign flow consumes. Property names are the JSON keys the mod
/// reads (<c>line</c>/<c>value</c>/<c>name</c>/<c>slot</c>) - keep them in sync with the mod.
/// </summary>
public class FillSignPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("line")]
    public string Line { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("value")]
    public string Value { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("slot")]
    public int Slot { get; set; }
}

public class LoreComponent
{
    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string Text { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("hover")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string Hover { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("onClick")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string OnClick { get; set; }
}

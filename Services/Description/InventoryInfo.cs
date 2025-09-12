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
        if (data.inventory.Settings.DisableInfoIn?.Contains("Crafting") ?? false)
            return;
        if (Random.Shared.NextDouble() < 0.9 && data.accountInfo?.UserId != "7")
            return;

        var text = Texts[Random.Shared.Next(Texts.Length)];
        if (data.inventory.Version >= 3 && Random.Shared.NextDouble() < 0.1)
        {
            text = new LoreBuilder()
                .AddText("You can drag the SkyCofl info display (this text) to somewhere else by holding `right-click` and moving the mouse")
                .Build();
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

            if (currentLine.Length > 0 && visibleLength + 1 + Regex.Replace(word, "§.", "").Length > 35)
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

    private string StripFormatting(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, "§.", string.Empty);
    }

    public string Build()
    {
        return System.Text.Json.JsonSerializer.Serialize(components);
    }
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

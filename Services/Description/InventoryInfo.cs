using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
        "Searching for a lowballer? `/cofl lowball` will help you find one",
        "Have a useful idea? Post a suggestion on our discord!",
        "Found any weird/wrong thing? Post a bug-report on our discord! Also run `/cofl report <description` to help us fix it",
    };
    public void Apply(DataContainer data)
    {
        if (Random.Shared.NextDouble() < 0.9)
            return;

        var text = Texts[Random.Shared.Next(Texts.Length)];
        var coloredText = Regex.Replace(text, @"`(/.*?)`", m => $"§b{m.Groups[1]}§r");

        if (coloredText.Contains('\n'))
        {
            data.mods.Add(coloredText.Split('\n').Select(line => new Models.Mod.DescModification(line)).ToList());
            return;
        }

        var words = coloredText.Split(' ');
        var result = new StringBuilder();
        var currentLine = new StringBuilder();
        var display = new List<Models.Mod.DescModification>();

        foreach (var word in words)
        {
            // Calculate visible length by removing color codes
            var visibleLength = Regex.Replace(currentLine.ToString(), "§.", "").Length;

            if (currentLine.Length > 0 && visibleLength + 1 + Regex.Replace(word, "§.", "").Length > 35)
            {
                result.AppendLine(currentLine.ToString());
                display.Add(new(currentLine.ToString()));
                currentLine.Clear();
            }

            if (currentLine.Length > 0)
            {
                currentLine.Append(' ');
            }
            currentLine.Append(word);
        }
        display.Add(new(currentLine.ToString()));

        data.mods.Add(display);
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // none
    }
}

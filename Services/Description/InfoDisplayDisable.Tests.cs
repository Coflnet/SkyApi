using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests
{
    public class InfoDisplayDisableTests
    {
        private static List<LoreComponent> Parse(string value)
            => JsonSerializer.Deserialize<List<LoreComponent>>(value);

        private static DataContainer WithMods(params List<DescModification>[] displays)
            => new DataContainer { mods = new List<List<DescModification>>(displays) };

        [Test]
        public void SingleLineDisplay_GetsInvisibleSpaceDisableButton()
        {
            var data = WithMods();
            var before = data.mods.Count;
            data.mods.Add(new List<DescModification> { new("Cheapest fish to add:") });

            InfoDisplayDisable.StampDisableButton(data, before, "fishfamily");

            var components = Parse(data.mods[before][0].Value);
            Assert.That(components, Has.Count.EqualTo(2), "original text kept, disable component appended");
            Assert.That(components[0].Text, Is.EqualTo("Cheapest fish to add:"));
            Assert.That(components[1].Text.Trim(), Is.Empty, "single line disable handle is just empty space");
            Assert.That(components[1].OnClick, Is.EqualTo("/cofl confirm /cofl set loreDisableInfoIn fishfamily"));
        }

        [Test]
        public void MultiLineDisplay_GetsXDisableButton()
        {
            var data = WithMods();
            var before = data.mods.Count;
            data.mods.Add(new List<DescModification>
            {
                new("Cheapest fish to add:"),
                new("1. Some Fish"),
            });

            InfoDisplayDisable.StampDisableButton(data, before, "fishfamily");

            var components = Parse(data.mods[before][0].Value);
            Assert.That(components.Last().Text, Does.Contain("x"), "multi-line display shows a visible x");
        }

        [Test]
        public void StampsIntoExistingComponentLine_PreservingOriginalComponents()
        {
            var data = WithMods();
            var before = data.mods.Count;
            var header = new LoreBuilder().AddText("Top Bazaar Crafts", "hover", "/recipe").BuildLine();
            data.mods.Add(new List<DescModification> { header, new("line 2") });

            InfoDisplayDisable.StampDisableButton(data, before, "bazaar");

            var components = Parse(data.mods[before][0].Value);
            Assert.That(components, Has.Count.EqualTo(2));
            Assert.That(components[0].Text, Is.EqualTo("Top Bazaar Crafts"));
            Assert.That(components[0].Hover, Is.EqualTo("hover"), "original interactive component untouched");
            Assert.That(components[1].OnClick, Is.EqualTo("/cofl confirm /cofl set loreDisableInfoIn bazaar"));
        }

        [Test]
        public void NoDisplayAdded_NoOp()
        {
            var data = WithMods();
            var before = data.mods.Count;
            // modifier added nothing (e.g. early return)
            Assert.DoesNotThrow(() => InfoDisplayDisable.StampDisableButton(data, before, "fishfamily"));
            Assert.That(data.mods, Is.Empty);
        }

        [Test]
        public void NullDisableName_NeverStamps()
        {
            var data = WithMods();
            var before = data.mods.Count;
            data.mods.Add(new List<DescModification> { new("plain") });

            InfoDisplayDisable.StampDisableButton(data, before, null);

            Assert.That(data.mods[before][0].Value, Is.EqualTo("plain"), "item-slot modifiers (null name) stay untouched");
        }

        [Test]
        public void AddReenablePlaceholder_AddsInvisibleReenableHandle()
        {
            var data = WithMods();

            InfoDisplayDisable.AddReenablePlaceholder(data, "fishfamily");

            Assert.That(data.mods, Has.Count.EqualTo(1));
            var components = Parse(data.mods[0][0].Value);
            Assert.That(components[0].Text.Trim(), Is.Empty, "re-enable handle renders no visible lore");
            Assert.That(components[0].OnClick, Is.EqualTo("/cofl confirm /cofl set loreDisableInfoIn rm fishfamily"), "re-enable is also guarded by confirm");
        }

        [Test]
        public void DisableInfoName_AutoGeneratesFromTypeName_WithBackwardCompatOverrides()
        {
            // most modifiers need no extra code - the name is just the type name (default interface member)
            Assert.That(((ICustomModifier)new FishFamilyCalculator()).DisableInfoName, Is.EqualTo("FishFamilyCalculator"));
            Assert.That(((ICustomModifier)new InstantBuyMaxAmount()).DisableInfoName, Is.EqualTo("InstantBuyMaxAmount"));
            // the two documented keys are preserved via an explicit override
            Assert.That(((ICustomModifier)new InventoryInfo()).DisableInfoName, Is.EqualTo("Crafting"));
            Assert.That(((ICustomModifier)new BazaarInfo()).DisableInfoName, Is.EqualTo("bazaar"));
        }

        [Test]
        public void IsInfoDisabled_MatchesCaseInsensitively()
        {
            var data = new DataContainer
            {
                inventory = new InventoryDataWithSettings
                {
                    Settings = new DescriptionSetting { DisableInfoIn = new HashSet<string> { "fishfamilycalculator" } }
                }
            };

            Assert.That(new FishFamilyCalculator().IsInfoDisabled(data), Is.True, "case-insensitive match on the auto-generated name");
            Assert.That(new BazaarInfo().IsInfoDisabled(data), Is.False, "unrelated display stays enabled");
        }
    }
}

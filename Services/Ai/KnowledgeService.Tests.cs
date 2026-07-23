using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Ai;

public class KnowledgeServiceTests
{
    [Test]
    public void FilterKnowledgeContent_ContainsSearchableNameSyntaxAndOptions()
    {
        var filter = JObject.Parse("""
            {
              "name": "ExoticColor",
              "options": ["Any", "Exotic", "Original", "Fairy+Crystal"],
              "longType": "Equal",
              "description": "Classifies exotic colors"
            }
            """);

        var content = KnowledgeService.FilterKnowledgeContent(filter);

        Assert.That(content, Does.Contain("SkyCofl filter name: ExoticColor"));
        Assert.That(content, Does.Contain("Syntax: ExoticColor=VALUE"));
        Assert.That(content, Does.Contain("Any, Exotic, Original, Fairy+Crystal"));
    }
}

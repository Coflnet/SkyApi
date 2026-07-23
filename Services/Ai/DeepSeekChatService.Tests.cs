using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Ai;

public class DeepSeekChatServiceTests
{
    [TestCase("<｜DSML｜tool_calls><｜DSML｜invoke name=\"search_api_tools\">")]
    [TestCase("<tool_calls><invoke name=\"search_knowledge\"><parameter name=\"query\">exotics</parameter></invoke></tool_calls>")]
    [TestCase("tool_calls: [{ \"function\": \"search_item\" }]")]
    [TestCase("{ \"tool_calls\": [{ \"function\": { \"name\": \"search_item\" } }] }")]
    [TestCase("<|DSML|tool_calls>")]
    [TestCase("<|analysis|>I should call search_knowledge")]
    [TestCase("<think>I should call search_knowledge first.</think>")]
    [TestCase("reasoning_content: I need more evidence")]
    [TestCase("tool result\u0000hidden")]
    [TestCase("{ \"name\": \"get_price\", \"arguments\": { \"item_tag\": \"HYPERION\" } }")]
    [TestCase("{ \"name\": \"search_filter_options\", \"arguments\": { \"query\": \"exotics\" } }")]
    public void IsPlausibleAnswer_RejectsLeakedToolMarkup(string answer)
    {
        Assert.That(DeepSeekChatService.IsPlausibleAnswer(answer), Is.False);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("...")]
    public void IsPlausibleAnswer_RejectsEmptyOrNonAnswerContent(string answer)
    {
        Assert.That(DeepSeekChatService.IsPlausibleAnswer(answer), Is.False);
    }

    [TestCase("answer answer answer answer answer answer answer answer answer answer answer answer answer answer answer answer answer answer answer answer")]
    [TestCase("same line\nsame line\nsame line")]
    public void IsPlausibleAnswer_RejectsExcessiveRepetition(string answer)
    {
        Assert.That(DeepSeekChatService.IsPlausibleAnswer(answer), Is.False);
    }

    [Test]
    public void IsPlausibleAnswer_AcceptsNormalAnswer()
    {
        Assert.That(
            DeepSeekChatService.IsPlausibleAnswer("Use the `!exotic` filter. See [the filter guide](/wiki/filter)."),
            Is.True);
        Assert.That(
            DeepSeekChatService.IsPlausibleAnswer("The API response may contain a `tool_calls` field."),
            Is.True);
    }

    [Test]
    public void SearchFilterOptions_FindsLiveExoticColorDefinition()
    {
        var options = Newtonsoft.Json.Linq.JArray.Parse("""
            [
              {"name":"ExoticColor","options":["Any","Exotic","Original","Fairy+Crystal"],"description":"Classifies exotic colors"},
              {"name":"Rarity","options":["COMMON","RARE"],"description":"Item tier"}
            ]
            """);

        var result = DeepSeekChatService.SearchFilterOptions(options, "how do I filter for exotics now");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That((string)result[0]["name"], Is.EqualTo("ExoticColor"));
        Assert.That(result[0]["options"]?.ToObject<string[]>(), Does.Contain("Exotic"));
    }
}

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
}

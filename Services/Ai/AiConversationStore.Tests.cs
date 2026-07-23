using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Ai;

public class AiConversationStoreTests
{
    [Test]
    public void CreateConversationId_ReturnsAcceptedFormat()
    {
        Assert.That(AiConversationStore.CreateConversationId(), Does.Match("^[a-f0-9]{32}$"));
    }
}

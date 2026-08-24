using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Ai;

/// <summary>Contains AI conversation store tests.</summary>
public class AiConversationStoreTests
{
    /// <summary>Creates conversation id returns accepted format.</summary>
    [Test]
    public void CreateConversationId_ReturnsAcceptedFormat()
    {
        Assert.That(AiConversationStore.CreateConversationId(), Does.Match("^[a-f0-9]{32}$"));
    }
}

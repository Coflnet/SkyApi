using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

#pragma warning disable CS1591
public class AccountDeletionServiceTests
{
    private class FakeAccountDeletionClient : IAccountDeletionClient
    {
        public string? LastEmail;
        public string? LastGoogleId;
        public string ReturnedUserId = "123";

        public Task<string> DeleteUser(string email, string googleId)
        {
            LastEmail = email;
            LastGoogleId = googleId;
            return Task.FromResult(ReturnedUserId);
        }
    }

    [Test]
    public async Task DeleteAccount_CallsDeletionClientWithEmailAndGoogleId()
    {
        var client = new FakeAccountDeletionClient();
        var service = new AccountDeletionService(client);
        var user = new GoogleUser { Id = 42, Email = "someone@example.com", GoogleId = "google-42" };

        await service.DeleteAccount(user);

        Assert.That(client.LastEmail, Is.EqualTo("someone@example.com"));
        Assert.That(client.LastGoogleId, Is.EqualTo("google-42"));
    }

    [Test]
    public async Task DeleteAccount_ReturnsIdFromDeletionClient()
    {
        var client = new FakeAccountDeletionClient { ReturnedUserId = "42" };
        var service = new AccountDeletionService(client);
        var user = new GoogleUser { Id = 42, Email = "someone@example.com", GoogleId = "google-42" };

        var result = await service.DeleteAccount(user);

        Assert.That(result.UserId, Is.EqualTo("42"));
    }

    [Test]
    public async Task DeleteAccount_NeverClaimsFullErasure()
    {
        // regression guard: the response must always be explicit about what is retained,
        // so the endpoint can never be mistaken for a guarantee of full erasure
        var service = new AccountDeletionService(new FakeAccountDeletionClient());
        var user = new GoogleUser { Id = 1, Email = "a@b.com", GoogleId = "g1" };

        var result = await service.DeleteAccount(user);

        Assert.That(result.Retained, Is.Not.Empty);
        Assert.That(result.Erased, Is.Not.Empty);
    }

    [Test]
    public void DeleteAccount_ThrowsForNullUser()
    {
        var service = new AccountDeletionService(new FakeAccountDeletionClient());

        Assert.ThrowsAsync<ArgumentNullException>(() => service.DeleteAccount(null!));
    }
}
#pragma warning restore CS1591

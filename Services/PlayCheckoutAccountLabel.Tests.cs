using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.McConnect.Model;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

#pragma warning disable CS1591
public class PlayCheckoutAccountLabelTests
{
    [Test]
    public async Task PrefersMostRecentlyUsedVerifiedAccount()
    {
        var service = new PlayCheckoutAccountLabel((id, token) =>
        {
            Assert.That(id, Is.EqualTo("user-id"));
            Assert.That(token.CanBeCanceled, Is.True);
            return Task.FromResult(new User(accounts:
            [
                new MinecraftUuid(accountUuid: "old", verified: true, lastRequestedAt: DateTime.UtcNow.AddDays(-1)),
                new MinecraftUuid(accountUuid: "new", verified: true, lastRequestedAt: DateTime.UtcNow),
                new MinecraftUuid(accountUuid: "unverified", verified: false, lastRequestedAt: DateTime.UtcNow.AddDays(1))
            ]));
        }, uuid =>
        {
            Assert.That(uuid, Is.EqualTo("new"));
            return Task.FromResult("MinecraftName");
        }, TimeSpan.FromSeconds(1));

        Assert.That(await service.GetAsync("user-id", "alice@example.com"), Is.EqualTo("MinecraftName"));
    }

    [TestCase(null, "your account")]
    [TestCase("", "your account")]
    [TestCase("invalid", "your account")]
    [TestCase("@example.com", "your account")]
    [TestCase("alice@", "your account")]
    [TestCase("alice@other@example.com", "your account")]
    [TestCase("alice@example.com", "a***@example.com")]
    [TestCase("a@example.com", "a***@example.com")]
    [TestCase("𐐀a@example.com", "𐐀***@example.com")]
    [TestCase("\ralice\n@example.com\t", "a***@example.com")]
    public async Task MissingAccountUsesSafeFallback(string email, string expected)
    {
        var service = new PlayCheckoutAccountLabel((_, _) => Task.FromResult(new User()),
            _ => throw new AssertionException("No name lookup is expected without a verified account"), TimeSpan.FromSeconds(1));
        Assert.That(await service.GetAsync("user-id", email), Is.EqualTo(expected));
    }

    [Test]
    public async Task UnverifiedAccountIsNeverUsed()
    {
        var called = false;
        var service = new PlayCheckoutAccountLabel((_, _) => Task.FromResult(new User(accounts:
            [new MinecraftUuid(accountUuid: "unverified", verified: false)])), _ =>
            {
                called = true;
                return Task.FromResult("UnverifiedName");
            }, TimeSpan.FromSeconds(1));
        Assert.That(await service.GetAsync("user-id", null), Is.EqualTo("your account"));
        Assert.That(called, Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("\r\n\t")]
    public async Task MissingNameUsesMaskedEmail(string name)
    {
        var service = WithName(_ => Task.FromResult(name));
        Assert.That(await service.GetAsync("user-id", "alice@example.com"), Is.EqualTo("a***@example.com"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task LookupFailureUsesMaskedEmail(bool failAccountLookup)
    {
        var service = new PlayCheckoutAccountLabel((_, _) => failAccountLookup
            ? Task.FromException<User>(new Exception("unavailable")) : Task.FromResult(LinkedUser()),
            _ => Task.FromException<string>(new Exception("unavailable")), TimeSpan.FromSeconds(1));
        Assert.That(await service.GetAsync("user-id", "alice@example.com"), Is.EqualTo("a***@example.com"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TimeoutBoundsAccountAndNameLookupEvenWithoutCancellationSupport(bool hangAccountLookup)
    {
        var service = new PlayCheckoutAccountLabel((_, _) => hangAccountLookup
            ? new TaskCompletionSource<User>().Task : Task.FromResult(LinkedUser()),
            _ => new TaskCompletionSource<string>().Task, TimeSpan.FromMilliseconds(30));
        var watch = Stopwatch.StartNew();
        Assert.That(await service.GetAsync("user-id", "alice@example.com").WaitAsync(TimeSpan.FromSeconds(2)),
            Is.EqualTo("a***@example.com"));
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task RemovesControlsAndCapsLabelLength()
    {
        var service = WithName(_ => Task.FromResult("\r\n\t\u2028\u2029\u202e" + new string('x', 100)));
        Assert.That(await service.GetAsync("user-id", null), Is.EqualTo(new string('x', 80)));
    }

    [Test]
    public async Task MasksBeforeLimitingEmailLength()
    {
        var service = WithName(_ => Task.FromResult<string>(null));
        Assert.That(await service.GetAsync("user-id", new string('a', 100) + "@example.com"),
            Is.EqualTo("a***@example.com"));
        Assert.That((await service.GetAsync("user-id", "alice@" + new string('d', 100))).Length,
            Is.EqualTo(80));
    }

    private static User LinkedUser() => new User(accounts:
        [new MinecraftUuid(accountUuid: "verified", verified: true)]);

    private static PlayCheckoutAccountLabel WithName(Func<string, Task<string>> name) =>
        new((_, _) => Task.FromResult(LinkedUser()), name, TimeSpan.FromSeconds(1));
}
#pragma warning restore CS1591

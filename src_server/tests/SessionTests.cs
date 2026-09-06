using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The token `coai-mcp` carries: minted from an identity provider's, spendable, revocable, and
/// never outliving its deadline.
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SessionTests
{
    private static async Task<SessionDto> IssueAsync(TeamServer server, string email)
    {
        using var client = server.ClientFor(email, "A Developer");
        var response = await client.PostAsync("/api/session", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<SessionDto>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task ASession_IsMintedFromATokenAndThenCarriesTheCaller()
    {
        using var server = new TeamServer();
        var session = await IssueAsync(server, $"dev@{TeamServer.Domain}");

        using var withSession = server.WithToken(session.Token);
        var me = await withSession.GetFromJsonAsync<WhoAmIDto>("/api/whoami", TestContext.Current.CancellationToken);

        session.Email.Should().Be($"dev@{TeamServer.Domain}");
        session.ExpiresUtc.Should().BeAfter(DateTimeOffset.UtcNow);
        me!.Email.Should().Be($"dev@{TeamServer.Domain}");
        me.Name.Should().Be("A Developer", "the name travels with the session, not only the email");
    }

    [Fact]
    public async Task ARevokedSession_IsRefusedAfterwards()
    {
        using var server = new TeamServer();
        var session = await IssueAsync(server, $"dev@{TeamServer.Domain}");
        using var client = server.WithToken(session.Token);

        (await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A token that could mint its own successor would never expire, which is the one property a
    /// deadline exists to give it.
    /// </summary>
    [Fact]
    public async Task ASession_CannotMintAnotherSession()
    {
        using var server = new TeamServer();
        var session = await IssueAsync(server, $"dev@{TeamServer.Domain}");
        using var client = server.WithToken(session.Token);

        var response = await client.PostAsync("/api/session", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Answering 204 to somebody holding Microsoft's token would say a credential was withdrawn
    /// when nothing was: a stateless token is not this server's to revoke.
    /// </summary>
    [Fact]
    public async Task RevokingWithAnIdentityProvidersToken_SaysWhatItCannotDo()
    {
        using var server = new TeamServer();
        using var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var response = await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("cannot withdraw");
    }

    /// <summary>
    /// The raw token is never written down, so a stolen data directory yields no bearer.
    /// </summary>
    [Fact]
    public async Task TheStoredSession_HoldsNoTokenAndIsNamedByItsHash()
    {
        using var server = new TeamServer();
        var session = await IssueAsync(server, $"dev@{TeamServer.Domain}");

        var files = Directory.GetFiles(Path.Combine(server.DataDir, "sessions"), "*.json");

        files.Should().ContainSingle();
        Path.GetFileName(files[0]).Should().Be(SessionStore.FileNameFor(session.Token));
        (await File.ReadAllTextAsync(files[0], TestContext.Current.CancellationToken))
            .Should().NotContain(session.Token, "the file is named by the hash so it need not hold the token");
    }

    /// <summary>
    /// A domain removed from the allow-list stops a LIVE session, not just the next sign-in —
    /// otherwise somebody who left keeps a week of access to the company's subscriptions.
    /// </summary>
    /// <remarks>Raised on this story's plan round, before a session had been issued.</remarks>
    [Fact]
    public async Task ASessionWhoseDomainIsNoLongerAllowed_StopsBeingServed()
    {
        var data = Path.Combine(Path.GetTempPath(), "coai-server-tests", Guid.NewGuid().ToString("N"));
        SessionDto session;
        using (var before = new TeamServer(new Dictionary<string, string?> { ["Coai__DataDir"] = data }))
        {
            session = await IssueAsync(before, $"leaver@{TeamServer.Domain}");
        }

        // The same data directory, the same live session file — and a company that no longer
        // includes that domain.
        using var after = new TeamServer(new Dictionary<string, string?>
        {
            ["Coai__DataDir"] = data,
            ["Coai__AllowedDomains"] = "another.example",
        });
        using var client = after.WithToken(session.Token);

        (await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void AnExpiredSession_IsRefusedAndSweptOnSight()
    {
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var store = new SessionStore(data, TimeSpan.FromDays(7));
        var (token, _) = store.Issue($"dev@{TeamServer.Domain}", "A Developer", DateTimeOffset.UtcNow);

        store.Validate(token, DateTimeOffset.UtcNow).Should().NotBeNull();
        store.Validate(token, DateTimeOffset.UtcNow.AddDays(8)).Should().BeNull("the deadline is absolute");
        Directory.GetFiles(Path.Combine(data, "sessions")).Should().BeEmpty(
            "the request that found it expired is the cheapest place to remove it");
    }

    /// <summary>
    /// The deadline does not move when a session is used. A sliding window would let a stolen token
    /// live for as long as the thief kept using it.
    /// </summary>
    [Fact]
    public void UsingASession_DoesNotExtendIt()
    {
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var store = new SessionStore(data, TimeSpan.FromDays(7));
        var start = DateTimeOffset.UtcNow;
        var (token, issued) = store.Issue($"dev@{TeamServer.Domain}", "", start);

        var later = store.Validate(token, start.AddDays(3));

        later!.ExpiresUtc.Should().Be(issued.ExpiresUtc);
        later.LastUsedUtc.Should().BeCloseTo(start.AddDays(3), TimeSpan.FromSeconds(1),
            "last used is informational, and it is what moves");
    }

    [Fact]
    public void TheSweep_TakesTheExpiredAndLeavesTheLiving()
    {
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var store = new SessionStore(data, TimeSpan.FromDays(7));
        var start = DateTimeOffset.UtcNow;
        store.Issue($"old@{TeamServer.Domain}", "", start.AddDays(-30));
        var (live, _) = store.Issue($"new@{TeamServer.Domain}", "", start);

        store.Sweep(start).Should().Be(1);

        store.Validate(live, start).Should().NotBeNull();
    }
}

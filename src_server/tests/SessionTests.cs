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

    /// <summary>
    /// Revoking says whether it actually happened, and revoking twice is not a failure.
    /// </summary>
    /// <remarks>
    /// Raised as Blocking on this story's code round: the endpoint answered 204 whatever the
    /// filesystem did, so a delete that failed told a person their credential was withdrawn while
    /// a stolen bearer went on working until it expired. The IO-failure branch itself is not
    /// portably provokable — a file that refuses deletion is a Windows sharing violation and a
    /// no-op on Linux — so what is pinned here is the contract the endpoint now reads.
    /// </remarks>
    [Fact]
    public void RevokingSaysWhetherItHappened_AndTwiceIsStillSuccess()
    {
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var store = new SessionStore(data, TimeSpan.FromDays(7));
        var (token, _) = store.Issue($"dev@{TeamServer.Domain}", "", DateTimeOffset.UtcNow);

        store.Revoke(token).Should().BeTrue();
        store.Revoke(token).Should().BeTrue("a session that is already gone is exactly as withdrawn");
        store.Validate(token, DateTimeOffset.UtcNow).Should().BeNull();
    }

    /// <summary>
    /// A read that fails is REPORTED, even though the caller still sees a plain 401: an unreadable
    /// session and an unknown one are the same answer to a client and completely different things
    /// to an operator.
    /// </summary>
    [Fact]
    public void ASessionFileThatWillNotParse_IsReported_AndRefused()
    {
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var reported = new List<string>();
        var store = new SessionStore(data, TimeSpan.FromDays(7), (message, _) => reported.Add(message));
        var (token, _) = store.Issue($"dev@{TeamServer.Domain}", "", DateTimeOffset.UtcNow);
        File.WriteAllText(
            Path.Combine(data, "sessions", SessionStore.FileNameFor(token)), "{ this is not json");

        store.Validate(token, DateTimeOffset.UtcNow).Should().BeNull();

        reported.Should().ContainSingle().Which.Should().Contain("could not be read");
    }

    [Fact]
    public async Task ARefusal_IsJson_LikeEveryOtherAnswerHere()
    {
        // A client deserialising this API's ErrorDto must not meet text/plain where the sentence is.
        using var server = new TeamServer();
        using var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var response = await client.DeleteAsync("/api/session", TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
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

    /// <summary>
    /// The hourly last-used stamp is informational, and a stamp that cannot land must not log a
    /// person out: a listing holding the file (or any reader without the delete share) is a
    /// warning in the log, not a refused request.
    /// </summary>
    /// <remarks>Own review of the people endpoint, 2026-10-09. Windows-only: Linux lets the rename through.</remarks>
    [Fact]
    public void AStampThatCannotBeWritten_StillAnswersTheSession_AndIsReported()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to replace a file somebody holds open");
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var reported = new List<string>();
        var store = new SessionStore(data, TimeSpan.FromDays(7), (message, _) => reported.Add(message));
        var start = DateTimeOffset.UtcNow;
        var (token, _) = store.Issue($"dev@{TeamServer.Domain}", "", start);
        // Somebody holding the file open WITHOUT the delete share: an older reader, a backup, an editor.
        using var holder = new FileStream(
            Path.Combine(data, "sessions", SessionStore.FileNameFor(token)), FileMode.Open, FileAccess.Read, FileShare.Read);

        var later = store.Validate(token, start.AddHours(2));

        later.Should().NotBeNull("the stamp decides nothing, so failing to write it must refuse nothing");
        later!.LastUsedUtc.Should().Be(start.AddHours(2), "the caller still sees the session as it now is");
        reported.Should().ContainSingle().Which.Should().Contain("could not be written");
        Directory.GetFiles(Path.Combine(data, "sessions"), "*.tmp").Should().BeEmpty(
            "a refused rename must not leave its temporary behind — this happens once an hour, for ever");
    }

    /// <summary>
    /// The sweep deletes only what it has READ and judged. A file it could not read this time — held
    /// by a backup, an editor, or this server's own rename under a last-used stamp — is a file it
    /// knows nothing about, and deleting it deleted a live session once an hour on Windows.
    /// </summary>
    /// <remarks>Risk consultation on story 2.1, 2026-10-10 (HIGH). Windows-only: only Windows lets a holder deny other readers.</remarks>
    [Fact]
    public void TheSweep_KeepsASessionItCouldNotReadThisTime()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets a holder deny other readers");
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var reported = new List<string>();
        var store = new SessionStore(data, TimeSpan.FromDays(7), (message, _) => reported.Add(message));
        var now = DateTimeOffset.UtcNow;
        var (token, _) = store.Issue($"dev@{TeamServer.Domain}", "", now);
        int swept;
        using (new FileStream(
            Path.Combine(data, "sessions", SessionStore.FileNameFor(token)), FileMode.Open, FileAccess.Read, FileShare.Delete))
        {
            // Every other READ is refused while this is held; a delete would go through.
            swept = store.Sweep(now);
        }

        store.Validate(token, now).Should().NotBeNull("a file the sweep could not read is not provably litter");
        swept.Should().Be(0);
        reported.Should().ContainSingle().Which.Should().Contain("kept");
    }

    [Fact]
    public void TheSweep_RemovesATornFile_AndSaysSo()
    {
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var reported = new List<string>();
        var store = new SessionStore(data, TimeSpan.FromDays(7), (message, _) => reported.Add(message));
        var now = DateTimeOffset.UtcNow;
        var (live, _) = store.Issue($"dev@{TeamServer.Domain}", "", now);
        var torn = Path.Combine(data, "sessions", "torn.json");
        File.WriteAllText(torn, "{ this is not json");

        store.Sweep(now).Should().Be(1, "a torn write is litter, read whole and judged");

        File.Exists(torn).Should().BeFalse();
        store.Validate(live, now).Should().NotBeNull();
        reported.Should().ContainSingle().Which.Should().Contain("torn.json");
    }

    /// <summary>
    /// Valid JSON, a future deadline, and no email: nobody can present it, the roster cannot name it,
    /// and the gate would throw on it. Litter — removed by the sweep, and said once, there.
    /// </summary>
    [Fact]
    public void TheSweep_RemovesASessionNobodyCanBeAuthorisedAs()
    {
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var reported = new List<string>();
        var store = new SessionStore(data, TimeSpan.FromDays(7), (message, _) => reported.Add(message));
        var now = DateTimeOffset.UtcNow;
        store.Issue($"dev@{TeamServer.Domain}", "", now);
        var nobody = Path.Combine(data, "sessions", "nobody.json");
        File.WriteAllText(nobody, $$"""{"email":null,"name":"Nobody","createdUtc":"{{now:O}}","expiresUtc":"{{now.AddDays(3):O}}","lastUsedUtc":"{{now:O}}"}""");

        store.Sweep(now).Should().Be(1, "a record with no email is a session nobody can present");

        File.Exists(nobody).Should().BeFalse();
        reported.Should().ContainSingle().Which.Should().Contain("nobody.json");
    }

    [Fact]
    public void Validate_RefusesARecordWithNoEmail()
    {
        var data = Directory.CreateTempSubdirectory("coai-session-").FullName;
        var store = new SessionStore(data, TimeSpan.FromDays(7));
        var now = DateTimeOffset.UtcNow;
        const string token = "a-token-somebody-wrote-a-file-for";
        Directory.CreateDirectory(Path.Combine(data, "sessions"));
        File.WriteAllText(
            Path.Combine(data, "sessions", SessionStore.FileNameFor(token)),
            $$"""{"email":null,"name":"Nobody","createdUtc":"{{now:O}}","expiresUtc":"{{now.AddDays(3):O}}","lastUsedUtc":"{{now:O}}"}""");

        store.Validate(token, now).Should().BeNull("a record with no email cannot name a caller, and the claims built from it would throw");
    }

    /// <summary>
    /// The sweep's decision, without a filesystem: only what was READ and JUDGED is litter.
    /// </summary>
    [Fact]
    public void TheSweepsVerdict_RemovesOnlyWhatItHasReadAndJudged()
    {
        var now = DateTimeOffset.UtcNow;
        SessionRecord Record(string email, DateTimeOffset expires) => new(email, "", now.AddDays(-1), expires, now.AddDays(-1));

        using var scope = new FluentAssertions.Execution.AssertionScope();
        SessionStore.Classify(new FileRead<SessionRecord>.Unreadable(new IOException("held by somebody")), now)
            .Should().Be(SessionStore.Litter.None, "could not be read THIS time says nothing about the file — keep it");
        SessionStore.Classify(new FileRead<SessionRecord>.Absent(), now)
            .Should().Be(SessionStore.Litter.None, "gone already; nothing to decide");
        SessionStore.Classify(new FileRead<SessionRecord>.Found(Record($"dev@{TeamServer.Domain}", now.AddDays(3))), now)
            .Should().Be(SessionStore.Litter.None);
        SessionStore.Classify(new FileRead<SessionRecord>.Found(Record($"dev@{TeamServer.Domain}", now.AddSeconds(-1))), now)
            .Should().Be(SessionStore.Litter.Expired);
        SessionStore.Classify(new FileRead<SessionRecord>.Corrupt(new System.Text.Json.JsonException("torn")), now)
            .Should().Be(SessionStore.Litter.Corrupt);
        SessionStore.Classify(new FileRead<SessionRecord>.Found(Record(null!, now.AddDays(3))), now)
            .Should().Be(SessionStore.Litter.Unusable, "no email: nobody can present it and the gate would throw on it");
        SessionStore.Classify(new FileRead<SessionRecord>.Found(Record("", now.AddDays(3))), now)
            .Should().Be(SessionStore.Litter.Unusable);
    }
}

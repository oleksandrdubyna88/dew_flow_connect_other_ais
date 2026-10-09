using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// Who has signed in — the admin's roster of unexpired sessions, projected to three fields and
/// nothing else.
/// </summary>
/// <remarks>
/// Story 2.1 of <c>PLAN_team_usage_by_person.md</c>. The risk this endpoint carries is the one its
/// plan names first: a wrong admin check hands every colleague's email and activity to any caller.
/// So the first test is the refusal, and the shape test pins that nothing beyond the three fields
/// — no token, no session id, no creation or expiry — is on the wire.
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class PeopleTests
{
    private const string Route = "/api/people";

    private static string Admin => $"boss@{TeamServer.Domain}";

    private static string Dev => $"dev@{TeamServer.Domain}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Signs <paramref name="email"/> in through the real route, so a session file exists.</summary>
    private static async Task SignInAsync(TeamServer server, string email, string? name)
    {
        using var client = server.ClientFor(email, name);
        var response = await client.PostAsync("/api/session", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<JsonElement> ListAsync(TeamServer server)
    {
        using var client = server.ClientFor(Admin);
        var response = await client.GetAsync(Route, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    private static IEnumerable<string> Emails(JsonElement people) =>
        people.EnumerateArray().Select(p => p.GetProperty("email").GetString()!);

    [Fact]
    public async Task ANonAdmin_IsRefused_AndToldWhereToAsk()
    {
        using var server = new TeamServer();
        using var client = server.ClientFor(Dev);

        var refused = await client.GetAsync(Route, Ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<ErrorDto>(Ct))!.Error.Should().Contain("Coai:Admins");
    }

    [Fact]
    public async Task PeopleAreRefusedWithoutASignIn()
    {
        using var server = new TeamServer();

        (await server.CreateClient().GetAsync(Route, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnExpiredSession_IsNotListed_AndItsFileIsLeftAlone()
    {
        using var server = new TeamServer();
        // The living one goes through the route, which also starts the host — and its startup
        // sweep — BEFORE the expired file is written, so what this test observes is the listing's
        // own behaviour and not the sweep's.
        await SignInAsync(server, Dev, "A Developer");
        var store = new SessionStore(server.DataDir, TimeSpan.FromDays(7));
        store.Issue($"gone@{TeamServer.Domain}", "Gone Away", DateTimeOffset.UtcNow.AddDays(-30));

        var people = await ListAsync(server);

        Emails(people).Should().BeEquivalentTo([Dev], "an expired session is not a person who is signed in");
        Directory.GetFiles(Path.Combine(server.DataDir, "sessions"), "*.json").Should().HaveCount(2,
            "a listing READS; it never deletes — that is the sweep's job, on its own clock");
    }

    [Fact]
    public async Task TwoSessionsOfOnePerson_AreOneRow_WithTheLatestNameAndTheLatestUse()
    {
        using var server = new TeamServer();
        await SignInAsync(server, Dev, "A Developer");
        var store = new SessionStore(server.DataDir, TimeSpan.FromDays(7));
        var now = DateTimeOffset.UtcNow;
        // Three casings from the identity provider, three sessions. The OLDEST was used most
        // recently — a laptop that never signs out — and the newest carries no name at all.
        // Issue stamps last-used = created, so without the Validate below "newest" and "latest
        // used" would be the same session and the max could not be told from the newest.
        var (oldest, _) = store.Issue($"Alice@{TeamServer.Domain}", "Alice Old", now.AddHours(-3));
        store.Issue($"alice@{TeamServer.Domain}", "Alice New", now.AddHours(-2));
        store.Issue($"ALICE@{TeamServer.Domain}", "", now.AddHours(-1));
        var usedLast = now.AddMinutes(-30);
        store.Validate(oldest, usedLast)!.LastUsedUtc.Should().Be(usedLast, "the fixture must have moved the stamp it relies on");

        var people = await ListAsync(server);

        var alice = people.EnumerateArray()
            .Should().ContainSingle(p => p.GetProperty("email").GetString()!.EndsWith($"@{TeamServer.Domain}", StringComparison.Ordinal)
                                         && p.GetProperty("email").GetString()!.StartsWith("alice", StringComparison.OrdinalIgnoreCase),
                "two casings of one email are one person, not two")
            .Subject;
        alice.GetProperty("email").GetString().Should().Be($"ALICE@{TeamServer.Domain}", "the newest session's casing, exactly");
        alice.GetProperty("displayName").GetString().Should().Be("Alice New", "the latest NON-EMPTY name wins");
        alice.GetProperty("lastUsedUtc").GetDateTimeOffset().Should().Be(usedLast,
            "last used is the latest over the person's unexpired sessions — here the OLDEST session's");
    }

    /// <summary>
    /// A positional record's omitted or null field arrives null whatever the type says (doctrine
    /// §4a). <c>Issue</c> normalises the name at mint; a file written by hand or by an older build
    /// need not have been, and one such file must not turn the whole roster into a 500.
    /// </summary>
    [Fact]
    public async Task ASessionFileWithANullName_IsListedWithAnEmptyName()
    {
        using var server = new TeamServer();
        await SignInAsync(server, Dev, "A Developer");
        var expires = DateTimeOffset.UtcNow.AddDays(3).ToString("O");
        var used = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        await File.WriteAllTextAsync(
            Path.Combine(server.DataDir, "sessions", "handwritten.json"),
            $$"""{"email":"nameless@{{TeamServer.Domain}}","name":null,"createdUtc":"{{used}}","expiresUtc":"{{expires}}","lastUsedUtc":"{{used}}"}""",
            Ct);

        var people = await ListAsync(server);

        people.EnumerateArray().Single(p => p.GetProperty("email").GetString() == $"nameless@{TeamServer.Domain}")
            .GetProperty("displayName").GetString().Should().BeEmpty();
    }

    /// <summary>A field this build does not know, written by a newer one, is not a reason to drop the person.</summary>
    [Fact]
    public async Task ASessionFileWithAnUnknownField_IsStillListed()
    {
        using var server = new TeamServer();
        await SignInAsync(server, Dev, "A Developer");
        var stamp = DateTimeOffset.UtcNow.ToString("O");
        var expires = DateTimeOffset.UtcNow.AddDays(3).ToString("O");
        await File.WriteAllTextAsync(
            Path.Combine(server.DataDir, "sessions", "newer.json"),
            $$"""{"email":"newer@{{TeamServer.Domain}}","name":"From A Newer Build","createdUtc":"{{stamp}}","expiresUtc":"{{expires}}","lastUsedUtc":"{{stamp}}","device":"laptop"}""",
            Ct);

        Emails(await ListAsync(server)).Should().Contain($"newer@{TeamServer.Domain}");
    }

    /// <summary>
    /// A domain removed from the allow-list stops a live session (<c>SessionTests</c>), so a roster
    /// that still listed it would show an admin somebody the server refuses on every request.
    /// </summary>
    [Fact]
    public async Task ASessionWhoseDomainIsNoLongerAllowed_IsNotListed()
    {
        var data = Path.Combine(Path.GetTempPath(), "coai-server-tests", Guid.NewGuid().ToString("N"));
        using (var before = new TeamServer(new Dictionary<string, string?> { ["Coai__DataDir"] = data }))
        {
            await SignInAsync(before, $"leaver@{TeamServer.Domain}", "A Leaver");
        }

        // The same data directory, the same live session file — and a company that no longer
        // includes that domain, administered from the one that does.
        using var after = new TeamServer(new Dictionary<string, string?>
        {
            ["Coai__DataDir"] = data,
            ["Coai__AllowedDomains"] = "another.example",
            ["Coai__Admins"] = "boss@another.example",
        });
        await SignInAsync(after, "stayer@another.example", "A Stayer");
        using var admin = after.ClientFor("boss@another.example");
        var response = await admin.GetAsync(Route, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var people = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        Emails(people).Should().BeEquivalentTo(["stayer@another.example"], "a session the gate would refuse is not somebody who is signed in");
    }

    [Fact]
    public async Task PeopleAreSortedByEmail()
    {
        using var server = new TeamServer();
        await SignInAsync(server, $"zed@{TeamServer.Domain}", "Zed");
        await SignInAsync(server, $"amy@{TeamServer.Domain}", "Amy");
        await SignInAsync(server, Dev, "A Developer");

        Emails(await ListAsync(server)).Should().ContainInOrder($"amy@{TeamServer.Domain}", Dev, $"zed@{TeamServer.Domain}");
    }

    /// <summary>
    /// The projection is the privacy boundary: a session record holds its creation, its deadline
    /// and is named by its token's hash, and none of that is anybody's business but the server's.
    /// </summary>
    [Fact]
    public async Task TheWire_CarriesExactlyThreeFields_PerPerson()
    {
        using var server = new TeamServer();
        await SignInAsync(server, Dev, "A Developer");

        var people = await ListAsync(server);

        people.ValueKind.Should().Be(JsonValueKind.Array);
        var row = people.EnumerateArray().Should().ContainSingle().Subject;
        row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["email", "displayName", "lastUsedUtc"],
            "never a token, a session id, a creation or an expiry");
        row.GetProperty("displayName").GetString().Should().Be("A Developer");
    }

    [Fact]
    public async Task AHostileDisplayName_RoundTripsAsData()
    {
        // A display name is identity-provider text. The server stores and answers it as a string;
        // escaping it is the page's job, and a server that "cleaned" it would hand the client a name
        // that is not the person's.
        const string hostile = "<img src=x onerror=\"alert('x')\"> & \"quotes\"";
        using var server = new TeamServer();
        await SignInAsync(server, Dev, hostile);

        var people = await ListAsync(server);

        people.EnumerateArray().Single().GetProperty("displayName").GetString().Should().Be(hostile);
    }
}

/// <summary>
/// <see cref="SessionStore.Active"/> on its own: a read over the files that never writes, and never
/// says a word about a file it could not read.
/// </summary>
public sealed class SessionStoreActiveTests
{
    private static string FreshDir() => Directory.CreateTempSubdirectory("coai-people-").FullName;

    [Fact]
    public void Active_OnAStoreThatNeverIssuedASession_IsEmpty()
    {
        var store = new SessionStore(FreshDir(), TimeSpan.FromDays(7));

        store.Active(DateTimeOffset.UtcNow).Should().BeEmpty();
    }

    [Fact]
    public void Active_ListsTheLiving_AndLeavesTheExpiredFileInPlace()
    {
        var data = FreshDir();
        var store = new SessionStore(data, TimeSpan.FromDays(7));
        var now = DateTimeOffset.UtcNow;
        store.Issue($"old@{TeamServer.Domain}", "", now.AddDays(-30));
        store.Issue($"new@{TeamServer.Domain}", "", now);

        var active = store.Active(now);

        active.Should().ContainSingle().Which.Email.Should().Be($"new@{TeamServer.Domain}");
        Directory.GetFiles(Path.Combine(data, "sessions"), "*.json").Should().HaveCount(2,
            "unlike Validate and Sweep, a listing deletes nothing");
    }

    [Fact]
    public void Active_SkipsATornFile_WithoutReportingIt_AndLeavesItInPlace()
    {
        // An admin's page asks every minute. A torn file reported on every listing would put a line
        // in the log sixty times an hour about something the sweep will remove anyway.
        var data = FreshDir();
        var reported = new List<string>();
        var store = new SessionStore(data, TimeSpan.FromDays(7), (message, _) => reported.Add(message));
        var now = DateTimeOffset.UtcNow;
        store.Issue($"dev@{TeamServer.Domain}", "", now);
        var torn = Path.Combine(data, "sessions", "torn.json");
        File.WriteAllText(torn, "{ this is not json");

        var active = store.Active(now);

        active.Should().ContainSingle();
        reported.Should().BeEmpty();
        File.Exists(torn).Should().BeTrue();
    }
}

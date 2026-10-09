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
        // Two casings from the identity provider, three sessions, and the newest of them carries no
        // name at all — which must not blank the person.
        store.Issue($"Alice@{TeamServer.Domain}", "Alice Old", now.AddHours(-3));
        store.Issue($"alice@{TeamServer.Domain}", "Alice New", now.AddHours(-2));
        store.Issue($"ALICE@{TeamServer.Domain}", "", now.AddHours(-1));

        var people = await ListAsync(server);

        var alice = people.EnumerateArray()
            .Should().ContainSingle(p => p.GetProperty("email").GetString()!.Equals($"alice@{TeamServer.Domain}", StringComparison.OrdinalIgnoreCase),
                "two casings of one email are one person, not two")
            .Subject;
        alice.GetProperty("displayName").GetString().Should().Be("Alice New", "the latest NON-EMPTY name wins");
        alice.GetProperty("lastUsedUtc").GetDateTimeOffset().Should().BeCloseTo(now.AddHours(-1), TimeSpan.FromSeconds(1),
            "last used is the latest over the person's unexpired sessions");
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

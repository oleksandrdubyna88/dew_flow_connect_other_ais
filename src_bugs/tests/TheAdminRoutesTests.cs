using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The five admin routes, over real HTTP, against the real serializer.
/// </summary>
/// <remarks>
/// <para>Same reason as <see cref="TheRouteTests"/>: the decisions are unit-tested, and what only an
/// HTTP test can see is the bearer header, the status codes, and above all the AOT JSON binding. Six
/// new shapes went into <c>BugsJson</c> for this story, and a serializer context that compiles
/// perfectly can still bind nothing at runtime — which is how a released `coai-server` once answered
/// 500 to everything.</para>
/// <para>Every test here reads its answers as a READER of the API does — through anonymous record
/// shapes declared at the bottom — rather than through the server's own wire types. A test that
/// deserialised with `AdminWire` would agree with the server about a field it had renamed.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheAdminRoutesTests
{
    /// <summary>The credential an administrator presents in these tests.</summary>
    private const string AdminKey = "an-administrators-key";

    /// <summary>A second one, so "an administrator" is never "the administrator".</summary>
    private const string OtherAdminKey = "another-administrators-key";

    private const string Configured = $"# alice\n{AdminKey}\n# bob\n{OtherAdminKey}";

    [Fact]
    public async Task IssuingAKeyReturnsItOnceAndStoresOnlyItsHash()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);

        var reply = await http.PostAsJsonAsync(
            "/admin/keys", new { note = "for the tuesday workshop" }, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Created);
        reply.Headers.Location!.ToString().Should().StartWith("/admin/keys/");
        var issued = await Read<Issued>(reply);
        issued.Key.Should().NotBeNullOrWhiteSpace("this is the ONE response that carries a key");
        issued.Note.Should().Be("for the tuesday workshop");
        issued.CreatedUtc.Should().Be("2026-09-17T10:00:00.0000000Z", "stamped from the injected clock");

        // The key really is a key: it authenticates an ingest. That is the whole point of the
        // response, and a test that only checked the string was non-empty would pass for a stub.
        using var ingesting = server.Bearing(issued.Key);
        var used = await ingesting.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);
        used.StatusCode.Should().Be(HttpStatusCode.OK);

        using var corpus = server.Reading();
        corpus.KeyFor(issued.Key, BugsServer.Secret).Should().Be(
            issued.Id, "the stored hash must be of the key that was handed out, not of something else");
    }

    /// <summary>Nothing can be asked for the key afterwards; the recovery is the listing.</summary>
    /// <remarks>
    /// The plan rejected an idempotency token for the lost-issuance case, and the reason is asserted
    /// here rather than trusted: the listing is newest-first with an exact `createdUtc`, so an
    /// administrator whose response was lost in flight sees a key created moments ago that they do
    /// not hold, and revokes it. That recovery only works because of the ORDER, so the order is the
    /// test.
    /// </remarks>
    [Fact]
    public async Task ALostIssuanceIsRecoverableFromTheNewestFirstListing()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);

        await Issue(http, "the first one");
        server.Clock.Set(new DateTimeOffset(2026, 9, 17, 10, 5, 0, TimeSpan.Zero));
        var lost = await Issue(http, "the one whose response never arrived");

        var page = await Get<KeysPage>(http, "/admin/keys");

        page.Items[0].Id.Should().Be(lost.Id, "the newest key is the first thing an administrator sees");
        page.Items[0].Note.Should().Be("the one whose response never arrived");
        page.Items[0].CreatedUtc.Should().Be("2026-09-17T10:05:00.0000000Z");
        page.Items.Should().HaveCount(2);
    }

    /// <summary>A key listing carries what the tab shows, and no credential of any kind.</summary>
    [Fact]
    public async Task AListedKeyCarriesItsCountsAndNeitherAKeyNorAHash()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);
        var issued = await Issue(http, "a contributor");

        using (var ingesting = server.Bearing(issued.Key))
        {
            var sent = await ingesting.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);
            sent.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var body = await Body(http, "/admin/keys");
        var page = await Get<KeysPage>(http, "/admin/keys");
        var listed = page.Items.Should().ContainSingle().Subject;

        listed.Id.Should().Be(issued.Id);
        listed.Sent.Should().Be(1, "one accepted ingest");
        listed.Waiting.Should().Be(1, "its pair is in quarantine");
        listed.LastSeenMonth.Should().Be("2026-09", "a month, never a date and never a clock time");
        listed.RevokedUtc.Should().BeNull("it is in force");
        page.Total.Should().Be(1);
        page.Limit.Should().Be(50);
        page.NextBefore.Should().BeNull("one row is not a full page, so there is no next one");

        body.Should().NotContain(issued.Key, "no listing ever carries a key value");
        body.Should().NotContain(
            Corpus.HashOf(issued.Key, BugsServer.Secret), "nor a hash of one");
    }

    /// <summary>Revoking is idempotent, and the second answer reports the ORIGINAL time.</summary>
    [Fact]
    public async Task RevokingTwiceIsTwoHundredAndTheSecondChangedNothing()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);
        var issued = await Issue(http, "a contributor");

        var first = await Post<Revocation>(http, $"/admin/keys/{issued.Id}/revoke");
        server.Clock.Set(new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero));
        var again = await Post<Revocation>(http, $"/admin/keys/{issued.Id}/revoke");

        first.Changed.Should().BeTrue();
        first.RevokedUtc.Should().Be("2026-09-17T10:00:00.0000000Z");
        again.Changed.Should().BeFalse("an administrator pressing revoke twice has not made a mistake");
        again.RevokedUtc.Should().Be(
            first.RevokedUtc, "the fact asked for is when it was stopped, not when it was asked again");

        using var corpus = server.Reading();
        corpus.AuditTrail(10).Where(row => row.Action == AuditAction.Revoke)
            .Should().ContainSingle("a revoke that changed nothing has no mutation to audit");
    }

    /// <summary>A key that never existed is a 404, which is not the same as one already revoked.</summary>
    [Fact]
    public async Task RevokingAKeyThatNeverExistedIsNotFound()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);

        var reply = await http.PostAsync(
            "/admin/keys/0123456789abcdef/revoke", content: null, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var corpus = server.Reading();
        corpus.AuditCount().Should().Be(0, "nothing happened, so nothing is in the trail");
    }

    /// <summary>The audit names the administrator who acted, by their derived id.</summary>
    [Fact]
    public async Task TheAuditNamesWhichAdministratorActed()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var alice = server.Bearing(AdminKey);
        using var bob = server.Bearing(OtherAdminKey);
        var issued = await Issue(alice, "a contributor");
        (await Post<Revocation>(bob, $"/admin/keys/{issued.Id}/revoke")).Changed.Should().BeTrue();

        var trail = await Get<AuditPage>(alice, "/admin/audit");

        trail.Items.Should().HaveCount(2);
        trail.Items[0].Action.Should().Be("revoke", "newest first");
        trail.Items[0].AdminId.Should().Be(AdminId.Of(Corpus.HashOf(OtherAdminKey, BugsServer.Secret)).Value);
        trail.Items[0].Target.Should().Be(issued.Id);
        trail.Items[0].AtUtc.Should().Be(
            "2026-09-17T10:00:00.0000000Z", "an exact time: this is a log about administrators");
        trail.Items[1].Action.Should().Be("issue");
        trail.Items[1].AdminId.Should().Be(AdminId.Of(Corpus.HashOf(AdminKey, BugsServer.Secret)).Value);
        trail.Items[1].AdminId.Should().NotBe(
            trail.Items[0].AdminId, "two administrators must not be one row in the trail");
    }

    /// <summary>A note that looks like an email address is refused, and stores nothing.</summary>
    [Fact]
    public async Task ANoteShapedLikeAnEmailAddressIsRefused()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);

        var reply = await http.PostAsJsonAsync(
            "/admin/keys", new { note = "bob@example.com" }, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("email");
        using var corpus = server.Reading();
        corpus.KeysTotal().Should().Be(0, "a refused issuance issues nothing");
    }

    /// <summary>A person's name is NOT refused, which is what makes the guard's scope honest.</summary>
    /// <remarks>
    /// The guard catches one shape and no other, and the code says so. This test is the other half of
    /// that statement: without it, "notes are validated" could mean anything, and a later tightening
    /// that refused `for bob's workshop` would look like a pass.
    /// </remarks>
    [Fact]
    public async Task ANoteThatIsMerelyANameIsAccepted()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);

        (await Issue(http, "Bob Smith's tuesday workshop")).Note.Should().Be("Bob Smith's tuesday workshop");
    }

    /// <summary>A note past the cap is refused, and the refusal says what was legal.</summary>
    [Fact]
    public async Task ANotePastTheCapIsRefusedWithTheNumber()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);

        var reply = await http.PostAsJsonAsync(
            "/admin/keys",
            new { note = new string('x', AdminApi.MostNote + 1) },
            TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain(AdminApi.MostNote.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A request with no body at all issues a key with an empty note, rather than failing.</summary>
    /// <remarks>
    /// `note` is OUR record and a key with none is legitimate; a 400 here would make the simplest
    /// possible call — issue me a key — the one that does not work.
    /// </remarks>
    [Fact]
    public async Task IssuingWithNoBodyIsAllowedAndTheNoteIsEmpty()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);

        var reply = await http.PostAsync("/admin/keys", content: null, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Read<Issued>(reply)).Note.Should().BeEmpty();
    }

    /// <summary>`/admin/active` answers the whole document when no contributor has sent anything.</summary>
    /// <remarks>
    /// <para>The shape never varying is what makes it renderable: a client that has to tell
    /// `{items:[]}` from a missing field is a client with two code paths for "quiet".</para>
    /// <para><b>"Nobody is sending" cannot be observed through this route</b>, and that is worth
    /// writing down rather than asserting falsely: reading it IS a request, admitted by the admin
    /// limiter a moment earlier, so the reader is always in its own answer. The first version of
    /// this test asserted an empty list and went red for exactly that reason. The genuinely empty
    /// list belongs one layer down, where a limiter with no traffic can exist — it is asserted in
    /// <see cref="TheRateLimiterTests"/>.</para>
    /// </remarks>
    [Fact]
    public async Task ActiveCarriesBothFieldsAndNoContributorWhenNoneHasSent()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);

        var body = await Body(http, "/admin/active");
        var active = await Get<ActiveNow>(http, "/admin/active");

        body.Should().Contain("\"items\"").And.Contain("\"windowSeconds\"", "both fields, always");
        active.Items.Should().OnlyContain(
            row => row.Id.StartsWith("admin-", StringComparison.Ordinal),
            "no contributor has sent anything, so only the administrator reading this is here");
        active.WindowSeconds.Should().Be(60, "the window is a fact about the limiter, not about a caller");
    }

    /// <summary>It shows contributors sending right now — which is what the endpoint is for.</summary>
    /// <remarks>
    /// The two limits are separate settings and therefore separate limiter instances, so this route
    /// reads BOTH: handed only the admin limiter it would answer with administrators and no
    /// contributors, which is the inverse of the question. The administrator's own row appears too,
    /// under its `admin-` prefix, because the reading request is itself traffic.
    /// </remarks>
    [Fact]
    public async Task ActiveShowsTheContributorSendingRightNow()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);
        var issued = await Issue(http, "a contributor");
        using (var ingesting = server.Bearing(issued.Key))
        {
            await ingesting.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);
            await ingesting.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);
        }

        var active = await Get<ActiveNow>(http, "/admin/active");

        var contributor = active.Items.Should().ContainSingle(row => row.Id == $"key:{issued.Id}").Subject;
        contributor.InWindow.Should().Be(2);
        contributor.Limited.Should().BeFalse("two requests is not the default limit");
        active.Items.Should().Contain(
            row => row.Id.StartsWith("admin-", StringComparison.Ordinal),
            "the administrator reading this is sending too, and its prefix says which kind it is");
        active.Items[0].InWindow.Should().BeGreaterThanOrEqualTo(
            active.Items[^1].InWindow, "busiest first, so the flood is the first row");
    }

    /// <summary>It persists nothing: reading it writes no row anywhere.</summary>
    [Fact]
    public async Task ActivePersistsNothing()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);
        var issued = await Issue(http, "a contributor");

        // Every table's row count, before and after, read from the schema rather than named: a
        // sixth table that a live view started writing to would otherwise go unnoticed. The db FILE
        // cannot be compared byte for byte — the server holds it open, and on Windows that is an
        // IOException rather than a reading — so the rows are what is compared.
        var db = Path.Combine(server.DataDir, "coai-bugs.db");
        var before = Rows(db);
        for (var at = 0; at < 5; at++)
        {
            (await http.GetAsync("/admin/active", TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Rows(db).Should().BeEquivalentTo(before, "a live view writes nothing, to any table");
        using var corpus = server.Reading();
        corpus.AuditCount().Should().Be(1, "only the issuance; a live view is not an administrative action");
        corpus.KeysPage(10).Should().ContainSingle().Which.Id.Value.Should().Be(issued.Id);
    }

    /// <summary>How many rows every table holds, keyed by table name.</summary>
    private static IReadOnlyDictionary<string, string> Rows(string db) =>
        TestSql.Column(db, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name")
            .ToDictionary(table => table, table => TestSql.Scalar(db, $"SELECT COUNT(*) FROM {table}"));

    /// <summary>No admin response but the one issuance carries a key or a hash — asserted over all five.</summary>
    /// <remarks>
    /// The rule is stated once in `AdminWire` and enforced by the shapes having no field for a
    /// credential. This asserts it from OUTSIDE, on the bytes each route actually answers, because
    /// the rule is about what reaches a reader and not about a type declaration.
    /// </remarks>
    [Fact]
    public async Task OnlyTheIssuanceResponseEverCarriesACredential()
    {
        using var server = new BugsServer(adminKeys: Configured);
        using var http = server.Bearing(AdminKey);
        var issued = await Issue(http, "a contributor");
        using (var ingesting = server.Bearing(issued.Key))
        {
            await ingesting.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);
        }

        var hash = Corpus.HashOf(issued.Key, BugsServer.Secret);
        var adminHash = Corpus.HashOf(AdminKey, BugsServer.Secret);
        foreach (var route in new[] { "/admin/keys", "/admin/audit", "/admin/active" })
        {
            var body = await Body(http, route);
            body.Should().NotBeEmpty($"{route} must have answered something for this to prove anything");
            body.Should().NotContain(issued.Key, $"{route} must not carry a contributor's key");
            body.Should().NotContain(hash, $"{route} must not carry its hash either");
            body.Should().NotContain(AdminKey, $"{route} must not carry an administrator's key");
            body.Should().NotContain(adminHash, $"{route} must not carry an administrator's hash");
        }

        var revocation = await Body(http, $"/admin/keys/{issued.Id}/revoke", post: true);
        revocation.Should().NotContain(issued.Key).And.NotContain(hash);
    }

    private static readonly object OnePair = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_1(var_1) { }",
                skeletonAfter = "method_1(var_1) { lock (var_2) { } }",
            },
        },
    };

    /// <summary>Issues a key and insists it worked, so a test's setup cannot fail silently.</summary>
    internal static async Task<Issued> Issue(HttpClient http, string note)
    {
        var reply = await http.PostAsJsonAsync(
            "/admin/keys", new { note }, TestContext.Current.CancellationToken);
        reply.StatusCode.Should().Be(HttpStatusCode.Created, "the issuance is this test's setup");

        return await Read<Issued>(reply);
    }

    internal static async Task<T> Get<T>(HttpClient http, string route)
    {
        var reply = await http.GetAsync(route, TestContext.Current.CancellationToken);
        reply.StatusCode.Should().Be(HttpStatusCode.OK, $"{route} must answer");

        return await Read<T>(reply);
    }

    private static async Task<T> Post<T>(HttpClient http, string route)
    {
        var reply = await http.PostAsync(route, content: null, TestContext.Current.CancellationToken);
        reply.StatusCode.Should().Be(HttpStatusCode.OK, $"{route} must answer");

        return await Read<T>(reply);
    }

    private static async Task<string> Body(HttpClient http, string route, bool post = false)
    {
        var reply = post
            ? await http.PostAsync(route, content: null, TestContext.Current.CancellationToken)
            : await http.GetAsync(route, TestContext.Current.CancellationToken);

        return await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<T> Read<T>(HttpResponseMessage reply) =>
        (await reply.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;

    /// <summary>The shapes as a READER of this API sees them, never the server's own wire types.</summary>
    internal sealed record Issued(string Id, string Key, string Note, string CreatedUtc);

    internal sealed record KeyListed(
        string Id, string Note, string CreatedUtc, string? RevokedUtc, string? LastSeenMonth, int Sent, int Waiting);

    internal sealed record KeysPage(IReadOnlyList<KeyListed> Items, int Limit, int Total, long? NextBefore);

    internal sealed record Revocation(string Id, string RevokedUtc, bool Changed);

    internal sealed record AuditListed(long Id, string AdminId, string Action, string Target, string AtUtc);

    internal sealed record AuditPage(IReadOnlyList<AuditListed> Items, int Limit, long? NextBefore);

    internal sealed record ActiveCaller(string Id, int InWindow, bool Limited);

    internal sealed record ActiveNow(IReadOnlyList<ActiveCaller> Items, int WindowSeconds);
}

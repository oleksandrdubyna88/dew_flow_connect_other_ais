using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// An administrator may upload, down a path of its own that counts nothing.
/// </summary>
/// <remarks>
/// <para><b>Why a separate path and not <see cref="Corpus.Accept"/> with a different id.</b>
/// `Accept` re-checks that the key is in force INSIDE its transaction, against `api_keys` — and an
/// administrator is deliberately not a row there. So "an admin key may also upload", as the plan
/// first wrote it, was either a rejection, a silent weakening of that guard, or an uncounted write
/// through a guard that happened not to fire. All three reviewers refused to let it stand.</para>
/// <para><b>The re-check is not bypassed; it does not apply.</b> It guards a race a contributor key
/// really has — a `--revoke` one-shot committing between the gate and the write, in another process,
/// against the same file. An administrator's credentials are read once at startup and are immutable
/// for the process's lifetime, because rotation means editing the secret and redeploying, which
/// RESTARTS this server. There is no window for the set to change in.
/// <see cref="AccceptRefusesWhatAcceptAdminStores"/> is that difference as a test.</para>
/// <para><b>An administrator's uploads are invisible to the Users tab</b>, which is a real cost and
/// is asserted here rather than left to be discovered: there is no key row to count against, so
/// story 3's tab must say so out loud.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheAdminUploadTests
{
    private const string AdminKey = "an-administrators-key";

    private static string AdminIdOf(string key) => AdminId.Of(Corpus.HashOf(key, BugsServer.Secret)).Value;

    /// <summary>The pair is stored, attributed to the administrator, and counted against nobody.</summary>
    [Fact]
    public async Task AnAdministratorsPairIsStoredAndAttributedAndCountedAgainstNoKey()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);

        var reply = await http.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "an administrator's own pair is worth having");
        var answer = await reply.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken);
        answer!.Items.Should().ContainSingle().Which.Took.Should().Be("accepted");

        var db = Path.Combine(server.DataDir, "coai-bugs.db");
        TestSql.Column(db, "SELECT key_id FROM quarantine").Should().Equal(
            [AdminIdOf(AdminKey)],
            "the column records WHICH CREDENTIAL sent the pair, and for an administrator that is "
            + "the derived id — so one administrator's mistake can be undone in bulk like anybody's");
        TestSql.Scalar(db, "SELECT COUNT(*) FROM api_keys").Should().Be(
            "0", "an administrator is not a key row and uploading does not make one");
    }

    /// <summary>An administrator may comment too, on the same route, and still counts against nobody.</summary>
    /// <remarks>
    /// The commented route shares one handler with the plain one and differs in a single flag, so
    /// the administrator's path through it is a branch nothing else here walks: it reaches
    /// <see cref="Corpus.AcceptAdmin"/> rather than <see cref="Corpus.Accept"/>, and a comment lost
    /// on that one path would be lost exactly where nobody is watching for it.
    /// </remarks>
    [Fact]
    public async Task AnAdministratorsCommentIsStoredAndStillCountsAgainstNoKey()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);

        var reply = await http.PostAsJsonAsync(
            "/ingest/commented", OneCommentedPair, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.OK);
        var db = Path.Combine(server.DataDir, "coai-bugs.db");
        TestSql.Scalar(db, "SELECT comment FROM quarantine").Should().Be("we hit this in the field");
        TestSql.Scalar(db, "SELECT COUNT(*) FROM api_keys").Should().Be(
            "0", "commenting is not a reason to invent a key row either");
    }

    /// <summary>The same pair as <see cref="OnePair"/>, with words an administrator typed.</summary>
    private static readonly object OneCommentedPair = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_1(var_1) { }",
                skeletonAfter = "method_1(var_1) { lock (var_2) { } }",
                comment = "we hit this in the field",
            },
        },
    };

    /// <summary>No counter and no month are written, because there is no row to write them on.</summary>
    [Fact]
    public async Task NoCounterAndNoMonthAreWrittenForAnAdministrator()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var admin = server.Bearing(AdminKey);
        var issued = await TheAdminRoutesTests.Issue(admin, "a contributor");

        for (var at = 0; at < 3; at++)
        {
            (await admin.PostAsJsonAsync("/ingest", Pair(at), TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var db = Path.Combine(server.DataDir, "coai-bugs.db");
        TestSql.Column(db, "SELECT submissions FROM api_keys").Should().Equal(
            ["0"], "the only key row is the contributor's, and it sent nothing");
        TestSql.Column(db, "SELECT last_seen_month FROM api_keys").Should().Equal(
            [string.Empty], "nor was it stamped with a month by somebody else's upload");
        TestSql.Scalar(db, "SELECT COUNT(*) FROM quarantine").Should().Be("3", "the pairs are all there");

        var listed = await TheAdminRoutesTests.Get<TheAdminRoutesTests.KeysPage>(admin, "/admin/keys");
        listed.Items.Should().ContainSingle().Which.Id.Should().Be(issued.Id);
        listed.Items[0].Sent.Should().Be(0);
        listed.Items[0].Waiting.Should().Be(
            0, "an administrator's three pairs belong to no key row, so the tab shows none of them");
    }

    /// <summary>The month rule holds for an administrator's pair too: a month, and no clock time.</summary>
    /// <remarks>
    /// The rule is about the column BESIDE a key id, not about whose key it is — so this is not a
    /// place the promise relaxes. A pair uploaded by an administrator still cannot be used to ask
    /// which hour anybody worked.
    /// </remarks>
    [Fact]
    public async Task AnAdministratorsPairCarriesAMonthAndNoClockTime()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        server.Clock.Set(new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero));
        using var http = server.Bearing(AdminKey);

        (await http.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var db = Path.Combine(server.DataDir, "coai-bugs.db");
        TestSql.Column(db, "SELECT received_month FROM quarantine").Should().Equal(["2026-11"]);
        TestSql.Column(db, "SELECT received_utc FROM quarantine").Should().Equal(
            [string.Empty], "frozen in step 1 and written empty for ever, whoever sent the pair");
    }

    /// <summary>An administrator's upload counts against the ADMIN limit, not the contributor one.</summary>
    /// <remarks>
    /// The credential decides the bucket and therefore the setting. With the contributor limit at
    /// one, an administrator uploading twice must still be admitted — otherwise an operator seeding
    /// the corpus would be throttled by the number that exists to slow strangers down.
    /// </remarks>
    [Fact]
    public async Task AnAdministratorsUploadIsLimitedByTheAdminSetting()
    {
        using var server = new BugsServer(ratePerMinute: 1, adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);

        for (var at = 0; at < 4; at++)
        {
            (await http.PostAsJsonAsync("/ingest", Pair(at), TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK, $"upload {at} is inside the admin limit of 120");
        }

        var active = await TheAdminRoutesTests.Get<TheAdminRoutesTests.ActiveNow>(http, "/admin/active");
        active.Items.Should().ContainSingle(row => row.Id == AdminIdOf(AdminKey))
            .Which.InWindow.Should().BeGreaterThanOrEqualTo(4, "the uploads are in the administrator's window");
    }

    /// <summary>And it is refused when the admin limit itself is reached, naming that limit.</summary>
    [Fact]
    public async Task AnAdministratorsUploadIsRefusedAtTheAdminLimit()
    {
        using var server = new BugsServer(adminKeys: AdminKey, adminRatePerMinute: 1);
        using var http = server.Bearing(AdminKey);

        (await http.PostAsJsonAsync("/ingest", Pair(0), TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var refused = await http.PostAsJsonAsync("/ingest", Pair(1), TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("per administrator", "the limit named must be the one that was reached");
    }

    /// <summary>A key that is nobody's is still nobody's, whichever store is asked.</summary>
    [Fact]
    public async Task AKeyThatIsNeitherAContributorNorAnAdministratorIsRefused()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing("neither-of-those-things");

        var reply = await http.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        TestSql.Scalar(Path.Combine(server.DataDir, "coai-bugs.db"), "SELECT COUNT(*) FROM quarantine")
            .Should().Be("0");
    }

    /// <summary>
    /// The difference the two paths exist for: <see cref="Corpus.Accept"/> refuses the very id
    /// <see cref="Corpus.AcceptAdmin"/> stores under.
    /// </summary>
    /// <remarks>
    /// This is the test that makes the design argument checkable instead of merely written down. An
    /// administrator has no `api_keys` row, so the in-force re-check — correct and load-bearing for a
    /// contributor — would refuse every administrative upload if the id were pushed through it.
    /// </remarks>
    [Fact]
    public void AccceptRefusesWhatAcceptAdminStores()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coai-bugs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var corpus = Corpus.Open(Path.Combine(dir, "coai-bugs.db"));
            var admin = AdminId.Of(Corpus.HashOf(AdminKey, "a-secret"));
            var month = UtcMonth.Of(new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero));

            var throughAccept = corpus.Accept(
                new KeyId(admin.Value), month, scope => scope.Keep("CSharp", "a", "b"));
            var throughAcceptAdmin = corpus.AcceptAdmin(
                admin, month, scope => scope.Keep("CSharp", "a", "b"));

            throughAccept.Should().BeOfType<Accepted<(Kept, string, bool)>.KeyNotInForce>(
                "an administrator is not a key row, and Accept is right to refuse one");
            throughAcceptAdmin.Kept.Should().Be(Kept.Stored, "which is why the administrator has its own path");
            corpus.Waiting(10).Should().ContainSingle("exactly one of the two wrote anything");
        }
        finally
        {
            Scratch.Delete(dir);
        }
    }

    /// <summary>The lease expires for an administrator's batch too.</summary>
    /// <remarks>
    /// A scope kept past its batch would write with no transaction around it. `AcceptAdmin` spends
    /// it in a `finally`, so the guarantee does not depend on the callback returning normally.
    /// </remarks>
    [Fact]
    public void AnAdministratorsScopeCannotOutliveItsBatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coai-bugs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var corpus = Corpus.Open(Path.Combine(dir, "coai-bugs.db"));
            var admin = AdminId.Of(Corpus.HashOf(AdminKey, "a-secret"));
            var month = UtcMonth.Of(new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero));

            var escaped = corpus.AcceptAdmin(admin, month, scope => scope);

            var late = () => escaped.Keep("CSharp", "late", "write");
            late.Should().Throw<InvalidOperationException>()
                .WithMessage("*already committed*", "a captured scope is a capability, and it is revoked");
            corpus.Waiting(10).Should().BeEmpty();
        }
        finally
        {
            Scratch.Delete(dir);
        }
    }

    private static object Pair(int at) => new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = $"method_{at}(var_1) {{ }}",
                skeletonAfter = $"method_{at}(var_1) {{ lock (var_2) {{ }} }}",
            },
        },
    };

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

    private sealed record Answer(IReadOnlyList<Item> Items);

    private sealed record Item(string EntryId, string Took, string Why);
}

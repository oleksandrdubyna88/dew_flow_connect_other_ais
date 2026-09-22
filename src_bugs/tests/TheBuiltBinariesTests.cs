using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Two real processes, a real socket: the shipped `coai-bugs` and the shipped `coai-mcp`.
/// </summary>
/// <remarks>
/// <para><b>What an in-process test cannot see.</b> `BothHalvesTests` hosts the server's ASSEMBLIES
/// through `WebApplicationFactory` and talks to it through a handler, which is the right shape for
/// checking that the two halves agree about ids and words. A code round pointed out what it still
/// cannot reach: a trimming, source-generation, embedded-resource or publish-layout defect leaves
/// the released binaries unable to ingest a single pair while every one of those tests stays
/// green — and this story's whole reason for existing is a keyword file that was read from a
/// directory no release has.</para>
/// <para><b>The seam is `COAI_CONTRACT_EXE`</b>, which `McpContractTests` and `LogCliScenarioTests`
/// already use and the release workflow already sets: unset, this runs the binaries this build
/// produced; set, it runs the PUBLISHED, AOT-compiled ones. So the same scenario is a fast check
/// here and the release smoke there, rather than two tests that drift.</para>
/// <para>It starts a server on a real port, mints a key through the real `--issue-key` one-shot,
/// uploads through the real `--upload-pairs` one-shot, and reads the result back through the real
/// `--waiting` one-shot. Nothing in the path is a fixture.</para>
/// </remarks>
[Collection("the-server")]
public sealed partial class TheBuiltBinariesTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-built-" + Guid.NewGuid().ToString("N")[..8]);

    private const string Secret = "a-secret-for-this-scenario-only";

    public TheBuiltBinariesTests() => Directory.CreateDirectory(_dir);

    /// <summary>The server binary: the published one when the release smoke points at it.</summary>
    private static string BugsExe =>
        Environment.GetEnvironmentVariable("COAI_BUGS_CONTRACT_EXE") is { Length: > 0 } published
            ? published
            : Built("src_bugs", "src", "coai-bugs");

    /// <summary>The client binary, on the same seam the other contract suites use.</summary>
    private static string McpExe =>
        Environment.GetEnvironmentVariable("COAI_CONTRACT_EXE") is { Length: > 0 } published
            ? published
            : Built("src_mcp", "src", "coai-mcp");

    /// <summary>
    /// A pair goes from a real client process to a real server process and is really stored.
    /// </summary>
    /// <remarks>
    /// The embedded keyword list is what this proves hardest: the server reads it during startup, so
    /// a binary that cannot find it never answers `/health` and this test times out rather than
    /// passing quietly.
    /// </remarks>
    [Fact]
    public async Task ARealUploadCrossesTwoRealProcesses()
    {
        MustExist(BugsExe);
        MustExist(McpExe);

        var key = IssueKey();
        key.Should().NotBeEmpty("--issue-key prints the key once, on stdout");

        var (server, port) = await Serving(string.Empty);
        try
        {
            SeedOneKeptPair();
            var upload = Run(
                McpExe,
                $"--upload-pairs --server http://127.0.0.1:{port}",
                Client(),
                key);

            upload.Code.Should().Be(0, $"the client said: {upload.Err}");
            upload.Out.Should().Contain("\"accepted\": 1", "the summary is this mode's whole answer")
                .And.Contain("\"refused\": 0")
                .And.Contain("\"trouble\": \"\"");
        }
        finally
        {
            Stop(server);
        }

        var waiting = Run(BugsExe, "--waiting", _dir);
        waiting.Code.Should().Be(0);
        waiting.Err.Should().Contain("1 waiting", "the pair really landed in the real database");
        waiting.Out.Should().Contain("method_1", "and `--waiting` shows a person the skeleton");
    }

    /// <summary>
    /// The PUBLISHED binary takes a comment on its own route, stores it, and shows it to a person.
    /// </summary>
    /// <remarks>
    /// <para><b>Three things only the real binary can answer.</b> The route exists in the published
    /// surface at all; the source-generated serializer really binds the fourth property, which is
    /// the one failure mode that compiles perfectly and drops the field at runtime; and the
    /// migration ran on the real file so the column is there to write into.</para>
    /// <para>And the other half of the mechanism, on the same binary: the old route accepts the same
    /// document and stores no comment. A person reading `--waiting` sees the difference, which is
    /// where they would see it.</para>
    /// <para>The client half cannot carry a comment yet — that is story 4.2a — so this posts the
    /// document itself rather than running `coai-mcp`. The end-to-end pass through both real
    /// processes arrives with that story.</para>
    /// </remarks>
    [Fact]
    public async Task TheRealServerTakesACommentOnItsOwnRouteAndShowsIt()
    {
        MustExist(BugsExe);
        var key = IssueKey();
        var (server, port) = await Serving(string.Empty);
        try
        {
            using var http = Talking(port, key);

            using var said = await http.PostAsJsonAsync(
                "/ingest/commented", OneCommentedPair, TestContext.Current.CancellationToken);
            said.StatusCode.Should().Be(HttpStatusCode.OK);
            (await said.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().Contain("\"contract\":2", "the published answer states which contract took it");
        }
        finally
        {
            Stop(server);
        }

        var waiting = Run(BugsExe, "--waiting", _dir);
        waiting.Code.Should().Be(0);
        waiting.Out.Should().Contain(
            "said: this one bit us in production",
            "a person deciding what to promote is shown the words that came with the pair");
    }

    /// <summary>And the SAME published binary refuses that document on the old route.</summary>
    /// <remarks>
    /// The route is the mechanism, so it is worth one scenario on the artefact that is actually
    /// deployed: two routes that behaved alike would leave a comment's fate to deployment order.
    /// Refused rather than stored-without-it, because a new server dropping a field in silence is
    /// the very failure the new route exists to prevent.
    /// </remarks>
    [Fact]
    public async Task TheRealServersPlainRouteRefusesAPairCarryingAComment()
    {
        MustExist(BugsExe);
        var key = IssueKey();
        var (server, port) = await Serving(string.Empty);
        try
        {
            using var http = Talking(port, key);

            using var plain = await http.PostAsJsonAsync(
                "/ingest", OneCommentedPair, TestContext.Current.CancellationToken);

            plain.StatusCode.Should().Be(HttpStatusCode.OK, "a refusal is an item's fate");
            (await plain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().Contain("refused").And.Contain("/ingest/commented");
        }
        finally
        {
            Stop(server);
        }

        Run(BugsExe, "--waiting", _dir).Err.Should().Contain(
            "nothing is waiting", "a refused pair is written to no table at all");
    }

    /// <summary>
    /// A candidate port taken between the probe and the bind costs the candidate, not the run.
    /// </summary>
    /// <remarks>
    /// <para><b>The race, made deterministic.</b> <c>FreePort</c> asks the OS for a port, releases
    /// it, and hands the number to a process that binds it a moment later; anything on the machine
    /// may take it in between. Waiting for that to happen by luck is how a flake is "investigated",
    /// so the port is held by an ordinary socket and offered as the FIRST candidate — the same
    /// shape `AStubSurvivesALostPortTests` uses for the in-process listener, and the same reason.</para>
    /// <para>It is held by a plain <see cref="TcpListener"/> rather than by a second copy of the
    /// server, because that is what the real collision looks like: whatever takes the port on a
    /// loaded runner is somebody else's socket, not another `coai-bugs`.</para>
    /// </remarks>
    [Fact]
    public async Task APortTakenBeforeTheBind_CostsTheCandidateAndNotTheRun()
    {
        MustExist(BugsExe);
        var thief = new TcpListener(IPAddress.Loopback, 0);
        thief.Start();
        var taken = ((IPEndPoint)thief.LocalEndpoint).Port;

        try
        {
            var (server, port) = await Serving(FreePorts().Prepend(taken), string.Empty);

            try
            {
                port.Should().NotBe(
                    taken, "the port was held, so the server must have taken another one");
                server.Process.HasExited.Should().BeFalse("and it must really be serving on it");

                using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
                (await http.GetAsync("/health", TestContext.Current.CancellationToken))
                    .StatusCode.Should().Be(
                        HttpStatusCode.OK,
                        "answering on the port it actually got is the whole point — a scenario "
                        + "pointed at a port somebody else holds talks to a stranger");
            }
            finally
            {
                Stop(server);
            }
        }
        finally
        {
            thief.Stop();
        }
    }

    /// <summary>
    /// What THIS platform prints when the port is taken is something the retry recognises.
    /// </summary>
    /// <remarks>
    /// The classifier is a list of sentences, and a list of sentences is a guess until somebody
    /// provokes the real thing. So this takes a port, starts the real server on it, and asserts
    /// that what the server actually said is recognised — a platform whose wording is missing
    /// reddens here and names the text, which is how the list is meant to grow. Without it the
    /// retry above could pass while recognising nothing, because its stand-in port is released
    /// before the second attempt either way.
    /// </remarks>
    [Fact]
    public async Task ARealCollisionIsRecognisedOnThisPlatform()
    {
        MustExist(BugsExe);
        var thief = new TcpListener(IPAddress.Loopback, 0);
        thief.Start();
        var taken = ((IPEndPoint)thief.LocalEndpoint).Port;

        try
        {
            var server = Start(BugsExe, $"--urls http://127.0.0.1:{taken}", _dir);

            try
            {
                (await Listening(server, taken)).Should().BeFalse(
                    "the port is held, so it cannot have started — and it must not take fifteen "
                    + "seconds to say so, which is what watching for the exit buys");
                server.Process.HasExited.Should().BeTrue();

                var said = server.Text;
                said.Should().NotBeEmpty(
                    "a server that could not bind says why, and this suite now keeps it");
                LostToARace(said).Should().BeTrue(
                    $"this platform's wording for a taken port must be one the retry recognises, "
                    + $"or every collision here becomes an unexplained failure. It said: {said}");
            }
            finally
            {
                Stop(server);
            }
        }
        finally
        {
            thief.Stop();
        }
    }

    /// <summary>An unknown mode exits 64, which is how a caller detects an old binary.</summary>
    /// <remarks>
    /// The other half of the rule — a mode this binary KNOWS never exits 64 — is `--revoke` with no
    /// `--id`, which the round found answering 64 and which answers 65 now. Both directions in one
    /// test, over the real executable, because an exit code is not observable any other way.
    /// </remarks>
    [Fact]
    public void TheExitCodesTellAnOldBinaryFromABadArgument()
    {
        MustExist(BugsExe);

        Run(BugsExe, "--rotate-the-moon", _dir).Code.Should().Be(
            64, "a mode this binary has never heard of is how a caller detects an old one");

        Run(BugsExe, "--revoke", _dir).Code.Should().Be(
            65, "it KNOWS --revoke; the argument is what is wrong, and 64 would say otherwise");
    }

    /// <summary>
    /// The shipped server migrates the file the field has, stamps the month, refuses a flood with a
    /// number, and refuses a revoked key — over a real socket, with the real one-shots running beside it.
    /// </summary>
    /// <remarks>
    /// <para><b>Unit tests can all pass while the deployed `/ingest` never invokes the limiter</b>,
    /// because the middleware is wired wrong; and an in-process host cannot show a second PROCESS
    /// opening the database while the server holds it. So: the database is written first exactly as
    /// `bugs-v0.1.0` left it on the host (<c>user_version = 0</c>), the real binary migrates it on
    /// start, `--issue-key` and `--revoke` run as real concurrent openers, and every status this
    /// story added is read off a real socket. The one-server-per-directory refusal and the
    /// out-of-range rate refusal are exit codes, which only a process can show.</para>
    /// <para>The month is compared with the wall clock read on either side of the ingest, so a run
    /// that straddles a UTC month boundary is not a failure of the server.</para>
    /// </remarks>
    [Fact]
    public async Task TheRealServerMigratesLimitsAndStopsARevokedKey()
    {
        MustExist(BugsExe);
        var database = Path.Combine(_dir, "coai-bugs.db");
        TheMigrationTests.WriteAFileFromStepOneOnly(database);

        var (server, port) = await Serving(string.Empty, rate: "2");
        try
        {
            TestSql.Scalar(database, "PRAGMA user_version").Should().Be(
                CorpusSchema.Steps.Length.ToString(CultureInfo.InvariantCulture),
                "the REAL binary ran the migration on the shape the field has");

            var (key, id) = IssueKeyAndId();
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            var before = UtcMonth.Now(TimeProvider.System).Value;
            (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK);
            var after = UtcMonth.Now(TimeProvider.System).Value;
            var usage = UsageOf(database, id);
            usage.Month().Should().MatchRegex(UtcMonth.Shape().ToString()).And.BeOneOf(before, after);
            usage.Count().Should().Be(1);

            (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK);
            using var third = await Ingest(http);
            third.StatusCode.Should().Be(
                HttpStatusCode.TooManyRequests, "the third request inside a minute, at a limit of two");
            third.Headers.RetryAfter.Should().NotBeNull("a 429 with no number is a client that retries at once for ever");
            third.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero);
            (await third.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Should().Contain("2 requests a minute", "the body names the limit");
            UsageOf(database, id).Count().Should().Be(2, "a 429 counts nothing");

            Run(BugsExe, $"--revoke --id {id}", _dir).Code.Should().Be(
                0, "the operator's stop button, run while the server serves — a real concurrent opener");
            (await Ingest(http)).StatusCode.Should().Be(
                HttpStatusCode.Unauthorized, "revoking must actually stop an ingest");

            Run(BugsExe, $"--urls http://127.0.0.1:{FreePort()}", _dir).Code.Should().Be(
                78, "one server per data directory: the limiter lives in the first one's memory");
        }
        finally
        {
            Stop(server);
        }

        Run(BugsExe, $"--urls http://127.0.0.1:{FreePort()}", _dir, rate: "1001").Code.Should().Be(
            78, "a rate past the cap is refused at startup rather than clamped");
    }

    /// <summary>
    /// The whole admin API over a real socket, from a real environment variable: issue, list, audit,
    /// see who is sending, revoke — and a key that really stops working.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is in THIS story and not story 4.</b> Every route can pass in-process and
    /// over a test handler while the DEPLOYED surface answers nothing: the administrators come from
    /// an environment variable parsed at startup, the gate is wired by hand rather than by routing,
    /// and the six new wire shapes are bound by a source-generated serializer that compiles whether
    /// or not it works. Deferring the real-binary check is how a broken shipped route gets accepted
    /// as complete. (Resolved plan decision 8.)</para>
    /// <para>It also asserts the case an operator hits on day one, on the real binary: with the
    /// variable ABSENT the server still starts and serves `/ingest`, and every admin route answers
    /// 401 — because a deployment whose secret was never filled in must not be a deployment that
    /// refuses to boot.</para>
    /// </remarks>
    [Fact]
    public async Task TheRealServerServesTheWholeAdminApiFromAnEnvironmentVariable()
    {
        MustExist(BugsExe);
        const string adminKey = "an-administrators-key-for-this-scenario";
        var (server, port) = await Serving(string.Empty, admins: $"# alice\n{adminKey}");
        try
        {
            using var stranger = Talking(port, string.Empty);
            (await stranger.GetAsync("/admin/keys", TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "over a real socket, with no credential");

            using var admin = Talking(port, adminKey);
            var key = await IssuedOverTheSocket(admin);

            // The key the real server minted really authenticates the real ingest route.
            using var contributor = Talking(port, key.Key);
            (await Ingest(contributor)).StatusCode.Should().Be(
                HttpStatusCode.OK, "a key issued through the API is a key the gate accepts");

            await TheKeyIsListedWithWhatItSent(admin, key);
            await TheActionIsInTheTrail(admin);
            await TheContributorIsSendingRightNow(admin, key);

            var revoked = await admin.PostAsync(
                $"/admin/keys/{key.Id}/revoke", content: null, TestContext.Current.CancellationToken);
            revoked.StatusCode.Should().Be(HttpStatusCode.OK, await Said(revoked));
            (await Ingest(contributor)).StatusCode.Should().Be(
                HttpStatusCode.Unauthorized, "revoking through the API must really stop an ingest");
        }
        finally
        {
            Stop(server);
        }

        // An out-of-range ADMIN limit is refused at startup, as its own setting, by the real process.
        Run(BugsExe, $"--urls http://127.0.0.1:{FreePort()}", _dir, admins: adminKey, adminRate: "1001")
            .Code.Should().Be(78, "the admin limit is validated and capped like the contributor one");
    }

    /// <summary>With no administrators configured, the real server still serves — and refuses every admin route.</summary>
    [Fact]
    public async Task TheRealServerStartsWithNoAdministratorsAndRefusesThemAll()
    {
        MustExist(BugsExe);
        var key = IssueKey();
        var (server, port) = await Serving(string.Empty);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            (await Ingest(http)).StatusCode.Should().Be(HttpStatusCode.OK, "contributors are unaffected");

            foreach (var route in new[] { "/admin/keys", "/admin/audit", "/admin/active" })
            {
                (await http.GetAsync(route, TestContext.Current.CancellationToken))
                    .StatusCode.Should().Be(
                        HttpStatusCode.Unauthorized,
                        $"{route} refuses a contributor's key exactly as it refuses a stranger's");
            }
        }
        finally
        {
            Stop(server);
        }
    }

    /// <summary>
    /// A credential that is BOTH a contributor key and an administrator line stops the server.
    /// </summary>
    /// <remarks>
    /// <para>The two stores are asked in order, so one string in both of them is a caller whose
    /// identity depends on which store still holds it: it uploads as a contributor, counted against
    /// its key row and limited by the contributor setting — and the moment that row is revoked the
    /// same bearer string becomes an administrator, uncounted and limited by the other setting. A
    /// revocation that PROMOTES a credential is the opposite of what an operator pressed the button
    /// for. (Code round, codex.)</para>
    /// <para>Refused at startup rather than resolved by a precedence rule, because every precedence
    /// is wrong in one direction: contributor-first is the surprise above, and admin-first would let
    /// pasting a contributor key into the variable silently grant administration. There is no
    /// legitimate reason for one string to be both, so it is a configuration error and the server
    /// says so and exits 78 — the same answer it gives an unusable rate limit.</para>
    /// <para>Over the real binary because only a process can show an exit code, and because the
    /// check needs the database open and the variable parsed — the two halves that only exist
    /// together at startup.</para>
    /// </remarks>
    [Fact]
    public void ACredentialThatIsBothAKeyAndAnAdministratorIsRefusedAtStartup()
    {
        MustExist(BugsExe);
        var key = IssueKey();
        key.Should().NotBeEmpty("the contributor key is this test's setup");

        var refused = Run(BugsExe, $"--urls http://127.0.0.1:{FreePort()}", _dir, admins: key);

        refused.Code.Should().Be(
            78,
            "a string that is both a contributor key and an administrator is a configuration error, "
            + $"not a caller with two roles; the server said: {refused.Err}");
        refused.Err.Should().Contain(
            AdminKeys.Variable, "and it must name the variable to edit");

        // The same server starts perfectly once the overlap is gone, so the refusal is about the
        // overlap and not about administrators being configured at all.
        var fine = Run(
            BugsExe, "--waiting", _dir, admins: "an-administrator-that-is-nobodys-contributor-key");
        fine.Code.Should().Be(0, $"the one-shots are unaffected; it said: {fine.Err}");
    }

    /// <summary>Issues through the real API and insists the one key-carrying response carried one.</summary>
    private static async Task<Issued> IssuedOverTheSocket(HttpClient admin)
    {
        var issued = await admin.PostAsJsonAsync(
            "/admin/keys", new { note = "issued over a real socket" }, TestContext.Current.CancellationToken);
        issued.StatusCode.Should().Be(HttpStatusCode.Created, await Said(issued));
        var key = await issued.Content.ReadFromJsonAsync<Issued>(TestContext.Current.CancellationToken);
        key!.Key.Should().NotBeNullOrWhiteSpace("the one response that carries a key must really carry it");

        return key;
    }

    /// <summary>The listing shows the key with the counter and the queue the real file holds.</summary>
    private static async Task TheKeyIsListedWithWhatItSent(HttpClient admin, Issued key)
    {
        var listed = await Read<KeysPage>(admin, "/admin/keys");

        listed.Total.Should().Be(1);
        listed.Items.Should().ContainSingle().Which.Id.Should().Be(key.Id);
        listed.Items[0].Sent.Should().Be(1, "the counter moved on the real file");
        listed.Items[0].Waiting.Should().Be(1);
    }

    /// <summary>The trail names the administrator by the id derived from the variable's own line.</summary>
    private static async Task TheActionIsInTheTrail(HttpClient admin)
    {
        var trail = await Read<AuditPage>(admin, "/admin/audit");

        trail.Items.Should().ContainSingle().Which.Action.Should().Be("issue");
        trail.Items[0].AdminId.Should().StartWith("admin-");
    }

    /// <summary>The live view shows both kinds of caller, each under its own prefix.</summary>
    private static async Task TheContributorIsSendingRightNow(HttpClient admin, Issued key)
    {
        var active = await Read<ActiveNow>(admin, "/admin/active");

        active.WindowSeconds.Should().Be(60);
        active.Items.Should().Contain(row => row.Id == $"key:{key.Id}", "the contributor just sent something")
            .And.Contain(row => row.Id.StartsWith("admin-", StringComparison.Ordinal), "so did the reader");
        active.Total.Should().BeGreaterThanOrEqualTo(2, "and the total counts what the page truncated to");
    }

    private static async Task<T> Read<T>(HttpClient http, string route)
    {
        using var reply = await http.GetAsync(route, TestContext.Current.CancellationToken);
        reply.StatusCode.Should().Be(HttpStatusCode.OK, await Said(reply));

        return (await reply.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>What the server actually answered, so a failure names it rather than a status code.</summary>
    private static async Task<string> Said(HttpResponseMessage reply) =>
        $"the server said: {await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}";

    private sealed record Issued(string Id, string Key, string Note, string CreatedUtc);

    private sealed record KeyListed(string Id, string Note, int Sent, int Waiting);

    private sealed record KeysPage(IReadOnlyList<KeyListed> Items, int Total, long? NextBefore);

    private sealed record AuditListed(long Id, string AdminId, string Action, string Target);

    private sealed record AuditPage(IReadOnlyList<AuditListed> Items, long? NextBefore);

    private sealed record ActiveCaller(string Id, int InWindow, bool Limited);

    private sealed record ActiveNow(IReadOnlyList<ActiveCaller> Items, int Limit, int Total, int WindowSeconds);

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

    private static Task<HttpResponseMessage> Ingest(HttpClient http) =>
        http.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);

    /// <summary>The same pair with words a person typed about it.</summary>
    private static readonly object OneCommentedPair = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_1(var_1) { }",
                skeletonAfter = "method_1(var_1) { lock (var_2) { } }",
                comment = "this one bit us in production",
            },
        },
    };

    /// <summary>What the key has done, read from the FILE by a third opener while the server serves.</summary>
    private static Usage UsageOf(string database, string id)
    {
        using var corpus = Corpus.Open(database);

        return corpus.UsageOf(new KeyId(id));
    }

    /// <summary>Mints a key through the real one-shot and reads its id off stderr, where the one-shot says it.</summary>
    private (string Key, string Id) IssueKeyAndId()
    {
        var issued = Run(BugsExe, "--issue-key --note a-scenario", _dir);
        issued.Code.Should().Be(0, $"--issue-key said: {issued.Err}");
        var id = Regex.Match(issued.Err, "issued ([0-9a-f]{16})").Groups[1].Value;
        id.Should().HaveLength(16, "the id is what --revoke needs, and stderr is where the one-shot says it");

        return (issued.Out.Trim(), id);
    }

    /// <summary>Seeds one kept pair into a client-side database the real CLI will read.</summary>
    private void SeedOneKeptPair() => Seed.KeptPairs(
        Client(),
        new CoaiMcp.Core.Collecting.CollectedPair(
            "GetOrAdd", "CSharp", "method_1(var_1) { }", "method_1(var_1) { lock (var_2) { } }"));

    /// <summary>Where the CLIENT's database lives, which is not the server's.</summary>
    private string Client()
    {
        var client = Path.Combine(_dir, "client");
        Directory.CreateDirectory(client);

        return client;
    }

    /// <summary>Mints a key through the real one-shot, which prints it once on stdout.</summary>
    private string IssueKey()
    {
        var issued = Run(BugsExe, "--issue-key --note a-scenario", _dir);
        issued.Code.Should().Be(0, $"--issue-key said: {issued.Err}");

        return issued.Out.Trim();
    }

    /// <summary>Starts the server and waits for it to answer, on a port it really got.</summary>
    /// <param name="exe">
    /// Which binary to start; this build's by default. A caller passes one to run a PREVIOUS
    /// release — the scenario that proves an old server has no route for a comment can only be
    /// written against the artefact that release really produced.
    /// </param>
    private Task<(Running Server, int Port)> Serving(
        string args, string admins = "", string rate = "", string exe = "") =>
        Serving(FreePorts(), args, admins, rate, exe);

    /// <summary>
    /// The same, over a caller's candidates — which is what makes a lost port testable.
    /// </summary>
    /// <remarks>
    /// <para><b>The race this closes.</b> <see cref="FreePort"/> asks the OS for a port, releases
    /// it, and hands the NUMBER to a process that binds it a moment later. Anything on the machine
    /// may take it in between — another test in this suite, another suite on a shared runner, an
    /// ephemeral outbound connection — and the server then exits without ever listening. Nine call
    /// sites took that number straight to a server, so this was nine chances per run for a green
    /// suite to go red about something no change had touched.</para>
    /// <para>`src_mcp/tests/LoopbackStub` closes the same race for its in-process listener, and
    /// this is deliberately NOT sharing its code: that one binds an `HttpListener` here and catches
    /// an exception carrying an errno, while this one launches a SEPARATE PROCESS and can observe
    /// only that it exited and what it printed. The retry loop looks alike; the detection has
    /// nothing in common, and the only genuinely shared part is the six lines of
    /// <see cref="FreePorts"/>. Linking one file between two test projects — there is no precedent
    /// for it here, and no shared test project to put it in — is more machinery than it removes.
    /// What IS reused is the DOCTRINE: a narrow classifier, a bounded retry, and a test that
    /// provokes the real collision rather than arguing about it.</para>
    /// <para>The bound matters as much as the retry. Ten lost candidates is a machine with no ports
    /// or a server that cannot start for its own reasons, and retrying for ever would turn either
    /// into a hang; so it stops, and the message carries what the server actually SAID.</para>
    /// </remarks>
    private async Task<(Running Server, int Port)> Serving(
        IEnumerable<int> candidates, string args, string admins = "", string rate = "",
        string exe = "")
    {
        var lost = new List<int>();
        var binary = exe.Length > 0 ? exe : BugsExe;

        foreach (var port in candidates)
        {
            var server = Start(
                binary, $"--urls http://127.0.0.1:{port} {args}".TrimEnd(), _dir, rate: rate, admins: admins);

            if (await Listening(server, port))
            {
                return (server, port);
            }

            var said = server.Text;
            Stop(server);

            LostToARace(said).Should().BeTrue(
                $"the server on port {port} stopped without listening, and not because the port was "
                + $"taken — so retrying would only lose the reason. It said: {said}");

            lost.Add(port);
            lost.Count.Should().BeLessThan(
                Attempts,
                $"{Attempts} candidates lost in a row is a machine out of ports, not a race; "
                + $"lost {string.Join(", ", lost)}");
        }

        throw new InvalidOperationException(
            $"no candidate port was left to try; lost {string.Join(", ", lost)}");
    }

    /// <summary>Runs one to completion and collects what it said.</summary>
    /// <remarks>
    /// Its own start, without the asynchronous draining: `ReadToEnd` on stdout while stderr fills is
    /// the other half of the same deadlock, so this reads stdout asynchronously and stderr on this
    /// thread — which is the documented pair that cannot block.
    /// </remarks>
    private static (int Code, string Out, string Err) Run(
        string exe, string args, string data, string key = "", string rate = "",
        string admins = "", string adminRate = "")
    {
        var how = Prepared(exe, args, data, key, rate, admins, adminRate);
        using var process = Process.Start(how)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000).Should().BeTrue($"{exe} {args} must finish");

        return (process.ExitCode, output.GetAwaiter().GetResult(), error);
    }

    public void Dispose() => Scratch.Delete(_dir);
}

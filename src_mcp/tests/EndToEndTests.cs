using System.Text.Json;
using Xunit;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// The whole story, replayed: a flawed plan → revisions → the gate → code rounds with a timeout
/// and a rate limit → a verdict → a clean machine. Every future change replays it.
/// </summary>
/// <remarks>
/// The vendors are scripted per round through the fake CLI's environment; nothing here touches a
/// real model or the network. What this proves that the unit tests cannot: the pieces compose —
/// dedup across vendors, the standing-rejection discount surviving a round boundary, partial
/// rounds still producing a verdict, and no worktree, child process or temp file left behind.
/// </remarks>
[Collection("fakecli-env")]
public sealed class EndToEndTests : IAsyncLifetime
{
    /// <summary>
    /// A scope, not a title. The code gate refuses a bare diff, so every code round in these tests
    /// carries what the change was supposed to achieve — which is what a real caller sends.
    /// </summary>
    private const string Scope = """
        # SCOPE

        The reviewer's own words must survive a failure. A reviewer that falls over is recorded with
        an outcome and nothing else, so the round summary says "unparseable" without the text that
        would not parse, and the same answer replayed by hand goes through cleanly.

        When it is done: the raw answer is kept beside the session, the refusal names the file, and
        a failed reviewer still reports what it consumed. Constraint: no new dependency, and the
        launcher stays the one in v2.Shared.
        """;

    private const string Clean = """{"findings": []}""";

    private const string FourMajors = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"},
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 40,
           "title": "no timeout on the outbound call", "why": "a hung peer hangs the request", "fix": "add a timeout"},
          {"severity": "major", "category": "architecture", "file": "app.cs", "line": 70,
           "title": "the parser reaches into the transport", "why": "layers cross", "fix": "invert it"},
          {"severity": "nit", "category": "convention", "file": "app.cs", "line": 90,
           "title": "trailing whitespace", "why": "noise", "fix": "trim"}
        ]}
        """;

    private const string OneMajor = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"},
          {"severity": "minor", "category": "ux", "file": "app.cs", "line": 12,
           "title": "the error message says nothing", "why": "unactionable", "fix": "name the field"}
        ]}
        """;

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;
    private string _worktreeRoot = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-e2e-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-e2e-data-").FullName;
        _worktreeRoot = Path.Combine(_data, "worktrees");
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 100)));
        await Git("add", ".");
        await Git("commit", "-m", "the feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private void Script(string answer, int exit = 0, string stderr = "")
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", exit.ToString());
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", stderr);
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>Same git as <see cref="Git"/>, but the answer comes back.</summary>
    private async Task<string> GitSays(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
        return result.StdOut.Trim();
    }

    /// <summary>One column of one stage's round, read straight from the projection.</summary>
    /// <remarks>
    /// Not through <c>RoundsQuery</c>: that is the log page's reader and shows what the page shows.
    /// The question here is whether the column was WRITTEN, which is a question about the table.
    /// </remarks>
    private string RoundColumn(string stage, string column)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_data, Store.RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = $"SELECT {column} FROM rounds WHERE stage = $stage";
        read.Parameters.AddWithValue("$stage", stage);
        return read.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    private PanelService Service(
        StagePolicy onExhausted = StagePolicy.Human,
        int maxRounds = 3,
        Serilog.ILogger? log = null) =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }, new("gemini") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(maxRounds, 2, onExhausted),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            log ?? Logger.None, Noticing.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string AcceptAll(JsonElement answer) =>
        JsonSerializer.Serialize(
            Enumerable.Range(0, answer.GetProperty("findings").GetArrayLength())
                .Select(i => new { finding = i, action = "accept" }));

    /// <summary>
    /// A whole round that answers NOTHING, and everything a person needs to ask it why.
    /// </summary>
    /// <remarks>
    /// <para>The scenario behind this: 2026-09-08, 16:12 UTC, eight remote reviewers answered
    /// <c>{"findings": []}</c> on a diff the local reviewer found eleven things in. Neither question
    /// could be asked afterwards — the round log named the RULES it sent and never the diff, and an
    /// `ok` outcome with zero findings dropped the vendor's raw text.</para>
    /// <para>Driven end to end rather than asserted on the pieces, because the three facts live in
    /// three different classes and the defect was that nothing joined them: `PanelService` assembles
    /// the context, `RoundAudit` writes the lines, and `ReviewerExecutor` keeps the file.</para>
    /// </remarks>
    [Fact]
    public async Task ARoundThatFoundNothing_SaysWhatItSent_AndKeepsWhatItWasTold()
    {
        var sink = new ListSink();
        var service = Service(log: new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());
        await service.OpenAsync(_repo, "feature");

        Script(Clean);
        await service.ReviewPlanAsync(_repo, "feature", "a plan nobody objects to");
        // The sink's own `{Message:lj}` rendering, never `RenderMessage()`: the latter quotes every
        // string property, so the test would be asserting against an artefact of itself instead of
        // the line that lands in the log file.
        var lines = () => sink.Lines;

        lines().Should().ContainMatch("context for review: plan * bytes*",
            "a plan round has no diff, and says so in the same shape the code round uses");

        await service.ResolveAsync(_repo, "feature", "[]");
        Script(Clean);
        await service.ReviewCodeAsync(_repo, "feature", "main", Scope);

        // What it ASSEMBLED. Every number was already computed before this change and thrown away,
        // which is why "did they see the diff" had to be answered by subtracting a rules byte count
        // from a token total in the ledger.
        lines().Should().ContainMatch("context for review: diff * bytes over * file(s), * elided; plan * bytes; rules * bytes");

        // And WHICH commit it is a diff of. Asserted here rather than only on `ContextAssembler`,
        // because the value crossing that seam is exactly what could go on being logged as the ref
        // the caller named while the diff was taken against something else — which is the state
        // three rounds were reviewed in before anybody noticed. (codex, the plan round.)
        lines().Should().ContainMatch("diffed against the merge base * of main and *",
            "a round that does not name what it compared against is a round nobody can re-check");

        // What each reviewer RECEIVED. The two are the same number only while nothing between them
        // is broken, and that is precisely what could not be established.
        lines().Should().ContainMatch("*opening: 6 reviewer(s)*bytes]*");

        // And what it was told. Two reviewers on the plan round and six on the code round answered
        // with nothing, so eight answers are on disk — each named for the reviewer that gave it,
        // which is the whole point: a person reading a silent round has twelve of these and needs
        // the one belonging to codex/Architecture. The two PlanCritique files land in the same
        // MILLISECOND and do not collide, because the provider and the role are in the name.
        var kept = Directory.GetFiles(Path.Combine(_data, "empty")).Select(Path.GetFileName).ToList();
        kept.Should().HaveCount(8, "every reviewer that found nothing kept what it actually said");
        kept.Should().ContainMatch("codex-Architecture-*").And.ContainMatch("gemini-PlanCritique-*");
        lines().Should().ContainMatch("*answered in *0 finding(s)*its answer was kept at*",
            "and the reviewer's own line names the file, which is where somebody chasing a silent round reads");
        (await File.ReadAllTextAsync(
                Path.Combine(_data, "empty", kept[0]!), TestContext.Current.CancellationToken))
            .Should().Contain("findings", "the file holds what the vendor said, not a note that it said nothing");
    }

    [Fact]
    public async Task FullLoop_FlawedPlan_RevisesToTheGate_ThenCodeRounds_ThenDone()
    {
        var service = Service();
        Parse(await service.OpenAsync(_repo, "feature")).GetProperty("stage").GetString().Should().Be("PlanReview");

        // Round 1: both vendors raise the same three majors (and a nit that must not gate).
        Script(FourMajors);
        var round1 = Parse(await service.ReviewPlanAsync(_repo, "feature", "the flawed plan"));
        round1.GetProperty("verdict").GetString().Should().Be("revise");
        round1.GetProperty("gatingCount").GetInt32().Should().Be(3, "3 majors after cross-vendor dedup; the nit never counts");
        round1.GetProperty("findings").GetArrayLength().Should().Be(4, "the nit is reported, just not gating");
        round1.GetProperty("findings")[0].GetProperty("providers").GetArrayLength().Should().Be(2);
        await service.ResolveAsync(_repo, "feature", AcceptAll(round1));

        // Round 2: the plan improved; one major left — at the threshold of 2, the gate passes.
        Script(OneMajor);
        var round2 = Parse(await service.ReviewPlanAsync(_repo, "feature", "the improved plan"));
        round2.GetProperty("verdict").GetString().Should().Be("proceed");
        round2.GetProperty("gatingCount").GetInt32().Should().Be(1);
        Parse(await service.ResolveAsync(_repo, "feature", AcceptAll(round2)))
            .GetProperty("instruction").GetString().Should().Contain("review_code");

        // The code stage: clean, so the session finishes.
        Script(Clean);
        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));
        code.GetProperty("verdict").GetString().Should().Be("proceed");
        code.GetProperty("reviewers").GetString().Should().Contain("all 6 reviewers answered", "3 roles x 2 providers");
        // And WHY it is three roles and not four. This fixture repository has no written rules, so
        // the Conventions reviewers are dropped — correctly, because a conventions pass with nothing
        // to judge against would invent a standard — and until now that was said only in the server's
        // own log. The AI that called the gate got a thinner round and no sentence at all.
        code.GetProperty("reviewers").GetString().Should()
            .Contain("Conventions was not asked")
            .And.Contain("no written rules")
            .And.NotContain("could not run", "a decision this gate made must not read as a failure");
        Parse(await service.ResolveAsync(_repo, "feature", "[]")).GetProperty("stage").GetString().Should().Be("Done");

        // The trail replays the whole story.
        var status = Parse(await service.StatusAsync(_repo, "feature"));
        status.GetProperty("rounds").GetArrayLength().Should().Be(3);
        status.GetProperty("rounds").EnumerateArray().Select(r => r.GetProperty("verdict").GetString())
            .Should().Equal("revise", "proceed", "proceed");
    }

    /// <summary>
    /// The caller accepted three findings, and the next round handed them straight back.
    /// </summary>
    /// <remarks>
    /// <para>Phase 2's instrument, driven end to end rather than asserted about: a real session, real
    /// rounds through the fake CLI, real decisions, and the number read back out of the database the
    /// panel reads. What no unit test can see is that the count is written where the ROUND is
    /// projected — three classes have to agree (the gate merges, the store records the decisions, the
    /// projection asks about them) and nothing joins them but this path.</para>
    /// <para><b>Nothing is called.</b> The round proceeds exactly as it would have; the only
    /// difference is a number on the row and one line in the audit.</para>
    /// </remarks>
    [Fact]
    public async Task AnAcceptedFindingRaisedAgain_IsCountedOnTheRound_AndNothingIsCalled()
    {
        var sink = new ListSink();
        var service = Service(log: new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger());
        await service.OpenAsync(_repo, "feature");

        // Round 1: four findings, and the caller accepts every one of them.
        Script(FourMajors);
        var round1 = Parse(await service.ReviewPlanAsync(_repo, "feature", "the flawed plan"));
        await service.ResolveAsync(_repo, "feature", AcceptAll(round1));

        // Round 2: the vendors say the SAME things. The plan changed; the defects did not.
        Script(FourMajors);
        var round2 = Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan, revised"));
        round2.GetProperty("findings").GetArrayLength().Should().Be(4, "the round runs exactly as before");

        var rounds = Store.RoundsQuery.Read(_data).Rounds;
        var first = rounds.Single(r => r.Number == 1);
        var second = rounds.Single(r => r.Number == 2);

        second.ConsultMissed.Should().Be(4, "every finding the caller accepted came back");
        first.ConsultMissed.Should().Be(0, "the first round had nothing to have accepted yet");

        // The audit SAYS it, in the trail the round writes, and says it as a measurement rather than
        // an instruction — the line a person meets when they ask why a round felt like the last one.
        sink.Lines.Should().ContainMatch("an automatic consultation could have fired here: 4 accepted finding(s)*");
        sink.Lines.Should().NotContainMatch("*consult now*");

        // And NOTHING was called: no consultation was opened, which is the whole promise of this
        // story. The directory is the one the consult tool writes into.
        Directory.Exists(Path.Combine(_data, "consultations")).Should().BeFalse(
            "story 6 measures; the trigger it informs does not exist yet");
    }

    [Fact]
    public async Task ARoundThatFixedWhatItAccepted_CountsNothing()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        Script(FourMajors);
        var round1 = Parse(await service.ReviewPlanAsync(_repo, "feature", "the flawed plan"));
        await service.ResolveAsync(_repo, "feature", AcceptAll(round1));

        // A different defect this time, which is what a round after a real fix looks like. NOT
        // `OneMajor`: that fixture repeats the first of `FourMajors` verbatim — which the counter
        // noticed the first time this test ran, and is the shape of the thing it exists to find.
        Script("""
            {"findings": [
              {"severity": "major", "category": "performance", "file": "other.cs", "line": 8,
               "title": "the cache is rebuilt on every request", "why": "one allocation per call", "fix": "hold it"}
            ]}
            """);
        await service.ReviewPlanAsync(_repo, "feature", "the improved plan");

        Store.RoundsQuery.Read(_data).Rounds.Single(r => r.Number == 2).ConsultMissed.Should().Be(0);
    }

    /// <summary>
    /// Two decisions about one finding is a contradiction, and it is refused rather than averaged.
    /// </summary>
    /// <remarks>
    /// <para>Found by the code round over the ordinal fix, by two vendors independently. Both
    /// entries passed every check: the range test looked at each one alone, so an accept and a
    /// reject for the same index both became decisions. The projection then wrote both in order —
    /// the LAST one silently winning — while <c>RecordClosing</c> counted both, so a round with one
    /// finding closed as one accepted AND one rejected. Neither number was true.</para>
    /// <para>Refusing is right rather than taking the last: a caller that says two things about one
    /// finding has a bug, and guessing which half it meant hides it.</para>
    /// </remarks>
    [Fact]
    public async Task ResolvingOneFindingTwiceInACall_IsRefused_RatherThanCountedTwice()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(OneMajor);
        var round = Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan"));
        round.GetProperty("findings").GetArrayLength().Should().BeGreaterThan(0, "the round must have something to decide");

        var raw = await service.ResolveAsync(_repo, "feature",
            """[{"finding":0,"action":"accept"},{"finding":0,"action":"reject","reason":"on reflection, no"}]""");

        // Asserted on the RAW answer first, so a failure says what went wrong rather than throwing
        // KeyNotFoundException on a property this call should never have produced.
        raw.Should().Contain("\"error\"",
            "accepting AND rejecting finding 0 in one call is a contradiction: it must be refused, "
            + "not recorded as two decisions about one finding");
        Parse(raw).GetProperty("error").GetString().Should()
            .Contain("finding 0", "the refusal must name the index that was sent twice")
            .And.Contain("twice");
    }

    [Fact]
    public async Task CodeRound_WithEveryReviewerFailing_CallsAHuman_NeverProceeds()
    {
        // This test asserted `proceed` until the first real run showed what that means: both
        // vendors failed (one out of quota, one refusing an untrusted folder), no findings
        // arrived, and the gate opened. The test had encoded the bug. It now asserts the rule.
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "feature", "plan");
        await service.ResolveAsync(_repo, "feature", "[]");

        Script(Clean, exit: 1, stderr: "429 Too Many Requests");
        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        code.GetProperty("verdict").GetString().Should().Be("call_human");
        code.GetProperty("reviewers").GetString().Should().Contain("0 of 6").And.Contain("rate limited");
        // A round can fail AND have skipped a role, and the failure must not swallow the skip: an
        // implementation that built the sentence only for the happy path would still pass the check
        // above while this reader — the one whose round went wrong — learned nothing about the
        // fourth role. (codex, the plan round.)
        code.GetProperty("reviewers").GetString().Should().Contain("Conventions was not asked");
        code.GetProperty("instruction").GetString().Should().Contain("do not proceed on your own");
    }

    [Fact]
    public async Task OneVendorDown_TheOtherStillGates()
    {
        // The realistic case the quota outage produced: a panel of one is still a panel.
        var service = new PanelService(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = "codex-that-is-not-installed" }, new("gemini") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None, Noticing.None);
        await service.OpenAsync(_repo, "feature");

        Script(FourMajors);
        var round = Parse(await service.ReviewPlanAsync(_repo, "feature", "the flawed plan"));

        round.GetProperty("verdict").GetString().Should().Be("revise", "one vendor's findings still gate");
        round.GetProperty("gatingCount").GetInt32().Should().Be(3);
        round.GetProperty("reviewers").GetString().Should().Contain("1 of 2");
    }

    [Fact]
    public async Task MaxRoundsExhausted_UnderEscalate_FiresTheLadderInOrder()
    {
        var service = Service(StagePolicy.Escalate, maxRounds: 1);
        await service.OpenAsync(_repo, "feature");

        var steps = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            Script(FourMajors);
            var round = Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan"));
            round.GetProperty("verdict").GetString().Should().Be("escalated");
            steps.Add(round.GetProperty("escalationStep").GetString());
            await service.ResolveAsync(_repo, "feature", AcceptAll(round));
        }

        steps.Should().Equal("ReviewerEffortUp", "ReviewerModelUp", "ArbiterModelUp");

        Script(FourMajors);
        Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan"))
            .GetProperty("verdict").GetString().Should().Be("call_human", "the ladder is exhausted");
    }

    [Fact]
    public async Task ACodeRound_RecordsWhatItsDiffWasAgainst_NotOnlyWhatItWasAt()
    {
        // `head_sha` is one end of the range the reviewers read. The other end lived in a local
        // inside the call that resolved it and was gone the moment the call returned, so a round's
        // diff could not be rebuilt from the store at all — the one fact about a round that the
        // repository does not keep on its own behalf.
        //
        // What is recorded is the RESOLVED base, not the ref the caller named: `main` resolves to
        // the merge base of main and the branch, which in this fixture is main's own commit.
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "feature", "plan");
        await service.ResolveAsync(_repo, "feature", "[]");

        Script(Clean);
        await service.ReviewCodeAsync(_repo, "feature", "main", Scope);

        RoundColumn("CodeReview", "base_ref").Should().Be(
            await GitSays("rev-parse", "main"),
            "the diff was taken against main's commit, and that is what makes it reproducible");

        // And a plan round leaves it empty rather than guessing: it assembles no diff, so there is
        // no base, and an invented one would be a fact nobody established.
        RoundColumn("PlanReview", "base_ref").Should().BeEmpty("a plan round compares nothing");
    }

    [Fact]
    public async Task AfterTheWholeRun_TheMachineIsClean()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "feature", "plan");
        await service.ResolveAsync(_repo, "feature", "[]");
        await service.ReviewCodeAsync(_repo, "feature", "main", Scope);
        await service.ResolveAsync(_repo, "feature", "[]");

        // No worktrees, and the live checkout untouched.
        var worktrees = await _launcher.RunAsync(new ProcessRequest("git", ["worktree", "list", "--porcelain"], _repo));
        worktrees.StdOut.Should().NotContain("coai-wt-");
        Directory.Exists(_worktreeRoot).Should().BeTrue();
        Directory.GetDirectories(_worktreeRoot).Should().BeEmpty("every round removes its own tree");

        var status = await _launcher.RunAsync(new ProcessRequest("git", ["status", "--short"], _repo));
        status.StdOut.Trim().Should().BeEmpty("reviewers are read-only, in a worktree");
    }

    // ---------------------------------------------------------------------------------------------
    // Nothing to review is SAID, never passed (todo/PLAN_a_failed_round_can_be_retried.md, S1).
    //
    // An empty diff used to launch every reviewer over an empty "## The change", collect nobody's
    // findings and answer `proceed` — and the session then ended, so the real change could never be
    // reviewed on that branch. A developer who forgot to commit was told "all clean".

    /// <summary>A session on <paramref name="branch"/> whose plan round has passed.</summary>
    private async Task<PanelService> PlanPassedOn(string branch)
    {
        var service = Service();
        await service.OpenAsync(_repo, branch);
        Script(Clean);
        await service.ReviewPlanAsync(_repo, branch, "plan");
        await service.ResolveAsync(_repo, branch, "[]");
        return service;
    }

    private async Task<int> RoundsOn(PanelService service, string branch) =>
        Parse(await service.StatusAsync(_repo, branch)).GetProperty("rounds").GetArrayLength();

    [Fact]
    public async Task ABranchWithNoChangeOverItsBase_IsRefused_AndNoReviewerRuns()
    {
        var service = await PlanPassedOn("feature");

        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "feature", Scope));

        code.TryGetProperty("verdict", out _).Should().BeFalse($"nothing was reviewed, so nothing may pass: {code}");
        code.GetProperty("error").GetString().Should().Contain("nothing to review")
            .And.Contain("no committed change");
        (await RoundsOn(service, "feature")).Should().Be(1, "the refusal records no round — only the plan round exists");
        Directory.Exists(_worktreeRoot).Should().BeFalse("no worktree is made for nothing");
    }

    [Fact]
    public async Task AChangeLeftUncommitted_IsNamed_InTheRefusal()
    {
        // The branch stands where its base does, and the work is in the checkout — the "forgot to
        // commit" case, and the one where committing is not allowed.
        await Git("checkout", "-b", "uncommitted");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "edited but never committed\n");
        var service = await PlanPassedOn("uncommitted");

        var code = Parse(await service.ReviewCodeAsync(_repo, "uncommitted", "feature", Scope));

        code.GetProperty("error").GetString().Should().Contain("nothing to review")
            .And.Contain("1 uncommitted file").And.Contain("app.cs")
            .And.Contain("COMMITTED changes only");
        (await RoundsOn(service, "uncommitted")).Should().Be(1);
    }

    [Fact]
    public async Task ABranchThatChangedOnlyExcludedFiles_NamesThem_InsteadOfPassing()
    {
        await Git("checkout", "-b", "lockonly");
        await File.WriteAllTextAsync(Path.Combine(_repo, "package-lock.json"), "{}\n");
        await Git("add", ".");
        await Git("commit", "-m", "only a lock file");
        var service = await PlanPassedOn("lockonly");

        var code = Parse(await service.ReviewCodeAsync(_repo, "lockonly", "feature", Scope));

        code.GetProperty("error").GetString().Should().Contain("nothing to review")
            .And.Contain("package-lock.json");
        (await RoundsOn(service, "lockonly")).Should().Be(1);
    }

    [Fact]
    public async Task AnUncommittedTail_IsSaidNotReviewed_OnARoundThatPasses()
    {
        // Committed work AND an uncommitted file: the diff is not empty, so the round runs — and
        // the answer must say what it did not look at, instead of letting "proceed" cover it.
        var service = await PlanPassedOn("feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "notes.txt"), "not committed yet\n");

        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        code.GetProperty("verdict").GetString().Should().Be("proceed");
        code.GetProperty("reviewers").GetString().Should().Contain("1 uncommitted file")
            .And.Contain("notes.txt").And.Contain("NOT reviewed");
    }

    [Fact]
    public async Task ABaseThatNamesNoCommit_IsRefusedByName_NotCalledEmpty()
    {
        var service = await PlanPassedOn("feature");

        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "origin/nowhere", Scope));

        code.GetProperty("error").GetString().Should().Contain("origin/nowhere")
            .And.NotContain("nothing to review");
        (await RoundsOn(service, "feature")).Should().Be(1);
    }

    // ---------------------------------------------------------------------------------------------
    // A gate can be run again (issue #490; todo/PLAN_a_failed_round_can_be_retried.md, S3a).
    //
    // A code round closed the session, `open` is idempotent, and the refusal said "open a new one" —
    // a door that did not exist. So a checkpoint round forbade the final one, and agents cut ten
    // review/* branches for one feature just to get fresh sessions.

    /// <summary>A session on "feature" whose code round has passed and been resolved: Done.</summary>
    private async Task<PanelService> CodeDoneOnFeature()
    {
        var service = await PlanPassedOn("feature");
        Script(Clean);
        Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope)).GetProperty("verdict").GetString()
            .Should().Be("proceed");
        await service.ResolveAsync(_repo, "feature", "[]");
        Parse(await service.StatusAsync(_repo, "feature")).GetProperty("stage").GetString().Should().Be("Done");
        return service;
    }

    [Fact]
    public async Task ADoneSession_RunsASecondCodeRound_ForNewCommits_WhenAskedAgain()
    {
        var service = await CodeDoneOnFeature();
        await File.AppendAllTextAsync(Path.Combine(_repo, "app.cs"), "the checkpoint's follow-up\n");
        await Git("add", ".");
        await Git("commit", "-m", "after the checkpoint");

        var again = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope, again: true));

        again.GetProperty("verdict").GetString().Should().Be("proceed", $"the new commits are reviewed: {again}");
        (await RoundsOn(service, "feature")).Should().Be(3, "plan, the first code round, and this one");
        await service.ResolveAsync(_repo, "feature", "[]");
        Parse(await service.StatusAsync(_repo, "feature")).GetProperty("stage").GetString().Should().Be("Done",
            "the round it reopened closes the session again");
    }

    [Fact]
    public async Task AskingAgain_WithNoNewCommit_IsRefused_AndSaysWhy()
    {
        var service = await CodeDoneOnFeature();

        var again = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope, again: true));

        again.GetProperty("error").GetString().Should().Contain("no new commit since code round 1");
        (await RoundsOn(service, "feature")).Should().Be(2, "a refusal records no round");
        Parse(await service.StatusAsync(_repo, "feature")).GetProperty("stage").GetString().Should().Be("Done",
            "and leaves the session as it was");
    }

    [Fact]
    public async Task AskingAgain_AfterOnlyExcludedFilesChanged_IsRefused_AndNamesThem()
    {
        var service = await CodeDoneOnFeature();
        await File.WriteAllTextAsync(Path.Combine(_repo, "package-lock.json"), "{}\n");
        await Git("add", ".");
        await Git("commit", "-m", "only a lock file since the round");

        var again = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope, again: true));

        again.GetProperty("error").GetString().Should().Contain("nothing reviewable since code round 1")
            .And.Contain("package-lock.json");
        Parse(await service.StatusAsync(_repo, "feature")).GetProperty("stage").GetString().Should().Be("Done");
    }

    [Fact]
    public async Task ADoneSession_NamesTheDoorItHas()
    {
        var service = await CodeDoneOnFeature();

        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        code.GetProperty("error").GetString().Should().Contain("again: true",
            "a refusal with no door is a stall, and 'open a new one' named a door that did not exist");
    }

    /// <summary>
    /// A LOST reply is no longer a blind decision (S3b): a round that saved its findings as pending
    /// refuses the next one until they are resolved, and `resolve` addresses them by index — so
    /// `status` must hand the same list back, in the same order.
    /// </summary>
    [Fact]
    public async Task ARoundAwaitingResolve_HandsItsFindingsBackThroughStatus()
    {
        var service = await PlanPassedOn("feature");
        Script(FourMajors);
        var reply = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));
        var sent = reply.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("title").GetString()).ToList();
        sent.Should().NotBeEmpty();

        // The reply is lost. A resumed caller has only status to go on.
        var status = Parse(await service.StatusAsync(_repo, "feature"));

        status.GetProperty("awaitingResolve").GetBoolean().Should().BeTrue();
        status.GetProperty("pending").EnumerateArray().Select(f => f.GetProperty("title").GetString())
            .Should().Equal(sent);
    }

    [Fact]
    public async Task ASessionWithNothingToResolve_CarriesNoPendingList()
    {
        var service = await PlanPassedOn("feature");

        var status = Parse(await service.StatusAsync(_repo, "feature"));

        status.GetProperty("awaitingResolve").GetBoolean().Should().BeFalse();
        status.TryGetProperty("pending", out var pending).Should().BeTrue();
        pending.GetArrayLength().Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------
    // One mutating call per session (todo/PLAN_a_failed_round_can_be_retried.md, S4).
    //
    // A round reads the session, runs for minutes and writes it back; two in flight over one session
    // both computed the next round from the same file, and the loser's round vanished. With `again`
    // reopening finished sessions, that is an ordinary thing to try. The claim is held here the way
    // ANOTHER call — another window, another agent — would hold it.

    [Fact]
    public async Task ARoundOnASessionAnotherCallIsChanging_IsRefused_BeforeAnyReviewerRuns()
    {
        var service = await PlanPassedOn("feature");
        Script(Clean);

        using (SessionClaim.TryTake(_data, _repo, "feature"))
        {
            var busy = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

            busy.GetProperty("error").GetString().Should().Contain("another call is already changing");
            (await RoundsOn(service, "feature")).Should().Be(1, "nothing was started");
        }

        Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope)).GetProperty("verdict").GetString()
            .Should().Be("proceed", "the claim is released with the call that held it");
    }

    [Fact]
    public async Task AResolveWhileAnotherCallHoldsTheSession_IsRefused()
    {
        var service = await PlanPassedOn("feature");
        Script(FourMajors);
        await service.ReviewCodeAsync(_repo, "feature", "main", Scope);

        using (SessionClaim.TryTake(_data, _repo, "feature"))
        {
            Parse(await service.ResolveAsync(_repo, "feature", "[]")).GetProperty("error").GetString()
                .Should().Contain("another call is already changing");
        }

        Parse(await service.StatusAsync(_repo, "feature")).GetProperty("awaitingResolve").GetBoolean()
            .Should().BeTrue("the refused resolve decided nothing");
    }
}

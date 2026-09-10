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
            log ?? Logger.None);

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
        Parse(await service.ResolveAsync(_repo, "feature", "[]")).GetProperty("stage").GetString().Should().Be("Done");

        // The trail replays the whole story.
        var status = Parse(await service.StatusAsync(_repo, "feature"));
        status.GetProperty("rounds").GetArrayLength().Should().Be(3);
        status.GetProperty("rounds").EnumerateArray().Select(r => r.GetProperty("verdict").GetString())
            .Should().Equal("revise", "proceed", "proceed");
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
            Logger.None);
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
}

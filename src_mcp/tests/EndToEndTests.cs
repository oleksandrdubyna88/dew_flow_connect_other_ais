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

    private PanelService Service(StagePolicy onExhausted = StagePolicy.Human, int maxRounds = 3) =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }, new("gemini") { ExecutablePath = FakeCliExe }],
                Rounds = new PanelConfig(maxRounds, Threshold: 2, onExhausted),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string AcceptAll(JsonElement answer) =>
        JsonSerializer.Serialize(
            Enumerable.Range(0, answer.GetProperty("findings").GetArrayLength())
                .Select(i => new { finding = i, action = "accept" }));

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
        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", "the improved plan"));
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
        var code = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", "plan"));

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
                Rounds = new PanelConfig(3, 2, StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
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
        await service.ReviewCodeAsync(_repo, "feature", "main", "plan");
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

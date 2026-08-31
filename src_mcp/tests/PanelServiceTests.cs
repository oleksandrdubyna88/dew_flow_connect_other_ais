using System.Text.Json;
using Xunit;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>Tests that steer the fake CLI through PROCESS-WIDE environment variables share one
/// collection so they never interleave — env is global state.</summary>
[CollectionDefinition("fakecli-env", DisableParallelization = true)]
public sealed class FakeCliEnvCollection;

/// <summary>
/// The whole orchestrator against real git and the vendor-mode fake CLI: this is the epic-04 DoD
/// line — the full loop, from a plain caller, with no vendor anywhere near CI.
/// </summary>
[Collection("fakecli-env")]
public sealed class PanelServiceTests : IAsyncLifetime
{
    private const string CleanReview = """{"findings": []}""";

    private const string OneMajor = """
        {"findings": [{"severity": "major", "category": "security", "file": "app.cs", "line": 1,
          "title": "token compared with ==", "why": "timing side channel", "fix": "use FixedTimeEquals"}]}
        """;

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-panel-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-panel-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v2\n");
        await Git("add", ".");
        await Git("commit", "-m", "change");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        SetAnswer(CleanReview);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_RECORD_DIR", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        try
        {
            Directory.Delete(_repo, recursive: true);
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return ValueTask.CompletedTask;
    }

    private static void SetAnswer(string json)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", json);       // the gemini path
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", json); // the codex path
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service(int threshold = 2, StagePolicy onExhausted = StagePolicy.Human, int maxRounds = 3) =>
        new(
            new PanelSettings
            {
                Providers =
                [
                    new("codex") { ExecutablePath = FakeCliExe },
                    new("gemini") { ExecutablePath = FakeCliExe },
                ],
                Rounds = new PanelConfig(maxRounds, threshold, onExhausted),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Open_IsIdempotentPerRepoAndBranch()
    {
        var service = Service();
        var first = Parse(await service.OpenAsync(_repo, "feature"));
        var second = Parse(await service.OpenAsync(_repo, "feature"));

        second.GetProperty("sessionId").GetString().Should().Be(first.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task ReviewCode_BeforePlanProceed_Refuses()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", "the plan"));

        answer.GetProperty("error").GetString().Should().Contain("plan gate comes first");
    }

    [Fact]
    public async Task ReviewPlan_MergesTheSameFindingAcrossProviders_IntoOne()
    {
        SetAnswer(OneMajor);
        var service = Service(threshold: 0);
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        answer.GetProperty("verdict").GetString().Should().Be("revise");
        var findings = answer.GetProperty("findings");
        findings.GetArrayLength().Should().Be(1, "codex and gemini raised the SAME defect");
        findings[0].GetProperty("providers").GetArrayLength().Should().Be(2);
        answer.GetProperty("reviewers").GetString().Should().Contain("all 2 reviewers answered");
    }

    [Fact]
    public async Task FullLoop_PlanProceeds_CodePasses_SessionDone()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan"))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        Parse(await service.ResolveAsync(_repo, "feature", "[]"))
            .GetProperty("instruction").GetString().Should().Contain("review_code");

        Parse(await service.ReviewCodeAsync(_repo, "feature", "main", "the plan"))
            .GetProperty("verdict").GetString().Should().Be("proceed");
        Parse(await service.ResolveAsync(_repo, "feature", "[]"))
            .GetProperty("stage").GetString().Should().Be("Done");
    }

    [Fact]
    public async Task Resolve_RejectionWithoutAReason_RefusesTheWholeCall()
    {
        SetAnswer(OneMajor);
        var service = Service(threshold: 0);
        await service.OpenAsync(_repo, "feature");
        await service.ReviewPlanAsync(_repo, "feature", "the plan");

        var answer = Parse(await service.ResolveAsync(_repo, "feature", """[{"finding": 0, "action": "reject"}]"""));

        answer.GetProperty("error").GetString().Should().Contain("without a reason");
    }

    [Fact]
    public async Task RejectedWithReason_DoesNotGate_TheNextRound()
    {
        SetAnswer(OneMajor);
        var service = Service(threshold: 0);
        await service.OpenAsync(_repo, "feature");
        await service.ReviewPlanAsync(_repo, "feature", "the plan");
        await service.ResolveAsync(_repo, "feature",
            """[{"finding": 0, "action": "reject", "reason": "constant time is not required for this token"}]""");

        // The reviewers repeat the same remark with the same argument next round.
        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan v2"));

        answer.GetProperty("verdict").GetString().Should().Be("proceed", "a standing rejection does not count");
        answer.GetProperty("discounted").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Status_SurvivesAServerRestart()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        await service.ReviewPlanAsync(_repo, "feature", "the plan");
        await service.ResolveAsync(_repo, "feature", "[]");

        var reborn = Service(); // a fresh instance over the same data dir IS the restart
        var status = Parse(await reborn.StatusAsync(_repo, "feature"));

        status.GetProperty("stage").GetString().Should().Be("CodeReview");
        status.GetProperty("rounds").GetArrayLength().Should().Be(1);
        status.GetProperty("rounds")[0].GetProperty("verdict").GetString().Should().Be("proceed");
    }

    [Fact]
    public async Task CodeStage_FansOutThreeRolesPerProvider_WithThreeDistinctPrompts()
    {
        var record = Directory.CreateTempSubdirectory("coai-record-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            var service = Service();
            await service.OpenAsync(_repo, "feature");
            await service.ReviewPlanAsync(_repo, "feature", "the plan");
            await service.ResolveAsync(_repo, "feature", "[]");
            Directory.GetFiles(record).Should().HaveCount(2, "the plan stage is one reviewer per provider");
            foreach (var file in Directory.GetFiles(record))
            {
                File.Delete(file);
            }

            await service.ReviewCodeAsync(_repo, "feature", "main", "the plan");

            var argvs = Directory.GetFiles(record, "*.argv")
                .Select(f => File.ReadAllText(f).Split('\0'))
                .ToList();
            argvs.Should().HaveCount(6, "two providers x three roles");
            // codex carries the prompt last; gemini carries it after -p.
            var prompts = argvs
                .Select(a => Array.IndexOf(a, "-p") is var i and >= 0 && i + 1 < a.Length ? a[i + 1] : a[^1])
                .Distinct()
                .ToList();
            prompts.Should().HaveCount(3, "three roles, three distinct prompts — each vendor gets the same three");
            prompts.Should().Contain(p => p.Contains("ARCHITECTURE reviewer"));
            prompts.Should().Contain(p => p.Contains("SECURITY AND RELIABILITY"));
            prompts.Should().Contain(p => p.Contains("CODE ONLY: no browser"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
        }
    }

    [Fact]
    public async Task MaxRoundsExhausted_YieldsCallHuman_WithTheOpenCount()
    {
        SetAnswer(OneMajor);
        var service = Service(threshold: 0, maxRounds: 1);
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        answer.GetProperty("verdict").GetString().Should().Be("call_human");
        answer.GetProperty("instruction").GetString().Should().Contain("do not proceed on your own");
    }
}

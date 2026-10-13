using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Vibe reviewer end to end through a FAKE <c>vibe</c> — a real child process, started by the real launcher, so what is
/// asserted is what crossed the process boundary (S2 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): the built environment
/// with the Python CLI pinned and no inherited <c>VIBE_*</c>, the export read and then gone, a home the run left something in
/// refused before the next launch, and — through a real code round — a reviewer started in the round's snapshot, which is
/// gone with the round.
/// </summary>
/// <remarks>
/// The child gets the MINIMAL environment, so no <c>FAKECLI_*</c> variable reaches it: the fake is steered by the file in
/// the temp directory (<c>fakecli-minimal.json</c>), which is why this class shares the <c>fakecli-env</c> collection.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AVibeReviewerRunsThroughAFakeVibeTests : IAsyncLifetime
{
    private const string Clean = """{"findings": []}""";

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private static string SteeringFile => Path.Combine(Path.GetTempPath(), "fakecli-minimal.json");

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("coai-answers-vibefake-");
    private string? _parentMcp;

    public ValueTask InitializeAsync()
    {
        FakeCliSteering.Reset();
        _parentMcp = Environment.GetEnvironmentVariable("VIBE_MCP_SERVERS");
        // In the PARENT, where a launch that inherited would carry it (§22 A: it starts a server under an empty home).
        Environment.SetEnvironmentVariable("VIBE_MCP_SERVERS", """[{"name":"x","transport":"stdio","command":"python"}]""");

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("VIBE_MCP_SERVERS", _parentMcp);
        File.Delete(SteeringFile);
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    private static string Array(string text) =>
        JsonSerializer.Serialize(new object[]
        {
            new Dictionary<string, object> { ["type"] = "message", ["role"] = "user", ["content"] = "review" },
            new Dictionary<string, object> { ["type"] = "message", ["role"] = "assistant", ["content"] = new object[] { new Dictionary<string, string> { ["type"] = "text", ["text"] = text } } },
        });

    private const string Export =
        """{"outcome":"finished","usage":{"input_tokens":1200,"output_tokens":34,"cached_input_tokens":0,"total_tokens":1234},"cost_usd":0.01,"config":{"active_model":"{{env:VIBE_ACTIVE_MODEL}}"}}""";

    private void Steer(string answer, string envRecord = "", string homeWrite = "", string recordDir = "", string version = "2.26.1") =>
        File.WriteAllText(SteeringFile, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["FAKECLI_STDOUT"] = Array(answer),
            ["FAKECLI_EXPORT_TEXT"] = Export,
            ["FAKECLI_VERSION_STDOUT"] = $"python.exe C:\\Users\\p\\.local\\bin\\vibe {version}\r\n",
            ["FAKECLI_RECORD_ENV"] = envRecord,
            ["FAKECLI_HOME_WRITE"] = homeWrite,
            ["FAKECLI_RECORD_DIR"] = recordDir,
        }));

    private ReviewerInvocation Invocation()
    {
        var work = Directory.CreateDirectory(_dir.At("work")).FullName;

        return new VibeRuntime("vibe", HostKind.Windows).Build(RoleCatalog.ArchitectureRole, "review this", work, _dir.At("s.json"), _dir,
            new ReviewerSettings("vibe") { ExecutablePath = FakeCliExe, Model = "glm-5-3", OwnedScratch = _dir.At("round"), Timeout = TimeSpan.FromSeconds(60) });
    }

    [Fact]
    public async Task TheBuiltEnvironment_IsWhatTheChildGets_AndTheReviewAndItsUsageAreRead()
    {
        var record = _dir.At("env.txt");
        Steer(Clean, envRecord: record);

        var outcome = await new ReviewerExecutor(_launcher).RunAsync(Invocation(), ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Usage.Should().Be(new Usage(1200, 34, null),
            "the export echoed the model the child was given, so the after-check passed and the export's usage was read");
        var seen = await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
        seen.Should().Contain("VIBE_CLI=python").And.Contain("VIBE_ACTIVE_MODEL=glm-5-3");
        seen.Should().ContainSingle(line => line.StartsWith("VIBE_HOME=", StringComparison.Ordinal))
            .Which.Should().StartWith("VIBE_HOME=" + _dir.At("round"));
        seen.Should().NotContain(line => line.StartsWith("VIBE_MCP_SERVERS", StringComparison.Ordinal), "the parent's VIBE_* never reaches the child");
        seen.Where(line => !line.StartsWith("VIBE_", StringComparison.Ordinal))
            .Should().OnlyContain(name => ProcessEnvironment.Minimal.Contains(name), "nothing but the minimal set is inherited");
    }

    [Fact]
    public async Task TheExport_IsGone_AfterTheLaunchWasRead()
    {
        Steer(Clean);

        await new ReviewerExecutor(_launcher).RunAsync(Invocation(), ct: TestContext.Current.CancellationToken);

        Directory.GetDirectories(_dir, "vx-*").Should().BeEmpty("D7: each attempt's export folder is deleted once read");
    }

    [Fact]
    public async Task WhatARunLeavesInItsHome_IsRefusedBeforeTheRepairStarts()
    {
        // The first launch answers nothing the parser reads and leaves a hooks.toml in its home.
        Steer("not the schema's JSON", homeWrite: "hooks.toml");
        var first = Invocation();

        var outcome = await new ReviewerExecutor(_launcher).RunAsync(first, first, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("hooks.toml").And.Contain("delete");
    }

    // ------------------------------------------------------------------------------------------------- a real round

    private const string Scope = """
        # SCOPE

        A Vibe reviewer must review a change in a sanitised copy of the tree, never in the checkout, and the copy must
        be gone when the round ends. When it is done: the round counts its answer, and nothing of it is left in temp.
        """;

    private async Task<(string Repo, string Data)> RepositoryAsync()
    {
        var repo = Directory.CreateDirectory(_dir.At("repo")).FullName;
        var data = Directory.CreateDirectory(_dir.At("data")).FullName;
        await Git(repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), "v1\n", TestContext.Current.CancellationToken);
        await Git(repo, "add", ".");
        await Git(repo, "commit", "-m", "base");
        await Git(repo, "checkout", "-b", "feature");
        Directory.CreateDirectory(Path.Combine(repo, ".vibe"));
        await File.WriteAllTextAsync(Path.Combine(repo, ".vibe", "hooks.toml"), "[[hooks]]\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 40)), TestContext.Current.CancellationToken);
        await Git(repo, "add", ".");
        await Git(repo, "commit", "-m", "the feature");

        return (repo, data);
    }

    private async Task Git(string repo, params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service(string data)
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry(RoleCatalog.ConventionsRole, Active: false),
            new RoleEntry(RoleCatalog.SecurityRole, Active: false),
            new RoleEntry(RoleCatalog.UxDxRole, Active: false),
        ]);

        return new PanelService(
            new PanelSettings
            {
                Providers = [new("vibe") { Runtime = "vibe", ExecutablePath = FakeCliExe, Model = "glm-5-3" }],
                Rounds = new PanelConfig(catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(1, 5)), StagePolicy.Human) { Catalog = catalog },
                DataDir = data,
                CodeWorkspace = "worktree",
                ReviewerTimeout = TimeSpan.FromSeconds(60),
            },
            VaultKeys.None("no vault in tests"), default, _launcher, Logger.None, Noticing.None);
    }

    [Fact]
    public async Task ACodeRound_StartsTheVibeReviewerInTheSnapshot_AndTheSnapshotIsGoneWithTheRound()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("D18: Vibe is refused on every host but Windows — VibeLaunchRulesTests pins that refusal");
        }

        var (repo, data) = await RepositoryAsync();
        var record = Directory.CreateDirectory(_dir.At("argv")).FullName;
        Steer(Clean, recordDir: record);
        var service = Service(data);
        await service.OpenAsync(repo, "feature");
        var planned = await service.ReviewPlanAsync(repo, "feature", Scope);
        await service.ResolveAsync(repo, "feature", "[]");

        var answered = await service.ReviewCodeAsync(repo, "feature", "main", Scope);
        var code = JsonDocument.Parse(answered).RootElement;

        code.TryGetProperty("reviewers", out var reviewers).Should().BeTrue($"the code round ran — plan: {planned}; code: {answered}");
        reviewers.GetString().Should().NotContain("FAILED").And.NotContain("not started");
        var workdirs = Directory.GetFiles(record, "*.argv").Select(LaunchRecords.Read).Select(argv => argv.Split('\0'))
            .Select(argv => argv[System.Array.IndexOf(argv, "--workdir") + 1]).ToList();
        var codeLaunch = workdirs.Should().ContainSingle(dir => Path.GetFileName(dir).StartsWith(Runners.Snapshots.ReviewSnapshots.Prefix, StringComparison.Ordinal),
            "the code round's Vibe reviewer started in the round's sanitised snapshot").Subject;
        codeLaunch.Should().NotStartWith(repo);
        Directory.Exists(codeLaunch).Should().BeFalse("the snapshot went with the round");
        Directory.Exists(Path.GetDirectoryName(codeLaunch)).Should().BeFalse("and so did the round's scratch, the Vibe homes in it");
    }

    /// <summary>
    /// The owner's decision of 2026-10-13: a Vibe version nobody measured is RUN, and the round's record of the reviewer says
    /// so — <c>reviewerStates[].warning</c>, what the rounds view reads — beside an answer counted as any other.
    /// </summary>
    [Fact]
    public async Task APlanRoundOnAnUnmeasuredVibe_RunsTheReviewer_AndItsStateCarriesTheWarning()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("D18: Vibe is refused on every host but Windows — VibeLaunchRulesTests pins that refusal");
        }

        var (repo, data) = await RepositoryAsync();
        Steer(Clean, version: "2.27.0");
        var service = Service(data);
        await service.OpenAsync(repo, "feature");

        var planned = JsonDocument.Parse(await service.ReviewPlanAsync(repo, "feature", Scope)).RootElement;
        var status = JsonDocument.Parse(await service.StatusAsync(repo, "feature")).RootElement;

        planned.GetProperty("verdict").GetString().Should().Be("proceed", $"the unmeasured Vibe answered: {planned}");
        planned.GetProperty("reviewers").GetString().Should().Contain("warned — vibe/PlanCritique").And.Contain("2.27.0",
            "the round's reviewer line, what the calling AI reads, says it too");
        var reviewer = status.GetProperty("rounds")[0].GetProperty("reviewerStates")[0];
        reviewer.GetProperty("status").GetString().Should().Be("done");
        reviewer.TryGetProperty("warning", out var warning).Should().BeTrue($"the reviewer's state carries the warning: {reviewer}");
        warning.GetString().Should().Contain("2.27.0").And.Contain("2.26.1");
    }
}

using System.Text.Json;
using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// The split order through the whole server: given once, on a plan that passed, to a caller that
/// has not had one — and never twice.
/// </summary>
/// <remarks>
/// <para>The unit tests say what <see cref="Core.Commands.GateCommands"/> returns for a context.
/// This says what a real <c>review_plan</c> call actually puts in its answer, which is the thing an
/// AI reads. Both of the operator's questions are here as tests:</para>
/// <list type="number">
/// <item>with the box unticked, nothing goes; with it ticked, the order goes after the plan
/// passes — and NOT after a plan that was told to revise;</item>
/// <item>the epics that split produces come back for their own plan review, on their own branches,
/// and are told they are pieces rather than told to split again. Without that the process has no
/// floor: epics of epics, for ever.</item>
/// </list>
/// </remarks>
[Collection("fakecli-env")]
public sealed class SplitOrderTests : IAsyncLifetime
{
    private const string Clean = """{"findings": []}""";

    private const string ThreeMajors = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"},
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 40,
           "title": "no timeout", "why": "a hung peer hangs the request", "fix": "add one"},
          {"severity": "major", "category": "architecture", "file": "app.cs", "line": 70,
           "title": "layers cross", "why": "the parser reaches into the transport", "fix": "invert it"}
        ]}
        """;

    /// <summary>Big and broad on both axes, so the verdict is EPICS and the wording is unambiguous.</summary>
    private static readonly string BigPlan =
        "# PLAN — something large\n\n## Build order\n\n"
        + string.Join("\n", Enumerable.Range(1, 8).Select(i => $"{i}. step {i}"))
        + "\n\n"
        + string.Join("\n", Enumerable.Range(0, 16).Select(i => $"- `src_mcp/a{i}.cs` and `src_vs_code/b{i}.ts`"))
        + "\n\n- under `.github/` and `research/` and `prompts/`\n"
        + string.Join("\n", Enumerable.Range(0, 400).Select(i => $"prose line {i}"));

    private readonly ProcessLauncher _launcher = new();
    private readonly string _caller = "test-caller-" + Guid.NewGuid().ToString("N")[..8];
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-split-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-split-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await Git("checkout", "-b", "epic-1");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        // The caller Claude Code would have exported for us. A fresh one per test class, so a
        // developer's own session id can never make these pass or fail.
        Environment.SetEnvironmentVariable("COAI_CALLER_SESSION", _caller);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "COAI_CALLER_SESSION"])
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

    private void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service(bool splitPlan) =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 2),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
                SplitPlan = splitPlan,
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string[] CommandsOf(JsonElement answer) =>
        answer.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Array
            ? [.. commands.EnumerateArray().Select(c => c.GetString() ?? string.Empty)]
            : [];

    private async Task<JsonElement> PlanRound(PanelService service, string branch, string plan)
    {
        await service.OpenAsync(_repo, branch);
        Script(Clean);
        return Parse(await service.ReviewPlanAsync(_repo, branch, plan));
    }

    [Fact]
    public async Task WithTheBoxUnticked_NoCommandArrivesAtAll()
    {
        var answer = await PlanRound(Service(splitPlan: false), "feature", BigPlan);

        answer.GetProperty("verdict").GetString().Should().Be("proceed");
        CommandsOf(answer).Should().BeEmpty("a switch nobody set changes nothing");
        var hasPreamble = answer.TryGetProperty("commandsPreamble", out var preamble)
            && preamble.ValueKind != JsonValueKind.Null;
        hasPreamble.Should().BeFalse("an introduction to nothing is still something the AI has to read");
    }

    [Fact]
    public async Task WithTheBoxTicked_TheOrderArrivesOnThePlanThatPassed()
    {
        var answer = await PlanRound(Service(splitPlan: true), "feature", BigPlan);

        var commands = CommandsOf(answer);
        commands.Should().ContainSingle();
        commands[0].Should().Contain("EPICS").And.Contain("review_code").And.Contain("commit");
        answer.GetProperty("commandsPreamble").GetString().Should().Contain("outrank");
    }

    [Fact]
    public async Task APlanTheGateSentBack_IsNotToldToStartBuildingIt()
    {
        // Three majors against a threshold of two: `revise`. The order to split and commit follows
        // permission to build, and this plan has not got it.
        var service = Service(splitPlan: true);
        await service.OpenAsync(_repo, "feature");
        Script(ThreeMajors);
        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", BigPlan));

        answer.GetProperty("verdict").GetString().Should().Be("revise");
        CommandsOf(answer).Should().BeEmpty();
    }

    [Fact]
    public async Task AnEpicOnItsOwnBranch_IsToldItIsAPiece_NotToSplitAgain()
    {
        // The operator's second question, end to end. The epic is a NEW session — our own plan
        // stage happens once per session, so the epic cannot come back on the same one — and that
        // is precisely why the memory is keyed by the CALLER and not by the session.
        var service = Service(splitPlan: true);
        CommandsOf(await PlanRound(service, "feature", BigPlan))[0].Should().Contain("EPICS");

        var epic = await PlanRound(service, "epic-1", BigPlan);

        var commands = CommandsOf(epic);
        commands.Should().ContainSingle();
        commands[0].Should().Contain("do NOT split it again");
        commands[0].Should().NotContain("EPICS", "the loop has to have a floor");
    }

    [Fact]
    public async Task ASecondServerProcess_RemembersTheFirstOnesOrder()
    {
        // The client respawns the server between calls in real use, so a memory in a field would
        // forget exactly when the epics start arriving.
        CommandsOf(await PlanRound(Service(splitPlan: true), "feature", BigPlan))[0].Should().Contain("EPICS");

        var epic = await PlanRound(Service(splitPlan: true), "epic-1", BigPlan);

        CommandsOf(epic)[0].Should().Contain("do NOT split it again");
    }

    [Fact]
    public async Task ADifferentClaude_IsOwedItsOwnSplitOrder()
    {
        // Two people working in one repository at once is the ordinary case, and the second one's
        // plan is not a piece of the first one's split.
        CommandsOf(await PlanRound(Service(splitPlan: true), "feature", BigPlan))[0].Should().Contain("EPICS");

        Environment.SetEnvironmentVariable("COAI_CALLER_SESSION", _caller + "-other");
        var theirs = await PlanRound(Service(splitPlan: true), "epic-1", BigPlan);

        CommandsOf(theirs)[0].Should().Contain("EPICS");
    }
}

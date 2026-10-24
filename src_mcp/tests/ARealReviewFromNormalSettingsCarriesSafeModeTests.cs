using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The scenario that was missing when <c>--safe-mode</c> was first built as an argv flag alone: a REAL review round, started from ORDINARY settings
/// (a claude row with its executable and nothing else), reaches the claude reviewer's launch with <c>--safe-mode</c> in the argv the child was STARTED
/// with — because the stage asks the installed CLI's help before it builds the roster, every round, and the roster copies the answer onto the row's
/// settings (todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md, E5.S2; cadence consultation 5119c081: the reviewer paths did not probe at all).
/// </summary>
/// <remarks>
/// <para>Against the fake CLI, never a vendor's: its <c>--help</c> prints the REAL 2.1.258 help (declaring the flag) or a help without it, and each
/// launch's argv and environment names are recorded by the child — what crossed the process boundary, not what a dictionary said.</para>
/// <para>The Team server's twin lives in its own job test (<c>ReviewLauncherTests</c>).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ARealReviewFromNormalSettingsCarriesSafeModeTests : IAsyncLifetime
{
    private const string Envelope = """{"type":"result","subtype":"success","is_error":false,"result":"{\"findings\":[]}"}""";

    private const string Purpose = """
        # What this document is for

        It is the requirements for a payments integration, written for the two engineers who will build it. After
        reading it they must be able to start without asking anybody a question.
        """;

    private const string Scope = """
        # SCOPE

        The claude reviewer is not steered by the branch it reviews: its launch carries --safe-mode whenever the installed claude
        declares it. Done when every review launch of a round says so in the argv the child was started with.
        """;

    private const string HelpWithoutTheFlag = "Usage: claude [options]\n\nOptions:\n  -p, --print                           Print response and exit\n  --strict-mcp-config                   Only use MCP servers\n";

    private static readonly string[] Steering =
        ["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_RECORD_DIR", "FAKECLI_HELP_STDOUT", "FAKECLI_RECORD_ENV", "CLAUDE_CODE_SAFE_MODE", "FAKECLI_VERSION_STDOUT"];

    private readonly ProcessLauncher _launcher = new();
    private readonly string _repo = Directory.CreateTempSubdirectory("testsToDelete-safemode-repo-").FullName;
    private readonly string _data = Directory.CreateTempSubdirectory("testsToDelete-safemode-data-").FullName;
    private readonly string _record = Directory.CreateTempSubdirectory("testsToDelete-safemode-record-").FullName;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private static string Help(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", name));

    public async ValueTask InitializeAsync()
    {
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Query.cs"), "class Query { int F() => 1; }\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Query.cs"), "class Query { int F(string v) => database.Query(v); }\n");
        await Git("add", ".");
        await Git("commit", "-m", "change");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", Envelope);
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", Help("help-2.1.258-windows.txt"));
        // The release the effect was OBSERVED on (ClaudeSafeMode.ObservedVersions): the help fixture is an older release's, the version is a separate answer.
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "2.1.309 (Claude Code)\n");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in Steering)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data, _record])
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

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>ORDINARY settings: a claude row with its executable — no capability, no tier, no safe-mode field anywhere.</summary>
    private PanelService Service(string row = """[{"id":"claude","runtime":"claude"}]""") =>
        new(
            new PanelSettings
            {
                Providers = [.. PanelSettings.ParseVendors(row).Select(p => p with { ExecutablePath = FakeCliExe })],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"), default, _launcher, Logger.None, Noticing.None);

    /// <summary>Every claude REVIEW launch recorded since the last clear — each one's argv, the stdin field dropped. A <c>--help</c> is no launch.</summary>
    private List<string[]> Launches() =>
        [.. Directory.GetFiles(_record, "*.argv").Select(file => LaunchRecords.Read(file).Split('\0')[..^1]).Where(argv => argv.Contains("-p"))];

    private void Forget()
    {
        foreach (var file in Directory.GetFiles(_record))
        {
            File.Delete(file);
        }
    }

    private static string Answered(string reply)
    {
        var root = JsonDocument.Parse(reply).RootElement;
        root.TryGetProperty("error", out var error).Should().BeFalse($"unexpected refusal: {error}");

        return reply;
    }

    private static void EveryLaunchCarries(List<string[]> launches, string flag, bool carries) =>
        launches.Should().NotBeEmpty().And.OnlyContain(argv => argv.Contains(flag) == carries,
            $"every claude review launch {(carries ? "carries" : "goes without")} {flag}");

    [Fact]
    public async Task APlanReview_FromNormalSettings_CarriesSafeMode()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: true);
    }

    [Fact]
    public async Task ACodeReview_FromNormalSettings_CarriesSafeMode()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        await service.ReviewPlanAsync(_repo, "feature", "the plan");
        await service.ResolveAsync(_repo, "feature", "[]");
        Forget();

        Answered(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: true);
    }

    [Fact]
    public async Task ADocumentReview_FromNormalSettings_CarriesSafeMode()
    {
        var service = Service();
        await service.OpenAsync(_repo, "main");

        Answered(await service.ReviewDocumentAsync(_repo, "main", Purpose, documentText: "# Spec\n\nIt must be fast.\n", documentName: "spec"));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: true);
    }

    [Fact]
    public async Task ASwappedCliBetweenRounds_IsReadAgain()
    {
        // No cache, not even across two rounds of one server (risk consultation 958e430d): an in-place upgrade or downgrade is seen by asking.
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));
        EveryLaunchCarries(Launches(), "--safe-mode", carries: true);
        await service.ResolveAsync(_repo, "feature", "[]");
        Forget();

        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", HelpWithoutTheFlag);
        Answered(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: false);
    }

    [Fact]
    public async Task AClaudeWithoutTheFlag_RunsWithoutIt_AndTheRoundSaysSo()
    {
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", HelpWithoutTheFlag);
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        var reply = Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: false);
        reply.Should().Contain("the installed claude does not declare --safe-mode", "a note, never a refusal — the review ran and answered")
            .And.Contain("CLAUDE.md");
    }

    [Fact]
    public async Task AnObservedRelease_CarriesTheFlag_AndNoNote()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        var reply = Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: true);
        reply.Should().NotContain("observed on claude", "2.1.309 is the release the effect was observed on");
    }

    [Fact]
    public async Task AnUnobservedRelease_CarriesTheFlag_AndTheRoundSaysTheEffectWasNotObserved()
    {
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "2.1.258 (Claude Code)\n");
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        var reply = Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: true);
        reply.Should().Contain("observed on claude 2.1.309 only").And.Contain("not on 2.1.258");
    }

    [Fact]
    public async Task AVersionThatCannotBeRead_CarriesTheFlag_AndTheRoundSaysItIsNotKnown()
    {
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", "no release in this line\n");
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        var reply = Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: true);
        reply.Should().Contain("not observed for it");
    }

    [Fact]
    public async Task AHelpThatNeverAnswers_RunsWithoutTheFlag_AndSaysItCouldNotBeRead()
    {
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", string.Empty);
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        var reply = Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: false);
        reply.Should().Contain("could not be read", "an unreadable help is Unknown, which is not the same as not declared");
    }

    [Fact]
    public async Task ARowThatSwitchedSafeModeOff_IsLaunchedWithoutIt_AndWithoutTheInheritedVariable()
    {
        // The way back must be a way back: the person's shell already says CLAUDE_CODE_SAFE_MODE, and the row says off.
        Environment.SetEnvironmentVariable("CLAUDE_CODE_SAFE_MODE", "1");
        var envRecord = Path.Combine(_record, "env.txt");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_ENV", envRecord);
        var service = Service("""[{"id":"claude","runtime":"claude","safeMode":"off"}]""");
        await service.OpenAsync(_repo, "feature");

        var reply = Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        EveryLaunchCarries(Launches(), "--safe-mode", carries: false);
        File.ReadAllLines(envRecord).Select(line => line.Split('=')[0]).Should().NotContain("CLAUDE_CODE_SAFE_MODE",
            "an Off row removes an inherited CLAUDE_CODE_SAFE_MODE from the child's environment, as the child saw it");
        reply.Should().Contain("safe mode is switched off for this claude row");
    }

    [Fact]
    public async Task ARowOnSafeMode_LeavesTheInheritedVariableToTheCli()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CODE_SAFE_MODE", "1");
        var envRecord = Path.Combine(_record, "env.txt");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_ENV", envRecord);
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        File.ReadAllLines(envRecord).Select(line => line.Split('=')[0]).Should().Contain("CLAUDE_CODE_SAFE_MODE",
            "the control: the recorder does see an inherited name, so its absence above means something");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(",\"safeMode\":\"on\"", true)]
    [InlineData(",\"safeMode\":\"off\"", false)]
    [InlineData(",\"safeMode\":\" OFF \"", false)]
    [InlineData(",\"safeMode\":\"whatever\"", true)]
    public void TheRowsSafeMode_IsParsed_OnUnlessItSaidOff(string field, bool expected)
    {
        PanelSettings.ParseVendors("[{\"id\":\"claude\",\"runtime\":\"claude\"" + field + "}]")[0].SafeMode.Should().Be(expected);
    }

    [Fact]
    public async Task TheRoundAsksEachClaudeExecutableOnce_AndNoOtherRuntime()
    {
        var watched = new WatchedLauncher(_launcher);
        var service = Service("""[{"id":"claude","runtime":"claude"},{"id":"claude-2","runtime":"claude"},{"id":"codex","runtime":"codex"}]""");

        var claude = await service.Roster.ClaudeCapabilitiesAsync(watched, TestContext.Current.CancellationToken);

        watched.Requests.Select(request => request.Arguments.Single()).Should().Equal(["--help", "--version"],
            "two rows on one executable are one installed release, asked its help once and — it declares the flag — its version once; a codex row asks nothing");
        var answer = claude.ByExecutable.Values.Should().ContainSingle().Subject;
        answer.SafeMode.Should().Be(CoaiMcp.Runners.Reviewers.SafeModeSupport.Declared);
        answer.Version.Should().Be(CoaiMcp.Runners.Reviewers.ClaudeRelease.Parse("2.1.309"));
    }

    [Fact]
    public async Task ARowThatSwitchedSafeModeOff_IsNeverAsked()
    {
        var watched = new WatchedLauncher(_launcher);
        var service = Service("""[{"id":"claude","runtime":"claude","safeMode":"off"}]""");

        var claude = await service.Roster.ClaudeCapabilitiesAsync(watched, TestContext.Current.CancellationToken);

        watched.Requests.Should().BeEmpty("an Off row sends nothing whatever the help says — a spawn for an answer nothing reads");
        claude.ByExecutable.Should().BeEmpty();
    }
}

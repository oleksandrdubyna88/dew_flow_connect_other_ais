using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The claude REVIEWER starts in the CLI's own safe mode — <c>--safe-mode</c>, so the reviewed branch's <c>CLAUDE.md</c> and hooks do not
/// steer it — when the installed CLI declares the flag and the row has not switched it off; otherwise it goes without, with a NOTE and never a
/// refusal (todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md, E5.S2; research/RESULTS_claude_reviewer_safe_mode.md).
/// </summary>
/// <remarks>
/// <para>These assert what is SENT, not what it does: that the effect holds under coai's exact argv is the lead's paid canary run (E5.S4), recorded
/// in the same RESULTS file. The two help fixtures are the REAL help texts of claude 2.1.258 (Windows) and 2.1.197 (WSL), both of which declare the
/// flag (<c>--safe-mode</c> at lines 198 and 166).</para>
/// <para>The consultants are NOT this story's: their argv carries no <c>--safe-mode</c>, and a help that did not come back still REFUSES their turn.</para>
/// </remarks>
public sealed class TheClaudeReviewerRunsInSafeModeTests
{
    private const string Repo = "D:/rsd/some-checkout";

    private static string Help(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", name));

    /// <summary>A help that came back, whole, without the flag — and without <c>--restricted</c> too.</summary>
    private const string HelpWithoutTheFlag = "Usage: claude [options]\n\nOptions:\n  -p, --print                           Print response and exit\n  --strict-mcp-config                   Only use MCP servers from --mcp-config\n";

    private static async Task<ClaudeCapability> Probed(ProcessResult help) =>
        await ClaudeCapability.ProbeAsync(new Printing(help), "claude", Path.GetTempPath(), TestContext.Current.CancellationToken);

    private static Task<ClaudeCapability> ProbedHelp(string text) => Probed(new ProcessResult(0, text, string.Empty, TimedOut: false));

    private static ReviewerInvocation Reviewer(ClaudeCapability cli, bool safeMode = true) =>
        new ClaudeRuntime().Build(RoleCatalog.ArchitectureRole, "review this", Repo, "D:/s.json", "D:/out",
            new ReviewerSettings("claude") { ClaudeCli = cli, SafeMode = safeMode });

    private static IReadOnlyList<string> Argv(ReviewerInvocation invocation) => invocation.Request.Arguments;

    private sealed class Printing(ProcessResult result) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => Task.FromResult(result);
    }

    private sealed class Recording(ProcessResult result) : IProcessLauncher
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            return Task.FromResult(result);
        }
    }

    // ---------- sent when declared ----------

    /// <summary>The release the effect was observed on (research/RESULTS_claude_reviewer_safe_mode.md §5) — the fixtures' help texts are older releases', so the version is named here.</summary>
    private const string ObservedRelease = "2.1.309";

    private static async Task<ClaudeCapability> ProbedOn(string help, string version) => (await ProbedHelp(help)) with { Version = version };

    [Fact]
    public async Task WhenHelpDeclaresSafeMode_TheArgvCarriesIt()
    {
        var launch = Reviewer(await ProbedOn(Help("help-2.1.258-windows.txt"), ObservedRelease));

        var argv = Argv(launch);
        argv.Should().Contain("--safe-mode");
        argv.ToList().IndexOf("--safe-mode").Should().BeLessThan(argv.ToList().IndexOf("--permission-mode"),
            "the flag rides with the CLI's own switches, before the permission mode the review is confined by");
        launch.Note.Should().BeEmpty("a launch that sent the flag, on a release where its effect was observed, has nothing to apologise for");
    }

    [Fact]
    public async Task WhenHelpDeclaresIt_OnTheOlderCli_Too()
    {
        var launch = Reviewer(await ProbedOn(Help("help-2.1.197-wsl.txt"), "2.1.197"));

        Argv(launch).Should().Contain("--safe-mode", "2.1.197 declares it at line 166, though it has no --restricted");
    }

    // ---------- sent, but is the effect observed on this release? ----------

    [Fact]
    public async Task ADeclaredFlagOnAnUnobservedVersion_IsSentWithANote()
    {
        var launch = Reviewer(await ProbedOn(Help("help-2.1.258-windows.txt"), "2.1.258"));

        Argv(launch).Should().Contain("--safe-mode", "never a refusal: the flag is still sent");
        launch.Note.Should().Contain("--safe-mode is sent").And.Contain("observed on claude 2.1.309 only").And.Contain("not on 2.1.258")
            .And.Contain("CLAUDE.md").And.Contain("hooks");
        (await new ClaudeRuntime().TakeTurnAsync(launch, new Printing(default!), TestContext.Current.CancellationToken)).Refusal.Should().BeEmpty();
    }

    [Fact]
    public async Task TheObservedVersion_CarriesNoNote()
    {
        var launch = Reviewer(await ProbedOn(Help("help-2.1.258-windows.txt"), ObservedRelease));

        Argv(launch).Should().Contain("--safe-mode");
        launch.Note.Should().BeEmpty();
        ClaudeSafeMode.ObservedVersions.Should().Contain(ObservedRelease, "the list is the one place the observed releases live");
    }

    [Fact]
    public async Task AnUnknownVersion_SaysTheEffectIsNotObserved()
    {
        var launch = Reviewer(await ProbedOn(Help("help-2.1.258-windows.txt"), string.Empty));

        Argv(launch).Should().Contain("--safe-mode", "an unknown release is a note, never a refusal");
        launch.Note.Should().Contain("--safe-mode is sent").And.Contain("observed on claude 2.1.309 only").And.Contain("not known")
            .And.Contain("not observed for it");
    }

    [Theory]
    [InlineData("2.1.309 (Claude Code)\n", "2.1.309")]
    [InlineData("2.1.309", "2.1.309")]
    [InlineData("claude 2.1.309 (Claude Code)\r\n", "2.1.309")]
    [InlineData("", "")]
    [InlineData("no release here", "")]
    [InlineData("wrapper 1.0.0\n2.1.309 (Claude Code)\n", "")]
    [InlineData("2.1.309.4", "")]
    public void TheVersionIsReadFromWhatTheCliPrinted_AndOnlyWhenItNamesOne(string printed, string expected)
    {
        ClaudeCapability.VersionIn(printed).Should().Be(expected);
    }

    [Fact]
    public async Task TheVersionIsAskedOnce_AndOnlyWhenTheHelpDeclaredTheFlag()
    {
        var asked = new Recording(new ProcessResult(0, "2.1.309 (Claude Code)\n", string.Empty, TimedOut: false));
        var declared = await ProbedHelp(Help("help-2.1.258-windows.txt"));
        var absent = await ProbedHelp(HelpWithoutTheFlag);

        var withVersion = await declared.WithVersionAsync(asked, "claude", Path.GetTempPath(), TestContext.Current.CancellationToken);
        var skipped = await absent.WithVersionAsync(asked, "claude", Path.GetTempPath(), TestContext.Current.CancellationToken);

        withVersion.Version.Should().Be("2.1.309");
        skipped.Version.Should().BeEmpty("nothing else reads the release: no spawn for a CLI without the flag");
        asked.Requests.Should().ContainSingle().Which.Arguments.Should().Equal("--version");
    }

    [Fact]
    public async Task AVersionThatDidNotAnswer_LeavesItEmpty()
    {
        var declared = await ProbedHelp(Help("help-2.1.258-windows.txt"));

        foreach (var unreadable in (ProcessResult[])[
            new(1, "2.1.309", string.Empty, TimedOut: false),
            new(0, string.Empty, string.Empty, TimedOut: true),
            new(0, "banner", string.Empty, TimedOut: false)])
        {
            (await declared.WithVersionAsync(new Printing(unreadable), "claude", Path.GetTempPath(), TestContext.Current.CancellationToken))
                .Version.Should().BeEmpty();
        }
    }

    // ---------- absent: a note, never a refusal ----------

    [Fact]
    public async Task WhenHelpDoesNotDeclareIt_TheArgvGoesWithout_AndTheLaunchCarriesANote()
    {
        var cli = await ProbedHelp(HelpWithoutTheFlag);
        var launch = Reviewer(cli);

        cli.SafeMode.Should().Be(SafeModeSupport.NotDeclared);
        Argv(launch).Should().NotContain("--safe-mode");
        launch.Note.Should().Contain("the installed claude does not declare --safe-mode")
            .And.Contain("CLAUDE.md").And.Contain("hooks")
            .And.NotContain("MCP", "--strict-mcp-config is already sent, so the note must not claim MCP servers reach this reviewer");

        var turn = await new ClaudeRuntime().TakeTurnAsync(launch, new Printing(default!), TestContext.Current.CancellationToken);
        turn.Refusal.Should().BeEmpty("a missing flag is a note, never a refusal");
        turn.Warning.Should().Be(launch.Note, "the note reaches the round through the launch control's warning");
    }

    [Fact]
    public async Task AnUnreadableHelp_IsUnknown_AndSaysSo()
    {
        // A help that timed out, was cut or failed says nothing about the flag: NOT "does not declare".
        foreach (var unreadable in (ProcessResult[])[
            new(1, "x", string.Empty, TimedOut: false),
            new(0, string.Empty, string.Empty, TimedOut: true),
            new(0, string.Empty, string.Empty, TimedOut: false),
            new(0, "Options:", string.Empty, TimedOut: false, Truncated: true)])
        {
            var cli = await Probed(unreadable);
            var launch = Reviewer(cli);

            cli.SafeMode.Should().Be(SafeModeSupport.Unknown);
            Argv(launch).Should().NotContain("--safe-mode");
            launch.Note.Should().Contain("could not be read").And.NotContain("does not declare");
        }
    }

    [Theory]
    [InlineData("Options:\n  --safe-mode <mode>           Safe mode\n")]
    [InlineData("Options:\n  --safe-mode=<mode>           Safe mode\n")]
    public async Task ADeclarationWithARequiredValue_IsNotSupport(string help)
    {
        // The `--restricted` rule, applied to the new flag: a value-taking --safe-mode would swallow the next argument.
        ClaudeCapability.SafeModeIn(help).Should().BeFalse();
        Argv(Reviewer(await ProbedHelp(help))).Should().NotContain("--safe-mode");
        ClaudeCapability.SafeModeIn("Options:\n  --safe-mode [mode]           Safe mode\n").Should().BeTrue("an OPTIONAL value is not taken from a following option");
    }

    [Fact]
    public void AMentionInProse_IsNotADeclaration()
    {
        ClaudeCapability.SafeModeIn("Options:\n  --bare                  Minimal mode; unlike --safe-mode it skips keychain reads\n").Should().BeFalse();
        ClaudeCapability.SafeModeIn("Notes:\n  --safe-mode is the default from 3.0 on\n").Should().BeFalse();
    }

    // ---------- the way back ----------

    [Fact]
    public async Task TheSafeModeSettingOff_DropsTheFlag_AndSaysSo()
    {
        var cli = await ProbedHelp(Help("help-2.1.258-windows.txt"));

        var off = Reviewer(cli, safeMode: false);

        Argv(off).Should().NotContain("--safe-mode", "the row switched it off, whatever the CLI declares");
        off.Note.Should().Contain("switched off").And.Contain("CLAUDE.md").And.Contain("hooks");
        off.Request.Environment.Should().ContainKey("CLAUDE_CODE_SAFE_MODE").WhoseValue.Should().BeNull(
            "off is the way back: an inherited CLAUDE_CODE_SAFE_MODE is removed from the child's environment too");
        Reviewer(cli).Request.Environment.Should().NotContainKey("CLAUDE_CODE_SAFE_MODE", "a launch that sends the flag leaves the variable to the CLI");
    }

    [Fact]
    public void TheRowsApiKeyIsStillApplied_BesideTheVariableRemoval()
    {
        var launch = new ClaudeRuntime().Build(RoleCatalog.ArchitectureRole, "r", Repo, "D:/s.json", "D:/out",
            new ReviewerSettings("claude") { ApiKey = "k", SafeMode = false });

        launch.Request.Environment["ANTHROPIC_API_KEY"].Should().Be("k");
        launch.Request.Environment.Should().ContainKey("CLAUDE_CODE_SAFE_MODE");
    }

    [Fact]
    public void ARowIsInSafeModeUnlessItSaidOff()
    {
        new ReviewerSettings("claude").SafeMode.Should().BeTrue("the safe default");
    }

    [Fact]
    public async Task ALaunchNobodyProbed_GoesWithoutTheFlag_AndSaysItWasNeverAsked()
    {
        var launch = Reviewer(ClaudeCapability.Unprobed);

        Argv(launch).Should().NotContain("--safe-mode");
        launch.Note.Should().Contain("was not asked").And.Contain("CLAUDE.md");
        (await new ClaudeRuntime().TakeTurnAsync(launch, new Printing(default!), TestContext.Current.CancellationToken)).Refusal.Should().BeEmpty();
    }

    // ---------- never --bare ----------

    [Fact]
    public async Task BareIsNeverSent()
    {
        // --bare skips the keychain read, which is where the owner's browser sign-in lives: a launch that cannot sign in.
        var declared = await ProbedHelp(Help("help-2.1.258-windows.txt"));
        var absent = await ProbedHelp(HelpWithoutTheFlag);
        var unknown = await Probed(new ProcessResult(1, string.Empty, string.Empty, TimedOut: false));

        foreach (var cli in (ClaudeCapability[])[declared, absent, unknown, ClaudeCapability.Unprobed])
        {
            foreach (var safeMode in (bool[])[true, false])
            {
                Argv(Reviewer(cli, safeMode)).Should().NotContain("--bare", $"{cli.SafeMode}, safe mode {safeMode}");
            }
        }

        ClaudeCapability.SafeModeFlag.Should().NotBe("--bare");
    }

    // ---------- the consultants are not touched ----------

    [Fact]
    public async Task TheConsultantsArgvIsUnchanged()
    {
        var cli = await ProbedHelp(Help("help-2.1.258-windows.txt"));
        cli.SafeMode.Should().Be(SafeModeSupport.Declared, "the capability knows it — and the consultants must still not send it");
        var consultant = new ClaudeConsultant(new ClaudeRuntime());

        var fresh = consultant.Build(new ConsultantLaunch(Repo, "help me", string.Empty, "D:/answers", new ReviewerSettings("claude") { ClaudeCli = cli }));
        var resumed = consultant.Build(new ConsultantLaunch(Repo, "help me", "67289235-65f7-40b5-9532-e63515d90f30", "D:/answers", new ReviewerSettings("claude") { ClaudeCli = cli }));
        var question = consultant.Build(new ConsultantLaunch(Repo, "q", string.Empty, "D:/answers", new ReviewerSettings("claude") { ClaudeCli = cli })
        {
            Confinement = new LaunchConfinement.Planned((Core.QuestionConsult.Confinement.Planned)Core.QuestionConsult.ConfinementPlanner.Plan("claude", Core.QuestionConsult.CapabilityGrant.None)),
            ScratchDir = Path.GetTempPath(),
        });

        fresh.Request.Arguments.Should().Equal(
            "-p", "--output-format", "json", "--permission-mode", "plan", "--restricted", "--tools", "Read,Glob,Grep", "--strict-mcp-config", "--add-dir", Repo);
        resumed.Request.Arguments.Should().Contain("--resume").And.NotContain("--safe-mode");
        question.Request.Arguments.Should().NotContain("--safe-mode");
        fresh.Note.Should().BeEmpty("a consultant carries no safe-mode note");
        fresh.Request.Environment.Should().NotContainKey("CLAUDE_CODE_SAFE_MODE");
    }

    [Fact]
    public async Task TheConsultantsUnknownStaysARefusal()
    {
        // The reviewer's Unknown is a note; the consultant's is STILL a refusal (the RestrictedSupport rule): the two must not drift together.
        var broken = new Printing(new ProcessResult(1, string.Empty, string.Empty, TimedOut: false));
        var consultant = new ClaudeConsultant(new ClaudeRuntime());
        var settings = new ReviewerSettings("claude");

        foreach (var handle in (string[])[string.Empty, "67289235-65f7-40b5-9532-e63515d90f30"])
        {
            var prepared = await consultant.PrepareAsync(new ConsultantLaunch(Repo, "help me", handle, "D:/answers", settings), broken, TestContext.Current.CancellationToken);

            prepared.Should().BeOfType<ConsultantPreparation.Refused>(handle.Length == 0 ? "fresh" : "resumed")
                .Which.Failure.Should().BeOfType<ConsultFailure.VendorRefused>();
        }

        var question = await consultant.PrepareAsync(new ConsultantLaunch(Repo, "q", string.Empty, "D:/answers", settings)
        {
            Confinement = new LaunchConfinement.Planned((Core.QuestionConsult.Confinement.Planned)Core.QuestionConsult.ConfinementPlanner.Plan("claude", Core.QuestionConsult.CapabilityGrant.None)),
        }, broken, TestContext.Current.CancellationToken);
        question.Should().BeOfType<ConsultantPreparation.Refused>("a question consultation is refused on an unreadable help too");
    }
}

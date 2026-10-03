using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the installed claude says about <c>--restricted</c> is read off its own <c>--help</c>, as an OPTION, on every
/// call — and the three answers are kept apart: it declares the flag, it does not, or we could not tell.
/// </summary>
/// <remarks>
/// <para>Why it is asked at all (research/RESULTS_claude_consultant_confinement.md, 2026-10-02): claude 2.1.258 on
/// Windows is confined by <c>--restricted</c> 9 of 9, and claude 2.1.197 in WSL refuses the launch outright with
/// <c>error: unknown option '--restricted'</c>. One argv cannot serve both.</para>
/// <para>Why three answers and not two (epic 3's code round, security): the first version read every failure to get
/// the help — a timeout, an exit code, a CLI that would not start — as "not declared", which LAUNCHES the consultant
/// without <c>--restricted</c>. A hung help on a claude that has the flag then ran a consultant that can read outside
/// the repository. "Could not tell" is now its own answer, asked once more, and the turn is refused on it.</para>
/// <para>The two fixtures are the REAL help texts of those two CLIs, captured 2026-10-03 on the measuring machine
/// (<c>fixtures/claude/</c>); the coloured cases are those same texts with ANSI colour put on the option names.</para>
/// </remarks>
public sealed class ClaudeCapabilityTests
{
    private const string Claude = "claude";

    private const string Escape = "\u001b";

    private static string Help(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", name));

    /// <summary>The real help with every long option name coloured, the way a CLI forced into colour prints it.</summary>
    private static string Coloured(string help) =>
        System.Text.RegularExpressions.Regex.Replace(help, "(?m)^(  (?:-\\w, )?)(--[a-z-]+)", $"$1{Escape}[36m$2{Escape}[39m");

    private static ProcessResult Printed(string stdout, int exit = 0, bool timedOut = false) =>
        new(exit, stdout, string.Empty, timedOut);

    private static Task<ClaudeCapability> Probed(IProcessLauncher launcher) =>
        ClaudeCapability.ProbeAsync(launcher, Claude, Path.GetTempPath(), TestContext.Current.CancellationToken);

    /// <summary>A launcher that answers every request from <paramref name="answer"/> and never runs anything.</summary>
    private static WatchedLauncher Answering(Func<ProcessRequest, ProcessResult> answer) =>
        new(new NothingRuns(), request => answer(request));

    /// <summary>A launcher that answers the n-th request with the n-th result.</summary>
    private static WatchedLauncher InTurn(params ProcessResult[] results)
    {
        var next = 0;

        return Answering(_ => results[Math.Min(next++, results.Length - 1)]);
    }

    private sealed class NothingRuns : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("this test runs no process");
    }

    private sealed class Throwing(Exception error) : IProcessLauncher
    {
        public int Asked { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Asked++;
            throw error;
        }
    }

    // ---------- the parse ----------

    [Fact]
    public void TheWindowsClaude_2_1_258_DeclaresRestricted()
    {
        ClaudeCapability.RestrictedIn(Help("help-2.1.258-windows.txt")).Should().BeTrue(
            "its option table carries `  --restricted   Restricted mode: …` at line 180");
    }

    [Fact]
    public void TheWslClaude_2_1_197_DoesNot()
    {
        ClaudeCapability.RestrictedIn(Help("help-2.1.197-wsl.txt")).Should().BeFalse(
            "that CLI refuses the flag: `error: unknown option '--restricted'`, exit 1");
    }

    [Fact]
    public void AColouredHelp_IsReadThroughItsColour()
    {
        // A CLI forced into colour (FORCE_COLOR, a terminal it believes it has) wraps the option name in escapes, and
        // `\e[36m--restricted\e[39m` is not the word `--restricted` — so a coloured 2.1.258 would read as NOT declared.
        var coloured = Coloured(Help("help-2.1.258-windows.txt"));
        coloured.Should().Contain($"{Escape}[36m--restricted{Escape}[39m", "the fixture must actually be coloured");

        ClaudeCapability.RestrictedIn(coloured).Should().BeTrue();
        ClaudeCapability.RestrictedIn(Coloured(Help("help-2.1.197-wsl.txt"))).Should().BeFalse();
    }

    [Fact]
    public void EveryLongNameOnADeclarationLine_IsRead()
    {
        ClaudeCapability.RestrictedIn("Options:\n  --sandbox, --restricted   Restricted mode\n").Should().BeTrue(
            "an alias listed first does not hide the flag after it");
        ClaudeCapability.RestrictedIn("Options:\n  -R, --restricted          Restricted mode\n").Should().BeTrue();
    }

    [Fact]
    public void ARestrictedThatTakesAValue_IsNotTheFlagWeSend()
    {
        // Sent bare before `--tools`, a value-taking --restricted would swallow `--tools` as its value — and the turn
        // would run with the user's whole tool set. So a declaration with a REQUIRED value is not support.
        ClaudeCapability.RestrictedIn("Options:\n  --restricted <mode>       Restricted mode\n").Should().BeFalse();
        ClaudeCapability.RestrictedIn("Options:\n  --restricted=<mode>       Restricted mode\n").Should().BeFalse();
        ClaudeCapability.RestrictedIn("Options:\n  --restricted [mode]       Restricted mode\n").Should().BeTrue(
            "an OPTIONAL value is not taken from a following option");
    }

    [Fact]
    public void AMentionInsideAnotherOptionsDescription_IsNotTheOption()
    {
        // The real 2.1.258 table already has this shape: `--restricted`'s own description wraps onto a line that
        // BEGINS `--tools names them, …` at column 40.
        const string help = """
            Options:
              --tools <tools...>                    Specify the list of available tools. With
                                                    --restricted the file tools are confined.
              --verbose                             Override verbose mode setting
            """;

        ClaudeCapability.RestrictedIn(help).Should().BeFalse("the flag is named in prose, not declared");
    }

    [Fact]
    public void ProseAtAnOptionsIndentation_IsNotADeclaration()
    {
        // The option column format: option tokens (and value placeholders), then two or more spaces, then the
        // description — or option tokens alone, an option header whose description wraps below. A sentence that
        // merely starts with the flag is neither.
        ClaudeCapability.RestrictedIn("Notes:\n  --restricted is the default from 3.0 on, see the docs\n").Should().BeFalse();
        ClaudeCapability.RestrictedIn("Notes:\n  --restricted, when given, confines the tools\n").Should().BeFalse();
    }

    [Fact]
    public void AnOptionHeaderWhoseDescriptionWrapsBelow_IsADeclaration()
    {
        // 2.1.258 prints `  --remote-control-session-name-prefix <prefix>` alone and its description on the next line.
        ClaudeCapability.RestrictedIn("Options:\n  --restricted\n      Restricted mode: removes the built-in tools\n").Should().BeTrue();
    }

    [Fact]
    public void AnOptionWhoseNameMerelyStartsTheSame_IsNotIt()
    {
        ClaudeCapability.RestrictedIn("Options:\n  --restricted-mode <m>   something else\n").Should().BeFalse();
        ClaudeCapability.RestrictedIn("Options:\n  --restrictedish        something else\n").Should().BeFalse();
    }

    [Fact]
    public void ACarriageReturn_StillReadsAsTheOption()
    {
        ClaudeCapability.RestrictedIn("Options:\r\n  --restricted          Restricted mode\r\n").Should().BeTrue();
    }

    [Fact]
    public void NothingAtAll_IsNotADeclaration()
    {
        ClaudeCapability.RestrictedIn(string.Empty).Should().BeFalse();
        ClaudeCapability.RestrictedIn("   \n\n").Should().BeFalse();
    }

    // ---------- the probe: three answers ----------

    [Fact]
    public async Task AHelpThatDeclaresIt_IsDeclared_AskedOnce_WithHelpAlone_UnderAShortCeiling()
    {
        var launcher = Answering(_ => Printed(Help("help-2.1.258-windows.txt")));

        (await Probed(launcher)).Support.Should().Be(RestrictedSupport.Declared);

        var asked = launcher.Requests.Should().ContainSingle().Subject;
        asked.Executable.Should().Be(Claude);
        asked.Arguments.Should().Equal(["--help"], "nothing but the help is asked: no prompt, no model, no bill");
        asked.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(10),
            "measured 0.26–0.86 s on Windows and 0.35–0.49 s in WSL; a CLI that needs longer is not answering");
    }

    [Fact]
    public async Task AHelpThatCameBackWithoutIt_IsNotDeclared_AndIsNotAskedAgain()
    {
        var launcher = Answering(_ => Printed(Help("help-2.1.197-wsl.txt")));

        var cli = await Probed(launcher);

        cli.Support.Should().Be(RestrictedSupport.NotDeclared, "the help answered, and it does not list the flag");
        cli.Qualifier.Should().Be("no-restricted");
        launcher.Requests.Should().ContainSingle("an answer is an answer — only 'could not tell' is asked again");
    }

    [Theory]
    [InlineData(1, false, "help", "exited 1")]
    [InlineData(0, true, "help", "did not answer")]
    [InlineData(0, false, "", "printed nothing")]
    public async Task AHelpThatDidNotComeBack_IsUNKNOWN_AfterOneMoreAsk_NeverNotDeclared(int exit, bool timedOut, string stdout, string reason)
    {
        var help = stdout.Length > 0 ? Help("help-2.1.258-windows.txt") : string.Empty;
        var launcher = Answering(_ => Printed(help, exit, timedOut));

        var cli = await Probed(launcher);

        cli.Support.Should().Be(RestrictedSupport.Unknown,
            "a failed --help says nothing about the flag — reading it as 'not declared' launched an unconfined consultant");
        cli.Reason.Should().Contain(reason);
        cli.Qualifier.Should().BeEmpty("an unknown capability selects no claude row");
        launcher.Requests.Should().HaveCount(2, "asked once more, then given up on");
    }

    [Fact]
    public async Task AClaudeThatCannotBeStarted_IsUnknown_NotACrash()
    {
        var missing = new Throwing(new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified"));

        var cli = await Probed(missing);

        cli.Support.Should().Be(RestrictedSupport.Unknown);
        cli.Reason.Should().Contain("could not be started");
        missing.Asked.Should().Be(2);
    }

    [Fact]
    public async Task AHelpThatWasCutShort_IsUnknown_NeverNotDeclared()
    {
        // The whole-branch review, L1: a help truncated by the launcher's ceiling is a head without the rest of the
        // table — a --restricted line further down is simply not in it, and "not declared" would launch unconfined.
        var head = string.Join('\n', Help("help-2.1.258-windows.txt").Split('\n').Take(20));
        var launcher = Answering(_ => new ProcessResult(0, head, string.Empty, TimedOut: false, Truncated: true));

        var cli = await Probed(launcher);

        cli.Support.Should().Be(RestrictedSupport.Unknown, "a cut-off help says nothing about the lines it lost");
        cli.Reason.Should().Contain("cut short");
    }

    [Fact]
    public async Task AClaudeThatCannotBeStarted_IsRefusedAsCliNotFound_WithTheInstallCure()
    {
        // The whole-branch review, L2: a missing executable is not the vendor refusing anything — the cure is to
        // install the CLI or point the row at it, not "check claude --help".
        var missing = new Throwing(new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified"));
        var launch = new CoaiMcp.Runners.Consultation.ConsultantLaunch(Path.GetTempPath(), "prompt", string.Empty, Path.GetTempPath(), new ReviewerSettings("claude"));

        var prepared = await new CoaiMcp.Runners.Consultation.ClaudeConsultant(new ClaudeRuntime())
            .PrepareAsync(launch, missing, TestContext.Current.CancellationToken);

        var refused = prepared.Should().BeOfType<CoaiMcp.Runners.Consultation.ConsultantPreparation.Refused>().Subject;
        refused.Failure.Should().BeOfType<CoaiMcp.Runners.Consultation.ConsultFailure.CliNotFound>();
        refused.Failure.Cure.Should().Be(VendorDiagnosis.InstallCure("claude"));
    }

    [Fact]
    public async Task AHelpThatAnswersOnTheSecondAsk_IsBelieved()
    {
        var launcher = InTurn(Printed(string.Empty, timedOut: true), Printed(Help("help-2.1.258-windows.txt")));

        (await Probed(launcher)).Support.Should().Be(RestrictedSupport.Declared);
        launcher.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task TheCliIsAskedEveryTime_SoAnUpgradeIsSeenOnTheNextTurn()
    {
        // NO cache (risk consultation 264fbcf2, 2026-10-03): a long-lived server must see the CLI as it is NOW.
        var installed = Help("help-2.1.197-wsl.txt");
        var launcher = Answering(_ => Printed(installed));

        (await Probed(launcher)).Support.Should().Be(RestrictedSupport.NotDeclared, "2.1.197 is installed");
        installed = Help("help-2.1.258-windows.txt");
        (await Probed(launcher)).Support.Should().Be(RestrictedSupport.Declared, "and the next turn sees the upgrade");
        launcher.Requests.Should().HaveCount(2, "one --help per question, never a remembered answer");
    }

    [Fact]
    public void NobodyAsked_IsItsOwnState_AndTheDefault()
    {
        new ReviewerSettings("claude").ClaudeCli.Support.Should().Be(RestrictedSupport.Unprobed,
            "neither real answer is a safe default — the builder refuses this one instead");
        ClaudeCapability.Unprobed.Qualifier.Should().BeEmpty();
    }
}

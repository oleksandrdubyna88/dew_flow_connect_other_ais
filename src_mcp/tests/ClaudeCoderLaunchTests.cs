using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The claude coder's write launch: the measured flag set R11, in the job's worktree, resumable only with a handle bound to that job and that
/// worktree, and no other launch can carry <c>acceptEdits</c> (epoch 3 E2 s2 t3, d1 and d2).
/// </summary>
/// <remarks>
/// <para><b>These assert what is SENT, not what it does.</b> A flag in an argv is a request to the CLI. That <c>--restricted</c> with this tool list
/// stops a write outside the worktree, a shell, a read outside and a write through a symlink is a measurement of claude 2.1.330, fresh and resumed
/// (<c>v2/spikes/RESULTS_coder_write_boundary.md</c>, rows R11 to R11l and the t3 section); a unit test only sees the argv, so the golden below is
/// the whole list, in order, spelled the way that measurement spelled it.</para>
/// <para>The worktree paths are made up and never opened: <c>Build</c> is pure, and <see cref="CoderWorktree"/> (t2) is what makes the folder.</para>
/// </remarks>
public sealed class ClaudeCoderLaunchTests
{
    private const string Session = "67289235-65f7-40b5-9532-e63515d90f30";
    private const string Model = "claude-opus-5";
    private const string Job = "job-a";
    private const string OtherJob = "job-b";

    private static readonly string Root = Path.GetFullPath("coder-root");
    private static readonly string OtherRoot = Path.GetFullPath("moved-coder-root");

    private static string Tree(string job, string? root = null) => Path.Combine(root ?? Root, CoderWorktree.FolderOf(job));

    private static LaunchConfinement.Coder Coder(string job = Job, string? root = null, CoderHandle? resume = null) =>
        new(job, Tree(job, root)) { Resume = resume ?? CoderHandle.None };

    private static ConsultantLaunch Launch(LaunchConfinement.Coder coder, string model = "", ClaudeCapability? cli = null) =>
        new(Path.GetFullPath("main-checkout"), "make the change", string.Empty, Path.GetFullPath("answers"),
            new ReviewerSettings("claude") { Model = model, ClaudeCli = cli ?? ClaudeCapability.WithRestricted })
        {
            Confinement = coder,
        };

    private static ClaudeConsultant Claude() => new(new ClaudeRuntime());

    private static string[] Measured(string tree) =>
    [
        "-p", "--output-format", "json", "--restricted", "--tools", "Read,Edit,Write,Glob,Grep", "--permission-mode", "acceptEdits",
        "--permission-prompts", "none", "--strict-mcp-config", "--add-dir", tree,
    ];

    // ---------- d1: the argv ----------

    [Fact]
    public void AFirstCoderTurn_IsTheMeasuredFlagSetR11_InTheWorktree()
    {
        var launch = Claude().Build(Launch(Coder()));

        launch.Request.Arguments.Should().Equal(Measured(Tree(Job)));
        launch.Request.WorkingDirectory.Should().Be(Tree(Job), "the cwd is the worktree");
        launch.Role.Should().Be(CoderLaunches.Role);
        launch.Request.StdIn.Should().Be("make the change", "the prompt travels on stdin, never in argv");
    }

    [Fact]
    public void TheWorktree_IsTheOnlyAddedDirectory_AndNoMcpServerIsOffered()
    {
        var argv = Claude().Build(Launch(Coder())).Request.Arguments;

        argv.Count(a => a == "--add-dir").Should().Be(1);
        argv.Should().Contain("--strict-mcp-config").And.NotContain("--mcp-config");
        argv.Should().NotContain(["--dangerously-skip-permissions", "--allowedTools", "--allowed-tools", "--disallowedTools", "--settings", "--bare"]);
    }

    [Fact]
    public void AModel_IsTheLastThingInTheArgv()
    {
        Claude().Build(Launch(Coder(), Model)).Request.Arguments.Should().Equal([.. Measured(Tree(Job)), "--model", Model]);
    }

    [Fact]
    public void AResumedTurn_IsTheSameArgvPlusTheResume()
    {
        var resumed = CoderHandle.Bind(Coder(), Session);

        Claude().Build(Launch(Coder(resume: resumed))).Request.Arguments
            .Should().Equal([.. Measured(Tree(Job)), "--resume", Session]);
        Claude().Build(Launch(Coder(resume: resumed), Model)).Request.Arguments
            .Should().Equal([.. Measured(Tree(Job)), "--resume", Session, "--model", Model]);
    }

    [Fact]
    public void ABoundHandle_RemembersTheJobTheWorktreeAndTheSession()
    {
        var handle = CoderHandle.Bind(Coder(), Session);

        handle.Should().Be(new CoderHandle(Job, Tree(Job), Session));
        handle.IsNone.Should().BeFalse();
        CoderHandle.None.IsNone.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("two words")]
    [InlineData("abc\n")]
    public void ASessionIdTheCliCouldNotHaveSaid_BindsToNothing(string session)
    {
        CoderHandle.Bind(Coder(), session).IsNone.Should().BeTrue("a malformed id is not a handle, and it never reaches an argv");
    }

    // ---------- d1: the handle is bound to the job and the worktree ----------

    [Fact]
    public async Task AHandleOfAnotherJob_IsRefusedBeforeAnyProcessStarts()
    {
        var anothers = CoderHandle.Bind(Coder(OtherJob), Session);
        var launch = Launch(Coder(Job, resume: anothers));

        var build = () => Claude().Build(launch);
        build.Should().Throw<ArgumentException>().WithMessage("*another job*");

        var launcher = new WatchedLauncher(new ProcessLauncher(), AnAcceptableClaude);
        var prepared = await Claude().PrepareAsync(launch, launcher, TestContext.Current.CancellationToken);

        var refused = prepared.Should().BeOfType<ConsultantPreparation.Refused>().Subject;
        refused.Failure.Should().BeOfType<ConsultFailure.LaunchRefused>().Which.Said.Should().Contain("another job");
        launcher.Requests.Should().BeEmpty("the handle is checked before the CLI is even asked its version");
    }

    [Fact]
    public async Task AHandleOfAnotherWorktree_IsRefused_EvenForTheSameJob()
    {
        var movedRoot = CoderHandle.Bind(Coder(Job, OtherRoot), Session);
        var launch = Launch(Coder(Job, resume: movedRoot));

        var build = () => Claude().Build(launch);
        build.Should().Throw<ArgumentException>().WithMessage("*another worktree*");

        var prepared = await Claude().PrepareAsync(launch, new WatchedLauncher(new ProcessLauncher(), AnAcceptableClaude), TestContext.Current.CancellationToken);
        prepared.Should().BeOfType<ConsultantPreparation.Refused>().Which.Failure
            .Should().BeOfType<ConsultFailure.LaunchRefused>().Which.Said.Should().Contain("another worktree");
    }

    [Fact]
    public void AHandleOfTheSameJobAndWorktree_IsAccepted_AndAForgedOneIsNot()
    {
        var own = CoderHandle.Bind(Coder(), Session);
        CoderLaunches.WhyNot(Coder(resume: own)).Should().BeEmpty();

        var forged = new CoderHandle(Job, Tree(Job), "not a session id\nwith a line break");
        CoderLaunches.WhyNot(Coder(resume: forged)).Should().NotBeEmpty("a handle that was not made by Bind is checked again where it is written into the argv");
    }

    [Theory]
    [InlineData("Job-A")]
    [InlineData("../job")]
    [InlineData("")]
    public void AJobIdThatIsNotTheWorktreeShape_IsRefused(string job)
    {
        CoderLaunches.WhyNot(new LaunchConfinement.Coder(job, Tree(Job))).Should().Contain("job id");
    }

    [Fact]
    public void AWorktreeThatIsNotTheFolderOfTheJob_IsRefused()
    {
        CoderLaunches.WhyNot(new LaunchConfinement.Coder(Job, Tree(OtherJob))).Should().Contain("not the worktree of job");
        CoderLaunches.WhyNot(new LaunchConfinement.Coder(Job, Path.Combine("relative", CoderWorktree.FolderOf(Job)))).Should().Contain("absolute");
        CoderLaunches.WhyNot(Coder()).Should().BeEmpty("the folder coai makes for a job is the one a launch may stand in");
    }

    [Fact]
    public void AHandleOnTheLaunchItself_IsRefused_ForACoder()
    {
        var launch = Launch(Coder()) with { Handle = Session };

        var build = () => Claude().Build(launch);

        build.Should().Throw<ArgumentException>().WithMessage("*Coder*handle*");
    }

    [Fact]
    public void ACoderLaunchNobodyPrepared_IsRefused_NeverBuiltWithAWeakerBoundary()
    {
        var unprepared = () => Claude().Build(Launch(Coder(), cli: ClaudeCapability.Unprobed));
        var withoutTheFlag = () => Claude().Build(Launch(Coder(), cli: ClaudeCapability.NoRestricted));

        unprepared.Should().Throw<ArgumentException>().WithMessage("*PrepareAsync*");
        withoutTheFlag.Should().Throw<ArgumentException>("a coder is never launched without --restricted");
    }

    // ---------- d2: acceptEdits cannot reach a consult or a question launch ----------

    [Fact]
    public void NoConsultOrQuestionLaunch_CarriesAWriteFlag()
    {
        var consult = new ConsultantLaunch(Path.GetFullPath("main-checkout"), "q", string.Empty, Path.GetFullPath("answers"),
            new ReviewerSettings("claude") { ClaudeCli = ClaudeCapability.WithRestricted });
        var argvs = new List<IReadOnlyList<string>> { Claude().Build(consult).Request.Arguments };
        foreach (var grant in (CapabilityGrant[])[CapabilityGrant.None, CapabilityGrant.Disk(Path.GetFullPath("alpha")), CapabilityGrant.Web])
        {
            var plan = (Confinement.Planned)ConfinementPlanner.Plan("claude", grant);
            argvs.Add(Claude().Build(consult with { Confinement = new LaunchConfinement.Planned(plan), ScratchDir = Path.GetFullPath("scratch") }).Request.Arguments);
        }

        argvs.Should().HaveCount(4);
        foreach (var argv in argvs)
        {
            argv.Should().NotContain(["acceptEdits", "--permission-prompts", "Edit", "Write"]).And.NotContain(a => a.Contains("Edit,", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheWriteFlags_AreSandboxVocabulary_SoNoPlannedLaunchSendsThemOnItsOwn()
    {
        ConfinementPlanner.IsSandboxVocabulary("--permission-prompts").Should().BeTrue(
            "NoAdapter_SendsASandboxFlag_ThePlanDidNotGiveIt reads this list; a planned launch that carried the flag would be red there");
        ConfinementPlanner.IsSandboxVocabulary("--permission-mode").Should().BeTrue();
        ConfinementPlanner.ClaudeWriteTools.Should().Equal("Read", "Edit", "Write", "Glob", "Grep");
        ConfinementPlanner.ClaudeDiskTools.Should().Equal("Read", "Glob", "Grep");
    }

    // ---------- d5: claude only ----------

    private static readonly string[] Others = ["codex", "antigravity", "local", "api", "vibe", "kimi"];

    public static TheoryData<string> OtherVendors() => [.. Others];

    private static IConsultantRuntime Adapter(string vendor) => vendor switch
    {
        "codex" => new CodexConsultant(new CodexRuntime()),
        "antigravity" => new AntigravityConsultant(new AntigravityRuntime()),
        "local" => new LocalConsultant(new LocalRuntime("local", "http://127.0.0.1:11434/v1"), "local"),
        "api" => new ApiConsultant(new ApiRuntime("grok", "https://api.x.ai/v1"), "grok", "grok"),
        "vibe" => new Runners.Vibe.VibeConsultant(new Runners.Vibe.VibeRuntime("vibe", Runners.Platform.HostKind.Windows)),
        "kimi" => new Runners.Kimi.KimiConsultant(new Runners.Kimi.KimiRuntime("kimi", Runners.Platform.HostKind.Windows,
            new Runners.Kimi.KimiSeams(root => Runners.Kimi.KimiPlaces.ForShim(root, Path.GetFullPath("persons-bin")), () => ("coai-mcp", [])))),
        _ => throw new ArgumentOutOfRangeException(nameof(vendor), vendor, null),
    };

    [Theory]
    [MemberData(nameof(OtherVendors))]
    public async Task AVendorOtherThanClaude_IsRefusedAWriteLaunch_WithTheSentence(string vendor)
    {
        var adapter = Adapter(vendor);
        var launch = Launch(Coder());

        var prepared = await adapter.PrepareAsync(launch, new WatchedLauncher(new ProcessLauncher(), AnAcceptableClaude), TestContext.Current.CancellationToken);

        var refused = prepared.Should().BeOfType<ConsultantPreparation.Refused>(vendor).Subject;
        refused.Failure.Should().BeOfType<ConsultFailure.VendorRefused>().Which.Said.Should().Be(CoderLaunches.NoCoderLaunch);
        CoderLaunches.NoCoderLaunch.Should().Contain("no coder launch").And.NotContain("claude only");

        var build = () => adapter.Build(launch);
        build.Should().Throw<ArgumentException>().WithMessage("*no coder launch*", $"{vendor}: reaching Build with a coder launch is a contract violation");
    }

    [Fact]
    public void EveryRuntimeInTheAssembly_ButClaude_IsInTheRefusalsList_SoANewVendorCannotSlipPast()
    {
        var runtimes = typeof(ClaudeConsultant).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IConsultantRuntime).IsAssignableFrom(t))
            .ToList();

        runtimes.Should().Contain(typeof(ClaudeConsultant), "the scan must still find the one runtime that is allowed — or it matches nothing and passes forever");
        runtimes.Where(t => t != typeof(ClaudeConsultant)).Should().BeEquivalentTo(Others.Select(vendor => Adapter(vendor).GetType()),
            "a runtime added without a line in Others was never asked for a write launch, and would take the default of its own PrepareAsync");
    }

    [Fact]
    public void ABoundHandle_IsReadOffTheEnvelopeOfTheTurn_AndATornEnvelopeBindsToNothing()
    {
        var coder = Coder();

        var whole = Claude().BindHandle(coder, Printed("{\"type\":\"result\",\"session_id\":\"" + Session + "\",\"result\":\"done\"}"));
        var torn = Claude().BindHandle(coder, Printed("{\"type\":\"result\",\"session_id\":\"67289"));

        whole.Should().Be(CoderHandle.Bind(coder, Session));
        whole.IsNone.Should().BeFalse();
        torn.IsNone.Should().BeTrue("a killed turn leaves no handle, and the job starts its next turn fresh");
    }

    // ---------- the capability, asked before the launch ----------

    private static ProcessResult Printed(string stdout, int exit = 0) => new(exit, stdout, string.Empty, false);

    private static string Help(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", name));

    /// <summary>A claude 2.1.330 that answers <c>--version</c> and <c>--help</c> and starts nothing else.</summary>
    private static ProcessResult? AnAcceptableClaude(ProcessRequest request) => request.Arguments switch
    {
        ["--version"] => Printed("2.1.330 (Claude Code)\n"),
        ["--help"] => Printed(Help("help-2.1.330-wsl.txt")),
        _ => throw new InvalidOperationException($"a capability check started a turn: {string.Join(' ', request.Arguments)}"),
    };

    [Fact]
    public async Task AClaudeThatCanBeLaunchedForWriting_IsPrepared_AndItsVersionIsOnTheResult()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), AnAcceptableClaude);

        var prepared = await Claude().PrepareAsync(Launch(Coder(), cli: ClaudeCapability.Unprobed), launcher, TestContext.Current.CancellationToken);

        var ready = prepared.Should().BeOfType<ConsultantPreparation.Ready>().Subject;
        ready.CliVersion.Should().Be("2.1.330", "the version is recorded on the launch result, where the job envelope takes it from");
        ready.Launch.Settings.ClaudeCli.Restricted.Should().BeTrue();
        ready.Launch.Confinement.Should().Be(Coder());
        launcher.Requests.Select(r => r.Arguments[0]).Should().Equal("--version", "--help");
        Claude().Build(ready.Launch).Request.Arguments.Should().Equal(Measured(Tree(Job)));
    }

    [Theory]
    [InlineData("2.1.329 (Claude Code)\n", "2.1.329")]
    [InlineData("9.0.0 (Claude Code)\n", "9.0.0")]
    [InlineData("a banner and no release\n", "unknown")]
    public async Task AClaudeOfAnyRelease_IsLaunchable_AndTheInstalledReleaseIsRecorded_NoReleaseStopsAnything(string printed, string recorded)
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => Printed(printed),
            _ => AnAcceptableClaude(request),
        });

        var prepared = await Claude().PrepareAsync(Launch(Coder()), launcher, TestContext.Current.CancellationToken);

        prepared.Should().BeOfType<ConsultantPreparation.Ready>().Which.CliVersion.Should().Be(recorded, "the release goes into the job's record beside the last measured one; it decides nothing");
    }

    [Fact]
    public async Task AClaudeThatCannotBeStarted_IsRefusedAsNotInstalled()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), _ => throw new System.ComponentModel.Win32Exception("no such file"));

        var prepared = await Claude().PrepareAsync(Launch(Coder()), launcher, TestContext.Current.CancellationToken);

        prepared.Should().BeOfType<ConsultantPreparation.Refused>().Which.Failure.Should().BeOfType<ConsultFailure.CliNotFound>();
    }

    [Fact]
    public async Task AHelpThatNeverAnswers_IsRefused_NotReadAsAnOldClaude()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--help"] => Printed(string.Empty, exit: 1),
            _ => AnAcceptableClaude(request),
        });

        var prepared = await Claude().PrepareAsync(Launch(Coder()), launcher, TestContext.Current.CancellationToken);

        prepared.Should().BeOfType<ConsultantPreparation.Refused>().Which.Failure
            .Should().BeOfType<ConsultFailure.VendorRefused>().Which.Said.Should().Contain("--help");
    }
}

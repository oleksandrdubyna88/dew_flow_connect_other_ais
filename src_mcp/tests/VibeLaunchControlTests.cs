using System.Collections.Concurrent;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ONE Vibe launch control, through the real executor (D8b, D10, D17, D18 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): every launch — a review, its repair, a retry — is checked immediately
/// before it starts, against the host, the built environment, the curated model, the owned home and the installed Vibe's
/// version; a refusal starts nothing; a turn on a home is held until the launch has been read.
/// </summary>
/// <remarks>
/// A scripted launcher stands in for the processes: it answers <c>--version</c> as the installed Vibe would, and a review
/// launch as Vibe does — the array on stdout, the export in <c>--output-dir</c>, and whatever the arm leaves in the home.
/// So every assertion here is about what the CONTROL let start, read off the launcher's own record.
/// </remarks>
public sealed class VibeLaunchControlTests : IDisposable
{
    private const string Answer = """[{"type":"message","role":"assistant","content":[{"type":"text","text":"{\"findings\": []}"}]}]""";

    private readonly TempDir _scratch = TempDir.For("coai-answers-vibectl-");
    private readonly TempDir _out = TempDir.For("coai-answers-vibeout-");
    private readonly TempDir _work = TempDir.For("coai-vibe-work-");

    public void Dispose()
    {
        _scratch.Dispose();
        _out.Dispose();
        _work.Dispose();
    }

    private ReviewerInvocation Invocation(string model = "glm-5-3", HostKind host = HostKind.Windows) =>
        new VibeRuntime("vibe", host).Build(RoleCatalog.ArchitectureRole, "review this", _work, "C:/s.json", _out,
            new ReviewerSettings("vibe") { Model = model, OwnedScratch = _scratch, Timeout = TimeSpan.FromMinutes(1) });

    private static string HomeOf(ReviewerInvocation invocation) => invocation.Request.Environment[VibeLaunch.HomeVariable]!;

    private static string ExportOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--output-dir") + 1];

    /// <summary>A Vibe on this "machine": its version, and what one review launch does.</summary>
    private sealed class FakeVibe(string version = "2.26.1") : IProcessLauncher
    {
        public ConcurrentQueue<ProcessRequest> Started { get; } = new();

        public Func<ProcessRequest, ProcessResult> Review { get; set; } = request => Answered(request);

        public TimeSpan ReviewTakes { get; set; } = TimeSpan.Zero;

        /// <summary>What the `--version` process does to the home while it runs — nothing, unless a test says so.</summary>
        public Action<ProcessRequest> WhileTheVersionIsAsked { get; set; } = _ => { };

        private int _inReview;

        public int MostAtOnce { get; private set; }

        public IReadOnlyList<ProcessRequest> Reviews => [.. Started.Where(r => !r.Arguments.SequenceEqual(["--version"]))];

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Started.Enqueue(request);
            if (request.Arguments.SequenceEqual(["--version"]))
            {
                WhileTheVersionIsAsked(request);
                return new ProcessResult(0, $"python.exe C:\\Users\\p\\.local\\bin\\vibe {version}\r\n", string.Empty, TimedOut: false);
            }

            MostAtOnce = Math.Max(MostAtOnce, Interlocked.Increment(ref _inReview));
            try
            {
                await Task.Delay(ReviewTakes, ct);
                return Review(request);
            }
            finally
            {
                Interlocked.Decrement(ref _inReview);
            }
        }

        /// <summary>What Vibe 2.26.1 does on a finished turn: the export, the model it ran on, the array on stdout.</summary>
        public static ProcessResult Answered(ProcessRequest request, string? ranOn = null, int exit = 0, string outcome = "finished", string usage = """{"input_tokens":6911,"output_tokens":38,"cached_input_tokens":0,"total_tokens":6949}""")
        {
            var export = ExportOf(request);
            Directory.CreateDirectory(export);
            var model = ranOn ?? request.Environment[VibeLaunch.ModelVariable];
            File.WriteAllText(Path.Combine(export, VibeExport.FileName),
                $$$"""{"outcome":"{{{outcome}}}","usage":{{{usage}}},"cost_usd":0.009,"config":{"active_model":"{{{model}}}"}}""");
            // Vibe makes its home when it is not there (§24: a bare start writes vibehistory into it).
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(request.Environment[VibeLaunch.HomeVariable]!).FullName, "vibehistory"), "Hello Vibe!\r\n");

            return new ProcessResult(exit, exit == 0 ? Answer : "[]", exit == 3 ? "Stopped: deadline\r\n" : string.Empty, TimedOut: false);
        }
    }

    [Fact]
    public async Task AFreshHome_OnAQualifiedVibe_IsAdmitted_AndTheReviewIsRead()
    {
        var vibe = new FakeVibe();

        var outcome = await new ReviewerExecutor(vibe).RunAsync(Invocation(), ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Usage.Should().Be(new Usage(6911, 38, null));
        vibe.Started.Select(r => r.Arguments[0]).Should().Equal("--version", "-p");
        vibe.Started.First().Environment.Should().BeEquivalentTo(vibe.Reviews[0].Environment,
            "the version is asked in the launch's own built environment, on its own home");
    }

    [Fact]
    public async Task TheExportFolder_IsGone_OnceTheLaunchHasBeenRead_AndTheTurnIsGivenBack()
    {
        var vibe = new FakeVibe();

        await new ReviewerExecutor(vibe).RunAsync(Invocation(), ct: TestContext.Current.CancellationToken);

        Directory.Exists(ExportOf(vibe.Reviews[0])).Should().BeFalse("D7: no export outlives its read");
        VibeHomeTurns.Held(vibe.Reviews[0].Environment[VibeLaunch.HomeVariable]!).Should().BeFalse("a finished turn leaves no gate behind");
    }

    [Fact]
    public async Task AModelOutsideTheCuratedList_IsRefused_BeforeAnythingStarts()
    {
        var vibe = new FakeVibe();

        var outcome = await new ReviewerExecutor(vibe).RunAsync(Invocation(model: "nope"), ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("'nope'").And.Contain("glm-5-3");
        vibe.Started.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    public async Task ALinuxOrWslHost_IsRefused_BeforeAnythingStarts(HostKind host)
    {
        var vibe = new FakeVibe();

        var outcome = await new ReviewerExecutor(vibe).RunAsync(Invocation(host: host), ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("D18");
        vibe.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task AHooksFileInTheHome_IsRefused_BeforeEvenTheVersionIsAsked()
    {
        var vibe = new FakeVibe();
        var invocation = Invocation();
        Directory.CreateDirectory(HomeOf(invocation));
        await File.WriteAllTextAsync(Path.Combine(HomeOf(invocation), "hooks.toml"), "[[hooks]]\n", TestContext.Current.CancellationToken);

        var outcome = await new ReviewerExecutor(vibe).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("hooks.toml").And.Contain(HomeOf(invocation));
        vibe.Started.Should().BeEmpty("nothing of Vibe's starts on a home that holds something Vibe does not write");
    }

    /// <summary>
    /// The risk consultation of 2026-10-13 (epic 2): the `--version` process is Vibe's own and runs on the home, so what it
    /// leaves is checked before the review starts — on the unmeasured branch (where the plugin bytes have no hashes to
    /// re-check) exactly as on the measured one.
    /// </summary>
    [Theory]
    [InlineData("2.27.0")]
    [InlineData("2.26.1")]
    public async Task AHooksFileTheVersionProbeLeaves_RefusesTheReview_OnEitherBranch(string version)
    {
        var vibe = new FakeVibe(version)
        {
            WhileTheVersionIsAsked = request => File.WriteAllText(
                Path.Combine(Directory.CreateDirectory(request.Environment[VibeLaunch.HomeVariable]!).FullName, "hooks.toml"), "[[hooks]]\n"),
        };
        var invocation = Invocation();

        var outcome = await new ReviewerExecutor(vibe).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>($"Vibe {version}: the home is checked again after `--version`")
            .Which.Reason.Should().Contain("hooks.toml");
        vibe.Reviews.Should().BeEmpty($"Vibe {version}: the review never starts on a home the version probe wrote a hooks.toml into");
    }

    /// <summary>
    /// A resumed home of an UNMEASURED Vibe whose cached plugin package carries an <c>mcp.json</c> that declares a server:
    /// Vibe 2.26.1 checks a cached package out without re-digesting it and connects the servers its <c>mcp.json</c>
    /// declares before any tool filter applies — so the launch is refused, and nothing of Vibe's starts.
    /// </summary>
    [Fact]
    public async Task ACachedPluginPackageThatDeclaresAServer_IsRefused_OnAnUnmeasuredVibe_BeforeAnythingStarts()
    {
        var vibe = new FakeVibe(version: "2.27.0");
        var invocation = Invocation();
        var package = Directory.CreateDirectory(Path.Combine(HomeOf(invocation), "logs", "session", "plugins", "packages", "zz", "zz-digest"));
        await File.WriteAllTextAsync(Path.Combine(package.FullName, "mcp.json"),
            """{"$schema":"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json","mcpServers":{"canary":{"type":"stdio","command":"python"}}}""",
            TestContext.Current.CancellationToken);

        var outcome = await new ReviewerExecutor(vibe).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("zz-digest/mcp.json");
        vibe.Started.Should().BeEmpty("a package that would start a server is refused before Vibe starts on the home");
    }

    /// <summary>
    /// The owner's decision of 2026-10-13: "Vibe must WARN, not refuse — set it up once and it must work if it can." A Vibe
    /// version with no manifest entry LAUNCHES — its plugin files admitted by name — and the outcome carries a warning that
    /// names the running version, the measured one, and what to re-run.
    /// </summary>
    [Fact]
    public async Task AnUnmeasuredVibe_Launches_OnAFreshHome_AndOnAResumedOne_AndTheOutcomeCarriesTheWarning()
    {
        foreach (var resumed in new[] { false, true })
        {
            var vibe = new FakeVibe(version: "2.27.0");
            var invocation = Invocation();
            if (resumed)
            {
                // A resumed home of the UNMEASURED version: its session, and plugin bytes no manifest names.
                Directory.CreateDirectory(Path.Combine(HomeOf(invocation), "logs", "session", "unified", "s1"));
                Directory.CreateDirectory(Path.Combine(HomeOf(invocation), "logs", "session", "plugins", "blobs", "zz"));
                await File.WriteAllTextAsync(Path.Combine(HomeOf(invocation), "logs", "session", "plugins", "blobs", "zz", "new-blob"), "2.27.0's bytes", TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(Path.Combine(HomeOf(invocation), "vibehistory"), "Hello Vibe!\r\n", TestContext.Current.CancellationToken);
            }

            var outcome = await new ReviewerExecutor(vibe).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

            outcome.Should().BeOfType<ReviewerOutcome.Ok>($"resumed={resumed}: an unmeasured version launches");
            outcome.Warning.Should().Contain("2.27.0").And.Contain("2.26.1").And.Contain("vibe-home-manifest.mjs")
                .And.Contain("probe-vibe-confinement.mjs", $"resumed={resumed}: it names the running version, the measured one and what to re-run");
            vibe.Reviews.Should().ContainSingle($"resumed={resumed}");
            Directory.Delete(HomeOf(invocation), recursive: true);
        }
    }

    [Fact]
    public async Task AMeasuredVibe_CarriesNoWarning()
    {
        var outcome = await new ReviewerExecutor(new FakeVibe()).RunAsync(Invocation(), ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Warning.Should().BeEmpty();
    }

    [Fact]
    public async Task AMeasuredVibe_WithATamperedPluginFile_IsStillRefused()
    {
        var vibe = new FakeVibe();
        var invocation = Invocation();
        var pinned = ((VibeManifestLoad.Loaded)VibeHomeManifest.Builtin).Manifest.For("2.26.1")[0].Pinned[0].Path;
        var file = Path.Combine([HomeOf(invocation), .. pinned.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "tampered", TestContext.Current.CancellationToken);

        var outcome = await new ReviewerExecutor(vibe).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain(pinned).And.Contain("bytes");
        vibe.Reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task AForbiddenFileInTheHome_IsStillRefused_OnAnUnmeasuredVibe()
    {
        var vibe = new FakeVibe(version: "2.27.0");
        var invocation = Invocation();
        Directory.CreateDirectory(HomeOf(invocation));
        await File.WriteAllTextAsync(Path.Combine(HomeOf(invocation), "hooks.toml"), "[[hooks]]\n", TestContext.Current.CancellationToken);

        var outcome = await new ReviewerExecutor(vibe).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("hooks.toml");
        vibe.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task ARepair_StartsOnlyAfterTheHomeIsCheckedAgain_SoWhatTheFirstLaunchLeftIsRefused()
    {
        var vibe = new FakeVibe();
        vibe.Review = request =>
        {
            // The first launch answers nothing the parser can read, and leaves a hooks.toml in its home.
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(request.Environment[VibeLaunch.HomeVariable]!).FullName, "hooks.toml"), "[[hooks]]\n");
            var answered = FakeVibe.Answered(request);
            return answered with { StdOut = """[{"role":"assistant","content":"not the schema"}]""" };
        };
        var first = Invocation();

        var outcome = await new ReviewerExecutor(vibe).RunAsync(first, first with { Request = first.Request with { StdIn = "repair" } },
            TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("hooks.toml");
        vibe.Reviews.Should().ContainSingle("the repair never started");
    }

    [Fact]
    public async Task TwoTurnsOnOneHome_RunOneAfterTheOther()
    {
        var vibe = new FakeVibe { ReviewTakes = TimeSpan.FromMilliseconds(300) };
        var executor = new ReviewerExecutor(vibe);
        var invocation = Invocation();

        await Task.WhenAll(executor.RunAsync(invocation, ct: TestContext.Current.CancellationToken), executor.RunAsync(invocation, ct: TestContext.Current.CancellationToken));

        vibe.Reviews.Should().HaveCount(2);
        vibe.MostAtOnce.Should().Be(1, "D17: turns sharing a home are serialised");
        VibeHomeTurns.Held(HomeOf(invocation)).Should().BeFalse();
    }

    [Fact]
    public async Task AnAnswerOnAnotherModel_IsAFailure_ThatKeepsWhatTheRunConsumed()
    {
        var vibe = new FakeVibe { Review = request => FakeVibe.Answered(request, ranOn: "mistral-medium-3.5") };

        var outcome = await new ReviewerExecutor(vibe).RunAsync(Invocation(model: "glm-5-3"), ct: TestContext.Current.CancellationToken);

        var failed = outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Subject;
        failed.FailureReason.Should().Contain("'mistral-medium-3.5'").And.Contain("'glm-5-3'");
        failed.Usage.Should().Be(new Usage(6911, 38, null), "D10: the reported usage stays with the failure");
    }

    [Fact]
    public async Task AStopAtTheTimeLimit_IsAFailure_EvenWithAnAnswer_AndItsNullUsageIsUnknown()
    {
        var vibe = new FakeVibe { Review = request => FakeVibe.Answered(request, exit: 3, outcome: "deadline", usage: "null") with { StdOut = Answer } };

        var outcome = await new ReviewerExecutor(vibe).RunAsync(Invocation(), ct: TestContext.Current.CancellationToken);

        var failed = outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Subject;
        failed.ExitCode.Should().Be(3);
        failed.FailureReason.Should().Contain("deadline");
        failed.Usage.Should().Be(Usage.Unknown, "§18: usage null in 2 of 3 — unknown, never zero");
    }
}

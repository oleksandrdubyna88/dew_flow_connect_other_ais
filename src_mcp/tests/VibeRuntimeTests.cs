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
/// What a Vibe reviewer launch IS (S2 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): the argv, the built environment, and
/// how the answer, the usage, a failure and the model are read back — each pinned to the cell of
/// research/RESULTS_vibe_confinement.md it rests on.
/// </summary>
/// <remarks>
/// In the serial <c>fakecli-env</c> collection: <c>AParentVibeVariable_NeverReachesTheLaunch</c> sets <c>VIBE_MCP_SERVERS</c> in the PROCESS, and outside a
/// collection that value was visible to every class running beside it (C11 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>).
/// </remarks>
[Collection("fakecli-env")]
public sealed class VibeRuntimeTests : IDisposable
{
    private readonly TempDir _out = TempDir.For("coai-answers-vibe-");

    private readonly TempDir _owned = TempDir.For("testsToDelete-vh-owned-");

    public void Dispose()
    {
        _out.Dispose();
        _owned.Dispose();
    }

    private const string Snapshot = @"C:\Temp\coai-answers-x\snapshot-1";

    private ReviewerInvocation Built(ReviewerSettings? settings = null, string prompt = "review this\nall of it") =>
        new VibeRuntime("vibe", HostKind.Windows).Build(RoleCatalog.ArchitectureRole, prompt, Snapshot, "C:/s.json", _out,
            settings ?? new ReviewerSettings("vibe") { Model = "glm-5-3", Timeout = TimeSpan.FromMinutes(10), OwnedScratch = _owned.Path });

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Fact]
    public void TheArgv_HoldsBothReadOnlyFlags_AndNeverTrust_AddDir_OrThePrompt()
    {
        var args = Built().Request.Arguments;

        // D2 (§4: 9 of 9 writes denied with both), D3 (§5: a trusted folder's hook runs; --add-dir trusts its folder).
        args.Should().ContainInConsecutiveOrder("--agent", "plan").And.ContainInConsecutiveOrder("--enabled-tools", "read_file");
        args.Should().NotContain("--trust").And.NotContain("--add-dir");
        args.Should().ContainInConsecutiveOrder("-p", "--output", "json");
        args.Should().NotContain(a => a.Contains("review this", StringComparison.Ordinal) || a.Contains('\n'), "the prompt rides stdin (D7, §13)");
        Built().Request.StdIn.Should().Be("review this\nall of it");
    }

    [Fact]
    public void TheWorkingFolder_IsTheOneTheRosterGave_AndTheWorkdirSaysTheSame()
    {
        var request = Built().Request;

        request.WorkingDirectory.Should().Be(Snapshot);
        After(request.Arguments, "--workdir").Should().Be(Snapshot);
    }

    [Fact]
    public void TheEnvironment_IsBuilt_NeverInherited_WithThePythonCliPinned()
    {
        var request = Built().Request;

        request.InheritsEnvironment.Should().BeFalse("§22 A: a VIBE_* the child inherits configures Vibe whatever its home holds");
        request.Passthrough.Should().BeEquivalentTo(ProcessEnvironment.Minimal);
        request.Environment.Keys.Should().BeEquivalentTo([VibeLaunch.HomeVariable, VibeLaunch.CliVariable, VibeLaunch.ModelVariable, VibeLaunch.Utf8Variable],
            "§26: UTF-8 mode is part of the built set");
        request.Environment[VibeLaunch.CliVariable].Should().Be("python", "§3: the Rust CLI refuses the shipped flags and is unmeasured");
        request.Environment[VibeLaunch.ModelVariable].Should().Be("glm-5-3");
        request.Environment[VibeLaunch.HomeVariable].Should().StartWith(_owned.Path);
    }

    [Fact]
    public void AParentVibeVariable_NeverReachesTheLaunch()
    {
        var before = Environment.GetEnvironmentVariable("VIBE_MCP_SERVERS");
        Environment.SetEnvironmentVariable("VIBE_MCP_SERVERS", """[{"name":"x","transport":"stdio","command":"python"}]""");
        try
        {
            var request = Built().Request;

            request.Environment.Should().NotContainKey("VIBE_MCP_SERVERS");
            request.InheritsEnvironment.Should().BeFalse();
            request.Passthrough.Should().NotContain("VIBE_MCP_SERVERS");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VIBE_MCP_SERVERS", before);
        }
    }

    [Fact]
    public void ARowWithNoModel_IsLaunchedOnTheCuratedDefault_AndTheRecordSaysWhich()
    {
        var invocation = Built(new ReviewerSettings("vibe"));

        invocation.Request.Environment[VibeLaunch.ModelVariable].Should().Be(VibeModels.Default);
        invocation.Model.Should().Be(VibeModels.Default);
    }

    /// <summary>D7 of the plan: the export (the whole conversation) lives in the attempt's own folder, beside the home — never in TEMP when a home folder is owned.</summary>
    [Fact]
    public void TheExport_LivesBesideTheHome_InTheOwnedFolder_OrInTheLaunchsOwnFolderWhenThereIsNone()
    {
        var owned = Built().Request;
        var none = Built(new ReviewerSettings("vibe")).Request;

        Path.GetDirectoryName(After(owned.Arguments, "--output-dir")).Should().Be(_owned.Path, "a sibling of the home, outside what the home check walks");
        Path.GetDirectoryName(After(none.Arguments, "--output-dir")).Should().Be(_out.Path);
    }

    [Fact]
    public void TheHome_IsOnePerReviewer_InTheRoundsScratch_OrTheLaunchsOwnFolderWhenThereIsNone()
    {
        var withScratch = Built().Request.Environment[VibeLaunch.HomeVariable]!;
        var without = Built(new ReviewerSettings("vibe")).Request.Environment[VibeLaunch.HomeVariable]!;

        Path.GetDirectoryName(withScratch).Should().Be(_owned.Path);
        Path.GetDirectoryName(without).Should().Be(_out.Path);
        Path.GetFileName(withScratch).Length.Should().BeLessThan(16, "a deep home broke on a >260-character path");
        VibeRuntime.HomeFor("x", "vibe", "Architecture").Should().Be(VibeRuntime.HomeFor("x", "vibe", "Architecture"))
            .And.NotBe(VibeRuntime.HomeFor("x", "vibe", "Security"));
    }

    [Theory]
    [InlineData(600, 590)]
    [InlineData(15, 5)]
    [InlineData(5, 1)]
    public void TheTimeLimit_IsAFewSecondsUnderTheLaunchsTimeout(int timeoutSeconds, int limit)
    {
        VibeRuntime.TimeLimit(TimeSpan.FromSeconds(timeoutSeconds)).Should().Be(limit);
    }

    [Fact]
    public void EachLaunch_GetsAnExportFolderOfItsOwn_AndATimeLimitFromItsOwnTimeout()
    {
        var runtime = new VibeRuntime("vibe", HostKind.Windows);
        var first = Built();
        var retry = runtime.ForLaunch(first with { Request = first.Request with { Timeout = TimeSpan.FromSeconds(100) } });
        var again = runtime.ForLaunch(first);

        After(retry.Request.Arguments, "--output-dir").Should().NotBe(After(first.Request.Arguments, "--output-dir"))
            .And.NotBe(After(again.Request.Arguments, "--output-dir"), "a retry never reads a stale export (D7)");
        Path.GetDirectoryName(After(retry.Request.Arguments, "--output-dir")).Should().Be(_owned.Path, "beside the owned home, in the folder the first launch's export was in");
        After(retry.Request.Arguments, "--time-limit").Should().Be("90");
    }

    private ReviewerInvocation WithExport(string? json)
    {
        var invocation = Built();
        var folder = After(invocation.Request.Arguments, "--output-dir");
        if (json is not null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, VibeExport.FileName), json);
        }

        return invocation;
    }

    private static readonly ProcessResult Finished = new(0, "[]", string.Empty, TimedOut: false);

    [Fact]
    public void TheAnswer_IsTheLastAssistantMessage_OfTheOutputArray()
    {
        const string stdout = """
            [
              {"type":"message","role":"user","content":[{"type":"text","text":"Reply"}]},
              {"type":"reasoning","text":"thinking"},
              {"type":"message","role":"assistant","content":[{"type":"text","text":"first"}]},
              {"type":"message","role":"assistant","content":[{"type":"text","text":"{\"findings\": "},{"type":"text","text":"[]}"}]}
            ]
            """;

        new VibeRuntime().ReadAnswer(Built(), Finished with { StdOut = stdout }).Should().Be("""{"findings": []}""");
        new VibeRuntime().ReadAnswer(Built(), Finished with { StdOut = "not json" }).Should().BeNull();
        new VibeRuntime().ReadAnswer(Built(), Finished with { StdOut = """[{"role":"user","content":"x"}]""" }).Should().BeNull();
    }

    [Fact]
    public void TheUsage_IsTheExports_WithNoCost_AndUnknownWhenThereIsNone()
    {
        var reported = WithExport("""{"outcome":"finished","usage":{"input_tokens":6911,"output_tokens":38,"cached_input_tokens":5,"total_tokens":6949},"cost_usd":0.0094,"config":{"active_model":"glm-5-3"}}""");
        var nulled = WithExport("""{"outcome":"deadline","usage":null,"cost_usd":null}""");
        var missing = WithExport(null);

        new VibeRuntime().ReadUsage(reported, Finished).Should().Be(new Usage(6911, 38, CostUsd: null, TokensCached: 5),
            "D9: cost_usd is Vibe's list price even on a subscription, and a numeric cost is read as a bill");
        new VibeRuntime().ReadUsage(nulled, Finished).Should().Be(Usage.Unknown, "§18: usage null in 2 of 3 deadline exports — unknown, never zero");
        new VibeRuntime().ReadUsage(missing, Finished).Should().Be(Usage.Unknown);
    }

    /// <summary>K3: a REVIEW is never capped — it reads a snapshot whose files its prompt names, and a cut review is paid for and lost.</summary>
    [Fact]
    public void AReview_AndItsRetryOrRepair_CarryNoTurnCap()
    {
        var runtime = new VibeRuntime("vibe", HostKind.Windows);
        var built = Built();

        built.Request.Arguments.Should().NotContain("--max-turns");
        runtime.ForLaunch(built).Request.Arguments.Should().NotContain("--max-turns");
    }

    /// <summary>K4: the cap's outcome is coai's sentence — what happened, why Vibe could not find it, and what to do.</summary>
    [Fact]
    public void ATurnLimit_IsSaidAsCoaisCap_AndATokenLimitInTheSameWords()
    {
        var turns = WithExport("""{"outcome":"turn_limit","stop_reason":"limit","usage":{"input_tokens":84186,"output_tokens":1395}}""");
        var tokens = WithExport("""{"outcome":"token_limit","stop_reason":"interrupted","usage":null}""");
        var deadline = WithExport("""{"outcome":"deadline","stop_reason":null,"usage":null}""");
        var runtime = new VibeRuntime();

        // Only that coai's cap stopped the launch before it answered: a conversation turn or a check may not be a file question at all.
        runtime.WhyItFailed(turns, Finished with { ExitCode = 3 }).Should().Contain("20").And.Contain("--max-turns").And.Contain("before it answered")
            .And.NotContain("list a folder").And.NotContain("name them").And.NotContain("did not find");
        runtime.WhyItFailed(tokens, Finished with { ExitCode = 3 }).Should().Contain("token").And.Contain("before it answered")
            .And.NotContain("list a folder").And.NotContain("did not find");
        runtime.WhyItFailed(deadline, Finished with { ExitCode = 3 }).Should().Contain("time limit", "the deadline's sentence is unchanged");
    }

    /// <summary>The file-search guidance belongs to the QUESTION ROW's report only (the consultant adapter offers it; nothing else does).</summary>
    [Fact]
    public void TheFileSearchAdvice_IsOfferedForACapStop_AndForNothingElse()
    {
        Runners.Consultation.IConsultantRuntime consultant = new VibeConsultant(new VibeRuntime());
        var capped = new VibeRuntime().WhyItFailed(WithExport("""{"outcome":"turn_limit","stop_reason":"limit"}"""), Finished with { ExitCode = 3 });
        var tokenCapped = new VibeRuntime().WhyItFailed(WithExport("""{"outcome":"token_limit","stop_reason":"interrupted"}"""), Finished with { ExitCode = 3 });

        consultant.QuestionRowAdvice(capped).Should().Contain("name them exactly").And.Contain("Vibe cannot list a folder").And.Contain("§28");
        consultant.QuestionRowAdvice(tokenCapped).Should().Contain("Vibe cannot list a folder");
        consultant.QuestionRowAdvice("Vibe stopped at its own time limit").Should().BeEmpty();
        consultant.QuestionRowAdvice(string.Empty).Should().BeEmpty();
        ((Runners.Consultation.IConsultantRuntime)new Runners.Consultation.CodexConsultant(new CodexRuntime())).QuestionRowAdvice(capped).Should().BeEmpty("no other adapter has a cap to explain");
    }

    [Fact]
    public void ADeadline_IsSaidAsOne_FromTheExportsOutcome()
    {
        var deadline = WithExport("""{"outcome":"deadline","stop_reason":null,"usage":null}""");
        var signedOut = WithExport("""{"outcome":"config_error","error":{"message":"Missing MISTRAL_API_KEY environment variable for mistral provider."}}""");

        new VibeRuntime().WhyItFailed(deadline, Finished with { ExitCode = 3 }).Should().Contain("deadline").And.Contain("time limit");
        new VibeRuntime().WhyItFailed(signedOut, Finished with { ExitCode = 1 }).Should().Contain("Missing MISTRAL_API_KEY");
        new VibeRuntime().WhyItFailed(WithExport(null), Finished with { ExitCode = 2 }).Should().BeEmpty("no export: the stderr tail speaks");
    }

    [Fact]
    public void AnAnswerOnAnotherModel_IsDisowned_AndOneOnTheAskedModelIsNot()
    {
        var runtime = new VibeRuntime();

        runtime.Disowned(WithExport("""{"outcome":"finished","config":{"active_model":"glm-5-3"}}"""), Finished).Should().BeEmpty();
        runtime.Disowned(WithExport("""{"outcome":"finished","config":{"active_model":"","routed_default_model":"glm-5-3"}}"""), Finished)
            .Should().Contain("not on the row's 'glm-5-3'", "§11: an unknown alias leaves active_model empty and answers on the default");
        runtime.Disowned(WithExport("""{"outcome":"finished","config":{"active_model":"mistral-medium-3.5"}}"""), Finished)
            .Should().Contain("'mistral-medium-3.5'");
        runtime.Disowned(WithExport("""{"outcome":"finished"}"""), Finished).Should().Contain("its own default");
        runtime.Disowned(WithExport(null), Finished).Should().Contain("no export");
    }

    [Fact]
    public void Vibe_AsksForASanitisedSnapshot()
    {
        new VibeRuntime().NeedsASanitisedSnapshot.Should().BeTrue("D6: Vibe reads only its cwd (§10) and loads its configuration from it");
    }

    [Fact]
    public void Vibe_IsRegistered_AndMachineOnly()
    {
        ReviewerRuntimeSelector.RuntimeNames.Should().Contain("vibe");
        ReviewerRuntimeSelector.MachineOnlyRuntimes.Should().Contain("vibe", "D12: the sign-in is this machine's keyring");
        ReviewerRuntimeSelector.Default.Find("vibe").Should().BeOfType<VibeRuntime>();
        ReviewerRuntimeSelector.Named("vibe", "my-mistral").Should().BeOfType<VibeRuntime>().Which.Provider.Should().Be("my-mistral");
        RuntimeResolution.For(new VendorIdentity("my-mistral", "vibe", string.Empty)).Should().BeOfType<VibeRuntime>();
    }

    /// <summary>
    /// The constructor that takes a bare <see cref="HostKind"/> is a test's: it gives the adapter NO sign-in (so a test never reads the
    /// person's real <c>~/.vibe/.env</c>) and, unset, this machine's WSL generation. A product caller on it would run Vibe signed out on a
    /// WSL2 host without a word — so the public ways in are the id alone (the machine's host) and a <see cref="VibeHost"/>.
    /// </summary>
    [Fact]
    public void TheConstructorThatTakesABareHostKind_IsNotPublic()
    {
        var publicOnes = typeof(VibeRuntime).GetConstructors().Select(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray()).ToList();

        publicOnes.Should().NotContain(parameters => parameters.Contains(typeof(HostKind)), "a bare HostKind carries no sign-in and no WSL generation: a test-only way in");
        publicOnes.Should().Contain(parameters => parameters.SequenceEqual(new[] { typeof(string) }), "the machine's host");
        publicOnes.Should().Contain(parameters => parameters.Length > 1 && parameters[1] == typeof(VibeHost), "a stated host");
    }

    [Theory]
    [InlineData(true, @"C:\Users\p\.local\bin\vibe.exe")]
    [InlineData(false, "vibe")]
    public void TheDefaultExecutable_IsWhereUvPutsIt_WhenItIsThere(bool installed, string expected)
    {
        VibeRuntime.InstalledExecutableFor(HostKind.Windows, @"C:\Users\p", path => installed && path == @"C:\Users\p\.local\bin\vibe.exe")
            .Should().Be(expected);
        VibeRuntime.InstalledExecutableFor(HostKind.MacOs, "/Users/p", _ => true).Should().Be("vibe", "nothing was measured on macOS");
    }

    private const string Home = "/home/p";

    /// <summary>The environment of a Linux host — only what a row sets.</summary>
    private static Func<string, string?> Env(params (string Name, string Value)[] set) => name => set.Where(pair => pair.Name == name).Select(pair => pair.Value).FirstOrDefault();

    /// <summary>
    /// S-V11 (DL5 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>), one row per step of the precedence on WSL. The row's own CLI path wins before
    /// any of this is asked (<c>VibeRuntime.Build</c>); what is asked here is what the row did NOT set. Every candidate counts only when its
    /// file is THERE, so each arm's `exists` is the list of files the machine has.
    /// </summary>
    [Theory]
    [InlineData("/home/p/.local/bin/vibe", "", "", "/home/p/.local/bin/vibe")]
    [InlineData("/uv/bin/vibe|/home/p/.local/bin/vibe", "/uv/bin", "", "/uv/bin/vibe")]
    [InlineData("/home/p/.local/bin/vibe", "/uv/bin", "", "/home/p/.local/bin/vibe")]
    [InlineData("/xdg/bin/vibe|/home/p/.local/bin/vibe", "", "/xdg/bin", "/xdg/bin/vibe")]
    [InlineData("/home/p/.local/bin/vibe", "relative/bin", "", "/home/p/.local/bin/vibe")]
    [InlineData("", "", "", "vibe")]
    public void OnWsl_TheDefaultExecutable_FollowsUvsOwnOrder_ThenThePath(string existing, string uvToolBinDir, string xdgBinHome, string expected)
    {
        var files = existing.Split('|', StringSplitOptions.RemoveEmptyEntries);

        VibeRuntime.InstalledExecutableFor(HostKind.Wsl, Home, Env(("UV_TOOL_BIN_DIR", uvToolBinDir), ("XDG_BIN_HOME", xdgBinHome)), files.Contains)
            .Should().Be(expected, $"expected {expected}, got something else (the machine has: {existing})");
    }

    [Fact]
    public void OnWsl_AnXdgDataHomeNamesItsSiblingBinFolder_AfterTheTwoVariablesAndBeforeTheHome()
    {
        VibeRuntime.InstalledExecutableFor(HostKind.Wsl, Home, Env(("XDG_DATA_HOME", "/data/share")), new[] { "/data/bin/vibe", "/home/p/.local/bin/vibe" }.Contains)
            .Should().Be("/data/bin/vibe", "uv derives its bin folder from $XDG_DATA_HOME/../bin before $HOME/.local/bin");
    }

    [Fact]
    public void OnWsl_TheRowsOwnCliPath_WinsOverEverythingTheDefaultFinds()
    {
        var invocation = new VibeRuntime("vibe", HostKind.Wsl, VibePathBudget.None, VibeSignIn.None, WslGeneration.Wsl2)
            .Build(RoleCatalog.ArchitectureRole, "p", "/w", "/s.json", "/out", new ReviewerSettings("vibe") { ExecutablePath = "/opt/my/vibe" });

        invocation.Request.Executable.Should().Be("/opt/my/vibe");
    }
}

/// <summary>
/// The pure halves of the ONE Vibe launch control (<see cref="VibeLaunch"/>): the host (D18), the built environment
/// (D17), the curated model (D10) and the version a <c>vibe --version</c> prints.
/// </summary>
public sealed class VibeLaunchRulesTests
{
    [Theory]
    [InlineData(HostKind.MacOs)]
    [InlineData(HostKind.Other)]
    public void AHostNothingWasMeasuredOn_IsRefused_NamingThePlanAndTheTwoMeasuredHosts(HostKind host)
    {
        VibeLaunch.HostRefusal(host, "vibe", WslGeneration.None).Should().Contain("D18").And.Contain(HostKinds.Word(host)).And.Contain("Windows and WSL2 only");
    }

    /// <summary>S-V17 (DL1): D18 lifts for WSL2 only. A native Linux host is refused with the sentence that says it is measured on WSL2 only.</summary>
    [Fact]
    public void ANativeLinuxHost_IsRefused_WithTheWsl2OnlySentence()
    {
        VibeLaunch.HostRefusal(HostKind.Linux, "vibe", WslGeneration.None).Should().Contain("DL1").And.Contain("WSL2 only").And.Contain("not yet on a native Linux host",
            "a native Linux host should still be refused with the WSL2-only sentence, but Vibe started");
    }

    [Fact]
    public void AWsl2Host_IsAdmitted()
    {
        VibeLaunch.HostRefusal(HostKind.Wsl, "vibe", WslGeneration.Wsl2).Should().BeEmpty();
    }

    /// <summary>S-V17: a WSL1 kernel reads as <see cref="HostKind.Wsl"/> and is refused with the WSL1 variant of the sentence — and its cure.</summary>
    [Theory]
    [InlineData(WslGeneration.Wsl1)]
    [InlineData(WslGeneration.None)]
    public void AWsl1Host_IsRefused_WithTheWsl1Sentence(WslGeneration generation)
    {
        VibeLaunch.HostRefusal(HostKind.Wsl, "vibe", generation).Should().Contain("WSL2 only").And.Contain("this is WSL1").And.Contain("Run this from Windows (not inside WSL):").And.Contain("`wsl --set-version <distro> 2`",
            "a WSL1 banner was admitted as WSL2");
    }

    /// <summary>
    /// S-V17: every banner of <c>shared/wsl-banner-vectors.json</c> — the ONE rule for which WSL a kernel is, which the extension's tests read
    /// too — through the generation predicate; only a WSL2 banner starts Vibe. A WSL2 kernel with no marker reads as none (the vector says so).
    /// </summary>
    [Theory]
    [MemberData(nameof(WslBannerVectors.Ids), MemberType = typeof(WslBannerVectors))]
    public void OnlyAWsl2Banner_StartsVibe(string id)
    {
        var vector = WslBannerVectors.Named(id);
        var generation = WslGenerations.Of(vector.Banner);

        generation.Should().Be(vector.Generation, $"vector '{id}': a WSL1 banner was admitted as WSL2, or the extension's rule and the server's differ");
        var kind = HostKinds.Of(windows: false, linux: true, macOs: false, wsl: VendorDiagnosis.NamesWsl(vector.Banner));

        (VibeLaunch.HostRefusal(kind, "vibe", generation).Length == 0).Should().Be(vector.Generation == WslGeneration.Wsl2);
    }

    [Fact]
    public void TheSharedBannerVectors_Loaded_AndCoverEveryGeneration()
    {
        WslBannerVectors.All.Select(vector => vector.Generation).Distinct().Should().BeEquivalentTo(Enum.GetValues<WslGeneration>(), "none, WSL1 and WSL2 each have a vector");
    }
    [Theory]
    [InlineData("wsl")]
    [InlineData(@"C:\Windows\System32\wsl.exe")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\p\.local\bin\vibe")]
    [InlineData(@"\\wsl$\Ubuntu\home\p\.local\bin\vibe")]
    public void AWslBridge_OnWindows_IsTheLinuxSide_AndRefused(string executable)
    {
        VibeLaunch.HostRefusal(HostKind.Windows, executable, WslGeneration.None).Should().Contain("D18");
    }

    [Fact]
    public void AWindowsVibe_OnWindows_IsNotRefusedByTheHost()
    {
        VibeLaunch.HostRefusal(HostKind.Windows, @"C:\Users\p\.local\bin\vibe.exe", WslGeneration.None).Should().BeEmpty();
    }

    private static ProcessRequest TheBuiltOne() =>
        new VibeRuntime("vibe", HostKind.Windows).Build(RoleCatalog.ArchitectureRole, "p", "C:/w", "C:/s.json", "C:/out", new ReviewerSettings("vibe")).Request;

    [Fact]
    public void TheBuiltEnvironment_PassesTheControl_AndEveryOtherShapeIsRefused()
    {
        var built = TheBuiltOne();

        VibeLaunch.EnvironmentRefusal(built).Should().BeEmpty();
        VibeLaunch.EnvironmentRefusal(built with { InheritsEnvironment = true }).Should().Contain("D17");
        VibeLaunch.EnvironmentRefusal(built with { Passthrough = ProcessEnvironment.Passthrough }).Should().Contain("D17");
        VibeLaunch.EnvironmentRefusal(built with { Environment = new Dictionary<string, string?>(built.Environment) { [VibeLaunch.CliVariable] = "rust" } })
            .Should().Contain("VIBE_CLI=python");
        VibeLaunch.EnvironmentRefusal(built with { Environment = new Dictionary<string, string?>(built.Environment) { ["VIBE_MCP_SERVERS"] = "[]" } })
            .Should().Contain("D17", "a fourth VIBE_* configures Vibe (§22 A)");
    }

    [Fact]
    public void AnAliasOutsideTheCuratedList_IsRefusedWithTheList()
    {
        VibeLaunch.ModelRefusal("glm-5-3").Should().BeEmpty();
        VibeLaunch.ModelRefusal("mistral-medium-3.5").Should().BeEmpty();
        VibeLaunch.ModelRefusal("nope").Should().Contain("'nope'").And.Contain("mistral-medium-3.5, glm-5-3");
    }

    [Theory]
    [InlineData("python.exe C:\\Users\\abcde\\.local\\bin\\vibe 2.26.1\r\n", "2.26.1")]
    [InlineData("vibe 2.27.10", "2.27.10")]
    [InlineData("usage: vibe", "")]
    public void TheVersion_IsReadOffWhatVibePrints(string printed, string version)
    {
        VibeLaunch.VersionIn(printed).Should().Be(version);
    }
}

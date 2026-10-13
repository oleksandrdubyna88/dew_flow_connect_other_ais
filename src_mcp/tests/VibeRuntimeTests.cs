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
public sealed class VibeRuntimeTests : IDisposable
{
    private readonly TempDir _out = TempDir.For("coai-answers-vibe-");

    public void Dispose() => _out.Dispose();

    private const string Snapshot = @"C:\Temp\coai-answers-x\snapshot-1";

    private ReviewerInvocation Built(ReviewerSettings? settings = null, string prompt = "review this\nall of it") =>
        new VibeRuntime("vibe", HostKind.Windows).Build(RoleCatalog.ArchitectureRole, prompt, Snapshot, "C:/s.json", _out,
            settings ?? new ReviewerSettings("vibe") { Model = "glm-5-3", Timeout = TimeSpan.FromMinutes(10), OwnedScratch = @"C:\Temp\coai-answers-r" });

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
        request.Environment.Keys.Should().BeEquivalentTo([VibeLaunch.HomeVariable, VibeLaunch.CliVariable, VibeLaunch.ModelVariable]);
        request.Environment[VibeLaunch.CliVariable].Should().Be("python", "§3: the Rust CLI refuses the shipped flags and is unmeasured");
        request.Environment[VibeLaunch.ModelVariable].Should().Be("glm-5-3");
        request.Environment[VibeLaunch.HomeVariable].Should().StartWith(@"C:\Temp\coai-answers-r");
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

    [Fact]
    public void TheHome_IsOnePerReviewer_InTheRoundsScratch_OrTheLaunchsOwnFolderWhenThereIsNone()
    {
        var withScratch = Built().Request.Environment[VibeLaunch.HomeVariable]!;
        var without = Built(new ReviewerSettings("vibe")).Request.Environment[VibeLaunch.HomeVariable]!;

        Path.GetDirectoryName(withScratch).Should().Be(@"C:\Temp\coai-answers-r");
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
        Path.GetDirectoryName(After(retry.Request.Arguments, "--output-dir")).Should().Be(_out.Path, "inside the launch's own coai-answers-* folder");
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

    [Theory]
    [InlineData(true, @"C:\Users\p\.local\bin\vibe.exe")]
    [InlineData(false, "vibe")]
    public void TheDefaultExecutable_IsWhereUvPutsIt_WhenItIsThere(bool installed, string expected)
    {
        VibeRuntime.InstalledExecutableFor(HostKind.Windows, @"C:\Users\p", path => installed && path == @"C:\Users\p\.local\bin\vibe.exe")
            .Should().Be(expected);
        VibeRuntime.InstalledExecutableFor(HostKind.Linux, "/home/p", _ => true).Should().Be("vibe", "nothing has run Vibe on Linux (D18)");
    }
}

/// <summary>
/// The pure halves of the ONE Vibe launch control (<see cref="VibeLaunch"/>): the host (D18), the built environment
/// (D17), the curated model (D10) and the version a <c>vibe --version</c> prints.
/// </summary>
public sealed class VibeLaunchRulesTests
{
    [Theory]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    [InlineData(HostKind.MacOs)]
    [InlineData(HostKind.Other)]
    public void AnyHostButWindows_IsRefused_NamingThePlan(HostKind host)
    {
        VibeLaunch.HostRefusal(host, "vibe").Should().Contain("D18").And.Contain(HostKinds.Word(host));
    }

    [Theory]
    [InlineData("wsl")]
    [InlineData(@"C:\Windows\System32\wsl.exe")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\p\.local\bin\vibe")]
    [InlineData(@"\\wsl$\Ubuntu\home\p\.local\bin\vibe")]
    public void AWslBridge_OnWindows_IsTheLinuxSide_AndRefused(string executable)
    {
        VibeLaunch.HostRefusal(HostKind.Windows, executable).Should().Contain("D18");
    }

    [Fact]
    public void AWindowsVibe_OnWindows_IsNotRefusedByTheHost()
    {
        VibeLaunch.HostRefusal(HostKind.Windows, @"C:\Users\p\.local\bin\vibe.exe").Should().BeEmpty();
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
    [InlineData("python.exe C:\\Users\\strug\\.local\\bin\\vibe 2.26.1\r\n", "2.26.1")]
    [InlineData("vibe 2.27.10", "2.27.10")]
    [InlineData("usage: vibe", "")]
    public void TheVersion_IsReadOffWhatVibePrints(string printed, string version)
    {
        VibeLaunch.VersionIn(printed).Should().Be(version);
    }
}

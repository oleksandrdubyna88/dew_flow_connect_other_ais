using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi reviewer end to end — the real adapter, the real launch control over a fake person's home (a real junction, a real lock), the real
/// executor — with the launcher playing <c>kimi</c> and the shim (E4.4, S4.11's line, <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>). What is
/// asserted is what the launcher was asked: the version in the built environment on the owned home, then the shim with the seven built names.
/// </summary>
public sealed class AKimiReviewerRunsThroughTheShimTests : IDisposable
{
    private readonly KimiFixture _person = new();

    public void Dispose() => _person.Dispose();

    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Kimi runs on Windows hosts only (D18)");

    private KimiRuntime Runtime() => new("kimi", HostKind.Windows, new KimiSeams(_person.PlacesAt, () => ("coai-mcp.exe", [])));

    private ReviewerInvocation Invocation(string model = "kimi-code/k3")
    {
        var settings = new ReviewerSettings("kimi") { Model = model, OwnedScratch = Path.Combine(_person.Folder, "round"), Timeout = TimeSpan.FromMinutes(5) };

        return Runtime().Build(RoleCatalog.ArchitectureRole, "review this", Directory.CreateDirectory(Path.Combine(_person.Folder, "worktree")).FullName, Path.Combine(_person.Folder, "s.json"), _person.Folder, settings);
    }

    private IDisposable Rooted() => KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));

    [Fact]
    public async Task AnAnsweredReview_IsReadFromTheShimsReport_ThroughTheLaunchControl()
    {
        OnWindows();
        using var rooted = Rooted();
        var launcher = new ScriptedKimi();
        var invocation = Invocation();
        var root = KimiEnvironment.FlagOf(invocation.Request, "--home");

        var outcome = await new ReviewerExecutor(launcher).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Usage.Should().Be(new Usage(19973, 31, null));
        outcome.Warning.Should().BeEmpty();
        outcome.CliVersion.Should().Be("kimi 2.1.1");
        var places = _person.PlacesAt(root);
        var version = launcher.Requests.First();
        version.Arguments.Should().Equal("--version");
        version.Executable.Should().Be(places.Executable);
        version.WorkingDirectory.Should().Be(places.Home);
        KimiEnvironment.Refusal(version, places).Should().BeEmpty("the version was asked in the built environment on the owned home");
        var shim = launcher.ShimRequests.Should().ContainSingle().Which;
        shim.Environment.Should().HaveCount(7).And.ContainKey(KimiEnvironment.HomeVariable);
        shim.InheritsEnvironment.Should().BeFalse();
        Junctions.SameFolder(Junctions.Target(places.Link), _person.Credentials).Should().BeTrue("the owned home's sign-in is the person's, by junction");
        File.ReadAllBytes(places.Config).Should().Equal(_person.ExpectedConfig("kimi-code/k3"), "the one config.toml coai writes, for this person and this model");
    }

    // ---------- E4.4 review M2: the request meets the real shim ----------

    /// <summary>Answers <c>--version</c> itself (the fake CLI would need a steering file the built environment does not carry) and hands everything else — the SHIM — to the real launcher.</summary>
    private sealed class VersionAnsweringLauncher(IProcessLauncher real) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            request.Arguments.Contains("--version")
                ? Task.FromResult(new ProcessResult(0, "kimi 2.1.1\r\n", string.Empty, TimedOut: false))
                : real.RunAsync(request, ct);
    }

    [Fact]
    public async Task TheRealArgv_ParsesInTheRealShim_ThatStartsItsAgentInTheBuiltEnvironment()
    {
        OnWindows();
        using var rooted = Rooted();
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk(KimiShimScripts.CleanReview)], FakeAcpAgent.Models("kimi-code/k3"));
        agent.WithFields(agent.EnvField());
        var runtime = new KimiRuntime("kimi", HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (ServerBinary.Path, [])));
        var settings = new ReviewerSettings("kimi")
        {
            Model = "kimi-code/k3",
            ExecutablePath = FakeAcpAgent.FakeCliExe,
            OwnedScratch = Path.Combine(_person.Folder, "round"),
            Timeout = TimeSpan.FromMinutes(2),
        };
        var invocation = runtime.Build(RoleCatalog.ArchitectureRole, "review this", agent.Cwd, Path.Combine(_person.Folder, "s.json"), _person.Folder, settings);

        var outcome = await new ReviewerExecutor(new VersionAnsweringLauncher(new ProcessLauncher())).RunAsync(invocation, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>("the argv KimiRuntime builds is one the real coai-mcp --ask-acp accepts, and the fake agent's answer comes back through the report");
        var variables = File.ReadAllLines(agent.EnvFile).Where(line => line.IndexOf('=', 1) > 0).ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..], StringComparer.OrdinalIgnoreCase);
        var places = _person.PlacesAt(KimiEnvironment.FlagOf(invocation.Request, "--home"));
        variables["USERPROFILE"].Should().Be(places.Os, "the shim itself started in the built environment, and the agent in the one it built");
        variables["KIMI_CODE_HOME"].Should().Be(places.Home);
    }

    // ---------- S4.11, the line ----------

    [Fact]
    public async Task AnUnmeasuredKimi_WarnsOnTheLine_WithTheSentenceTheCardShows()
    {
        OnWindows();
        using var rooted = Rooted();
        var launcher = new ScriptedKimi(version: "2.2.0");

        var outcome = await new ReviewerExecutor(launcher).RunAsync(Invocation(), ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>("the owner's rule: warn and run");
        outcome.Warning.Should().Be(((IReviewerRuntime)Runtime()).VersionWarning("kimi, version 2.2.0")).And.Contain("2.2.0").And.Contain("2.1.1");
        outcome.CliVersion.Should().Be("kimi 2.2.0");
    }

    // ---------- the control refuses, the shim never starts ----------

    [Fact]
    public async Task ARefusedLaunch_NeverStartsTheShim_AndSaysWhy()
    {
        OnWindows();
        using var rooted = Rooted();
        var launcher = new ScriptedKimi();

        var outcome = await new ReviewerExecutor(launcher).RunAsync(Invocation("moonshot-ai/kimi-k2.6"), ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("not on the Kimi Code plan");
        launcher.Requests.Should().BeEmpty("nothing was started: not even the version");
    }

    [Fact]
    public async Task AHomeTheRunLeftSomethingForbiddenIn_IsRefusedBeforeTheRepairStarts()
    {
        OnWindows();
        using var rooted = Rooted();
        var invocation = Invocation();
        var root = KimiEnvironment.FlagOf(invocation.Request, "--home");
        var launcher = new ScriptedKimi(_ =>
        {
            File.WriteAllText(Path.Combine(root, "home", "mcp.json"), "{}");

            return KimiShimScripts.Answered("not the schema's JSON");
        });

        var outcome = await new ReviewerExecutor(launcher).RunAsync(invocation, invocation, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("mcp.json").And.Contain("delete");
        launcher.Shims.Should().Be(1, "the repair never started on a home that was written to");
    }
}

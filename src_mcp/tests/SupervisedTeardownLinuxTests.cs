using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The Linux teardown, in REAL processes (DL9 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>; S-K6, S-K15, S-K16, S-K22, L-N2, L-N3; A3 §4, §10). A test host must never become a child subreaper — it would
/// collect every other test's orphans — so the subreaper is a child of the test: <c>FakeCli supervise-tree</c> drives the product's <see cref="SupervisedTeardown"/> directly, <c>FakeCli shim-acp</c> runs the
/// ACP client the way <c>--ask-acp</c> runs it (subreaper, <c>SIGTERM</c>/<c>SIGINT</c> into a token, the Kimi supervisor and its marker) against the scripted agent. Linux CI only; elsewhere they skip.
/// </summary>
/// <remarks>
/// What they do NOT prove: the real <c>kimi</c>'s sub-agents, its background shell and its MCP servers (<c>research/RESULTS_kimi_confinement.md</c> §29.8) — a fake agent starts the processes a test names, and
/// a real Kimi starts others.
/// </remarks>
public sealed class SupervisedTeardownLinuxTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly TempDir _dir = TempDir.For("testsToDelete-teardown-");
    private readonly List<Process> _started = [];
    private readonly List<PinnedMember> _members = [];

    [DllImport("libc", EntryPoint = "kill")]
    private static extern int Kill(int pid, int signal);

    /// <summary>Writes down WHO a process is — its pid with the start time and exact start ticks it has right now — so the end of the test can end it by identity (a pidfd, checked on the pin), never by a bare number. A process that is already gone is nobody to end.</summary>
    private void Remember(int pid)
    {
        if (ProcessTracking.StartedAt(pid) is { } started)
        {
            _members.Add(PinnedMember.Of(new ProcessIdentity(pid, started), ProcReads.Real));
        }
    }

    public void Dispose()
    {
        // Every process this test made, ended BY IDENTITY (the product TreeSignals: pin, check the start, signal through the pidfd) — never by a bare number or an image name — so a red run
        // leaks nothing into the suite and a pid that somebody else has reused is left alone.
        if (OperatingSystem.IsLinux() && _members.Count > 0)
        {
            _ = TreeSignals.Kill([.. _members.DistinctBy(member => member.Identity.Pid)], PidfdLayer.Calls);
        }

        _started.ForEach(process => process.Dispose());
        _dir.Dispose();
    }

    private static void OnLinux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "the Linux teardown (subreaper, pidfd, /proc) runs on the Linux CI job");

    private Process StartFakeCli(params string[] arguments)
    {
        var info = new ProcessStartInfo(FakeAcpAgent.FakeCliExe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info)!;
        _started.Add(process);
        Remember(process.Id);

        return process;
    }

    private static async Task<(int Exit, string Out, string Err)> FinishedAsync(Process process)
    {
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(Patience);
        await process.WaitForExitAsync(limit.Token);

        return (process.ExitCode, await output, await error);
    }

    private static bool IsGone(int pid) => ProcStat.ReadFor(pid, ProcReads.Real) is ProcStatRead.Gone;

    private static async Task<bool> GoneSoonAsync(int pid)
    {
        var until = DateTime.UtcNow + Patience;
        while (!IsGone(pid) && DateTime.UtcNow < until)
        {
            await Task.Delay(50);
        }

        return IsGone(pid);
    }

    private int PidIn(string file)
    {
        var until = DateTime.UtcNow + Patience;
        while (!(File.Exists(file) && int.TryParse(File.ReadAllText(file), out _)) && DateTime.UtcNow < until)
        {
            Thread.Sleep(25);
        }

        var pid = int.Parse(File.ReadAllText(file));
        Remember(pid);

        return pid;
    }

    // ---------- supervise-tree: the product's SupervisedTeardown, under a real subreaper ----------

    private async Task<JsonElement> SupervisedAsync(string mode)
    {
        var (exit, output, error) = await FinishedAsync(StartFakeCli("supervise-tree", mode, _dir));
        exit.Should().Be(0, error);

        return JsonDocument.Parse(output.Trim()).RootElement.Clone();
    }

    private static string StateOf(JsonElement report, string file) => report.GetProperty("states").GetProperty(file).GetString()!;

    [Fact]
    public async Task AnAgentThatExitsFirst_LeavesNoSameGroupAndNoSetsidChild()
    {
        OnLinux();

        var report = await SupervisedAsync("orphans");

        var c1 = PidIn(_dir.At("c1.pid"));
        var c2 = PidIn(_dir.At("c2.pid"));
        report.GetProperty("survivors").GetArrayLength().Should().Be(0);
        StateOf(report, "c1.pid").Should().Be("gone", $"the same-group grandchild of an agent that crashed first is still alive (pid {c1})");
        StateOf(report, "c2.pid").Should().Be("gone", $"the setsid grandchild of an agent that crashed first is still alive (pid {c2})");
        (await GoneSoonAsync(c1)).Should().BeTrue();
        (await GoneSoonAsync(c2)).Should().BeTrue();
    }

    /// <summary>C24 (3): a pid file that holds no pid is a NAMED state in the report, never the fake's exit 97 (CI run 39760284371: <c>FormatException: The input string '' was not in a correct format.</c>).</summary>
    [Fact]
    public async Task APidFileThatHoldsNoPid_IsReportedAsUnreadable_NotAnEscapedException()
    {
        OnLinux();
        File.WriteAllText(_dir.At("torn.pid"), string.Empty);

        var report = await SupervisedAsync("orphans");

        StateOf(report, "torn.pid").Should().Be("unreadable: ''", "an empty pid file is a torn write the report names, not a FormatException that ends the fake with exit 97");
    }

    [Fact]
    public async Task AGrandchildBornAfterTheFirstListing_IsFoundByTheReListing()
    {
        OnLinux();

        var report = await SupervisedAsync("late");

        var late = PidIn(_dir.At("late.pid"));
        File.ReadAllText(_dir.At("listed.txt")).Split(',').Should().NotContain(late.ToString(), "the grandchild was born after the first listing");
        report.GetProperty("survivors").GetArrayLength().Should().Be(0, "a grandchild born after the first listing survived the confirmation");
        StateOf(report, "late.pid").Should().Be("gone");
        (await GoneSoonAsync(late)).Should().BeTrue();
    }

    [Fact]
    public async Task AGrandchildThatExitedByItself_IsReapedByTheDrain_NotASurvivor()
    {
        OnLinux();

        var report = await SupervisedAsync("zombie");

        var exited = PidIn(_dir.At("g.pid"));
        report.GetProperty("zombieBefore").GetBoolean().Should().BeTrue("the precondition: the orphan really was a zombie of the shim before the teardown");
        report.GetProperty("survivors").GetArrayLength().Should().Be(0, $"an exited grandchild (state Z, pid {exited}) was recorded as a survivor");
        StateOf(report, "g.pid").Should().Be("gone", "a zombie stayed in /proc after the drain");
    }

    [Fact]
    public async Task TheDrainNeverTakesAManagedChildsStatus()
    {
        OnLinux();

        var report = await SupervisedAsync("managed");

        report.GetProperty("managedExitedBefore").GetBoolean().Should().BeTrue("the precondition: the managed child had already exited before the teardown, so the teardown has nothing to kill in it");
        // 137 would mean the child was killed while still alive (the precondition above missed); a status taken from under .NET would end the supervise-tree process itself (exit 134, FailFast on ECHILD) and fail the earlier exit-0 assertion.
        report.GetProperty("managedExit").GetInt32().Should().Be(17, "the managed child's own exit code was lost: 137 means it was killed while still alive, anything else means the drain took its status (expected 17)");
        report.GetProperty("survivors").GetArrayLength().Should().Be(0);
        StateOf(report, "c2.pid").Should().Be("gone");
    }

    [Fact]
    public async Task AManagedChildThatIsStillAliveAtTheTeardown_IsKilledLikeEveryDescendant()
    {
        OnLinux();

        var report = await SupervisedAsync("managed-live");

        report.GetProperty("managedAliveBefore").GetBoolean().Should().BeTrue("the precondition: the managed child was still alive when the teardown started");
        report.GetProperty("managedExit").GetInt32().Should().Be(137, "DL9: every live descendant is SIGKILLed, a child .NET started too — only its STATUS is never taken by the drain");
        report.GetProperty("survivors").GetArrayLength().Should().Be(0);
        StateOf(report, "c2.pid").Should().Be("gone");
    }

    // ---------- shim-acp: the ACP client as --ask-acp runs it ----------

    private string MarkerPath => _dir.At("locks", "0123456789abcdef.survivors.json");

    private Process StartShim(FakeAcpAgent agent, int deadlineSeconds) => StartFakeCli("shim-acp", agent.Script, MarkerPath, agent.Cwd, deadlineSeconds.ToString());

    private static bool Prompted(FakeAcpAgent agent) => agent.ReceivedMethods().Contains("session/prompt");

    private static async Task WaitAsync(Func<bool> done, string what)
    {
        var until = DateTime.UtcNow + Patience;
        while (!done() && DateTime.UtcNow < until)
        {
            await Task.Delay(25);
        }

        done().Should().BeTrue(what);
    }

    [Fact]
    public async Task ThroughTheShim_AnAgentThatExitsFirst_LeavesNoGrandchildAndNoMarker()
    {
        OnLinux();
        using var agent = new FakeAcpAgent(string.Empty, a => a.Grandchild(), _ => FakeAcpAgent.GrandchildSetsid(_dir.At("c2.pid")), _ => FakeAcpAgent.Exit(0));

        var (exit, output, error) = await FinishedAsync(StartShim(agent, 60));

        exit.Should().Be(69, error);
        var report = JsonDocument.Parse(output.Trim()).RootElement;
        report.GetProperty("end").GetString().Should().Be("Protocol");
        report.GetProperty("survivors").GetArrayLength().Should().Be(0);
        var sameGroup = PidIn(agent.PidFile);
        var ownSession = PidIn(_dir.At("c2.pid"));
        (await GoneSoonAsync(sameGroup)).Should().BeTrue($"the same-group grandchild of an agent that crashed first is still alive (pid {sameGroup})");
        (await GoneSoonAsync(ownSession)).Should().BeTrue($"the setsid grandchild of an agent that crashed first is still alive (pid {ownSession})");
        File.Exists(MarkerPath).Should().BeFalse("a marker remained after a clean teardown");
    }

    [Fact]
    public async Task ThroughTheShim_AGrandchildThatExitedBeforeTheTeardown_IsNotASurvivor_AndNoMarkerRemains()
    {
        OnLinux();
        using var agent = new FakeAcpAgent(string.Empty, _ => FakeAcpAgent.GrandchildExits(_dir.At("g.pid")), _ => FakeAcpAgent.Hang);

        var (exit, output, error) = await FinishedAsync(StartShim(agent, 3));

        exit.Should().Be(69, error);
        var report = JsonDocument.Parse(output.Trim()).RootElement;
        report.GetProperty("end").GetString().Should().Be("Deadline");
        report.GetProperty("survivors").GetArrayLength().Should().Be(0, $"an exited grandchild (state Z, pid {PidIn(_dir.At("g.pid"))}) was recorded as a survivor");
        File.Exists(MarkerPath).Should().BeFalse("a marker remained after a clean teardown");
    }

    [Fact]
    public async Task AShimSigtermedWhileItsAgentLives_EndsTheTree()
    {
        OnLinux();
        using var agent = new FakeAcpAgent(string.Empty, _ => FakeAcpAgent.GrandchildSetsid(_dir.At("c2.pid")), _ => FakeAcpAgent.Hang);
        var shim = StartShim(agent, 120);
        await WaitAsync(() => Prompted(agent) && File.Exists(_dir.At("c2.pid")), "the agent never got to its prompt");
        var grandchild = PidIn(_dir.At("c2.pid"));

        _ = Kill(shim.Id, 15);
        var (exit, output, error) = await FinishedAsync(shim);

        exit.Should().Be(69, error);
        var report = JsonDocument.Parse(output.Trim()).RootElement;
        report.GetProperty("end").GetString().Should().Be("Cancelled", "the report is printed as every end is");
        var agentPid = report.GetProperty("agentPid").GetInt32();
        Remember(agentPid);
        (await GoneSoonAsync(agentPid)).Should().BeTrue($"after SIGTERM the agent (pid {agentPid}) is still alive");
        (await GoneSoonAsync(grandchild)).Should().BeTrue("after SIGTERM the setsid grandchild is still alive");
        File.Exists(MarkerPath).Should().BeFalse();
    }

    [Fact]
    public async Task AShimSigkilledLeavesARunningMarkerNamingTheAgent_WhichTheNextLaunchReconciles()
    {
        OnLinux();
        using var agent = new FakeAcpAgent(string.Empty, _ => FakeAcpAgent.AwaitCancel);
        var shim = StartShim(agent, 120);
        await WaitAsync(() => Prompted(agent) && KimiSurvivorMarker.Read(MarkerPath) is MarkerRead.Read { Marker.Marker.Tree.Count: 1 }, "the shim never wrote its running marker");

        _ = Kill(shim.Id, 9);
        await shim.WaitForExitAsync(TestContext.Current.CancellationToken);

        var marker = KimiSurvivorMarker.Read(MarkerPath).Should().BeOfType<MarkerRead.Read>().Subject.Marker;
        marker.Marker.Phase.Should().Be(ReaderPhases.Running);
        var agentPid = marker.Marker.Tree.Single().Pid;
        Remember(agentPid);
        marker.Marker.Unidentified.Should().BeFalse("the marker written before the start does not name the agent");
        (await GoneSoonAsync(agentPid)).Should().BeTrue("the agent leaves on its stdin's EOF once the shim is dead");

        var verdict = await KimiSurvivorGate.ReconcileAsync(MarkerPath, () => new KimiPresence.NoKimi(), ReconcileSeams.Real);

        verdict.Should().BeOfType<SurvivorVerdict.Cleared>("the next launch reconciles the dead shim's marker");
        File.Exists(MarkerPath).Should().BeFalse();
    }

    // ---------- L-N3: the real-process arms of the survivor gate (the seam arm is KimiSurvivorGateTests') ----------

    [Fact]
    public async Task AStandingMarkerNamingALivingCoaiChild_IsEndedByIdentity_ThenCleared()
    {
        OnLinux();
        var sleeper = StartFakeCli("sleep", "600000");
        var identity = new ProcessIdentity(sleeper.Id, ProcessTracking.StartedAt(sleeper.Id)!.Value);
        KimiSurvivorMarker.Write(
            MarkerPath, new KimiSurvivorMarkerDto(new ReaderMarkerDto(ReaderPhases.Running, 1, DateTime.UtcNow, DateTimeOffset.UtcNow, [identity]), "/owned/kimi-root", "/people/sign-in"));

        var verdict = await KimiSurvivorGate.ReconcileAsync(MarkerPath, () => new KimiPresence.NoKimi(), ReconcileSeams.Real);

        verdict.Should().BeOfType<SurvivorVerdict.Cleared>($"a launch proceeded on a sign-in whose previous tree still has a survivor (pid {sleeper.Id})");
        await sleeper.WaitForExitAsync(TestContext.Current.CancellationToken);
        sleeper.ExitCode.Should().Be(137, "ended by SIGKILL through its pidfd");
        File.Exists(MarkerPath).Should().BeFalse();
    }

    [Fact]
    public async Task AStandingMarkerWhoseStartDiffers_LeavesTheReusedNumberAlone()
    {
        OnLinux();
        var bystander = StartFakeCli("sleep", "600000");
        var reused = new ProcessIdentity(bystander.Id, ProcessTracking.StartedAt(bystander.Id)!.Value.AddHours(-3));
        KimiSurvivorMarker.Write(
            MarkerPath, new KimiSurvivorMarkerDto(new ReaderMarkerDto(ReaderPhases.Running, 1, DateTime.UtcNow, DateTimeOffset.UtcNow, [reused]), "/owned/kimi-root", "/people/sign-in"));

        var verdict = await KimiSurvivorGate.ReconcileAsync(MarkerPath, () => new KimiPresence.NoKimi(), ReconcileSeams.Real);

        verdict.Should().BeOfType<SurvivorVerdict.Cleared>("the process under a reused number is nothing of ours");
        bystander.HasExited.Should().BeFalse("a number that belongs to somebody else now was signalled");
    }
}

/// <summary>Which listed descendants the drain takes the status of — pure, so it runs everywhere (the real drain is <see cref="SupervisedTeardownLinuxTests"/>').</summary>
public sealed class ReapSelectionTests
{
    private static readonly DateTime Started = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private static ProcessIdentity Member(int pid) => new(pid, Started);

    [Fact]
    public void OnlyAnExitedEntry_ThatThisProcessDidNotStart_IsReaped()
    {
        IReadOnlyList<ProcessIdentity> listed = [Member(10), Member(11), Member(12), Member(13)];

        var targets = ReapSelection.Of(listed, new HashSet<int> { 11 }, pid => pid is 11 or 12);

        targets.Should().Equal([12], "11 is a child .NET started and reaps itself, 10 and 13 are not zombies");
    }

    [Fact]
    public void ANumberThatWasNotListed_IsNeverATarget() =>
        ReapSelection.Of([Member(10)], new HashSet<int>(), _ => true).Should().Equal([10]);
}

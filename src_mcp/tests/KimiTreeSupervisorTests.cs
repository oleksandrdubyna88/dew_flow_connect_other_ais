using System.Diagnostics;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The survivor marker's phases around the teardown (DL9; A3 §6.1, §8): <c>spawning</c> before the agent starts (a failed write starts nothing), <c>running [agent]</c> right after the start — what a
/// SIGKILLed shim leaves behind — <c>running [members]</c> BEFORE any signal, deleted only on a confirmed-empty re-listing, kept (and naming the survivors) otherwise. The teardown is a seam here;
/// the real one runs in <c>SupervisedTeardownLinuxTests</c>.
/// </summary>
public sealed class KimiTreeSupervisorTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly TempDir _dir = TempDir.For("testsToDelete-tree-supervisor-");
    private readonly List<string> _notes = [];

    private string MarkerPath => _dir.At("locks", "0123456789abcdef.survivors.json");

    public void Dispose() => _dir.Dispose();

    private static ProcessIdentity Member(int pid) => new(pid, Started);

    private KimiSurvivorMarkerDto OnDisk() => KimiSurvivorMarker.Read(MarkerPath).Should().BeOfType<MarkerRead.Read>().Subject.Marker;

    private KimiTreeSupervisor Ready(KimiTreeSupervisor.Teardown teardown) =>
        KimiTreeSupervisor.Begin(MarkerPath, "/owned/kimi-root", "/people/sign-in", 4242, Started, teardown, _notes.Add).Should().BeOfType<SupervisorBegin.Ready>().Subject.Supervisor;

    private static KimiTreeSupervisor.Teardown Teardown(IReadOnlyList<ProcessIdentity> listed, IReadOnlyList<ProcessIdentity> survivors, Action? during = null) =>
        (_, _, _, onListed) =>
        {
            onListed(listed);
            during?.Invoke();

            return Task.FromResult(new TreeEnd(survivors));
        };

    [Fact]
    public void Begin_WritesTheSpawningMarker_NamingNothing_OwnedByTheShim()
    {
        _ = Ready(Teardown([], []));

        var marker = OnDisk();
        marker.Marker.Phase.Should().Be(ReaderPhases.Spawning);
        marker.Marker.Tree.Should().BeEmpty();
        marker.Marker.OwnerPid.Should().Be(4242);
        marker.Marker.OwnerStartedUtc.Should().Be(Started);
        marker.OwnedRoot.Should().Be("/owned/kimi-root");
        marker.SignInFolder.Should().Be("/people/sign-in");
    }

    [Fact]
    public void AMarkerThatCannotBeWritten_StartsNothing_WithTheSentence()
    {
        Directory.CreateDirectory(_dir.At("locks"));
        Directory.CreateDirectory(MarkerPath);

        var begin = KimiTreeSupervisor.Begin(MarkerPath, "/o", "/s", 1, Started, Teardown([], []), _notes.Add);

        begin.Should().BeOfType<SupervisorBegin.Refused>().Which.Sentence.Should().Contain(MarkerPath).And.Contain("could not be written").And.Contain("does not start Kimi without it");
    }

    [Fact]
    public void Started_WritesRunningNamingTheAgent_WhichIsWhatASigkilledShimLeaves()
    {
        var supervisor = Ready(Teardown([], []));

        supervisor.Started(Member(5150));

        var marker = OnDisk();
        marker.Marker.Phase.Should().Be(ReaderPhases.Running);
        marker.Marker.Tree.Should().Equal(Member(5150));
        marker.Marker.Unidentified.Should().BeFalse("the marker written before the start does not name the agent");
    }

    [Fact]
    public async Task TheListedMembers_AreWrittenBeforeTheTeardownGoesOn_AndACleanEndDeletesTheMarker()
    {
        ReaderMarkerDto? seenBeforeTheSignal = null;
        var supervisor = Ready(Teardown([Member(5150), Member(5151)], [], during: () => seenBeforeTheSignal = OnDisk().Marker));
        supervisor.Started(Member(5150));

        var end = await supervisor.EndAsync(Process.GetCurrentProcess());

        seenBeforeTheSignal.Should().NotBeNull();
        seenBeforeTheSignal!.Tree.Select(member => member.Pid).Should().Equal(5150, 5151);
        end.Survivors.Should().BeEmpty();
        File.Exists(MarkerPath).Should().BeFalse("a marker remained after a clean teardown");
    }

    [Fact]
    public async Task ASurvivor_KeepsTheMarker_NamingTheMembersAndTheSurvivorBornAfterTheListing_AndSaysTheSentence()
    {
        var supervisor = Ready(Teardown([Member(5150)], [Member(5160)]));
        supervisor.Started(Member(5150));

        var end = await supervisor.EndAsync(Process.GetCurrentProcess());

        end.Survivors.Should().Equal(Member(5160));
        OnDisk().Marker.Tree.Select(member => member.Pid).Should().Equal(5150, 5160);
        _notes.Should().ContainSingle().Which.Should().Contain(MarkerPath).And.Contain("5160").And.Contain("safe to delete once");
    }

    [Fact]
    public async Task AWriteThatFailsDuringTheTeardown_NeverSkipsTheEnd_ThePreviousMarkerStands_AndItIsSaid()
    {
        var supervisor = Ready(Teardown([Member(5150)], []));
        supervisor.Started(Member(5150));
        File.Delete(MarkerPath);
        Directory.CreateDirectory(MarkerPath);

        var end = await supervisor.EndAsync(Process.GetCurrentProcess());

        end.Survivors.Should().BeEmpty("the teardown ran to its end");
        _notes.Should().NotBeEmpty().And.OnlyContain(note => note.Contains(MarkerPath));
    }

    // ---------- a start time is never invented (the E5 risk consultant, gap 2) ----------

    [Fact]
    public void AShimWhoseOwnStartTimeCannotBeRead_RefusesTheTurn_AndWritesNoMarker()
    {
        var begin = KimiTreeSupervisor.Begin(MarkerPath, "/o", "/s", 4242, _ => null, Teardown([], []), _notes.Add);

        begin.Should().BeOfType<SupervisorBegin.Refused>("a marker that names its owner by an invented start time makes a live owner read as gone").Which.Sentence.Should().Contain("start time").And.Contain("4242");
        File.Exists(MarkerPath).Should().BeFalse("nothing is written, so nothing can be reconciled wrongly");
    }

    [Fact]
    public async Task AMarkerNamesItsShimByTheStartTimeReallyRead_SoALaterCorrectReadStillFindsItAlive_AndNothingIsEnded()
    {
        var realStart = DateTime.UtcNow.AddSeconds(-10);
        var proc = new FakeProcReads().With(4242, "S");
        _ = KimiTreeSupervisor.Begin(MarkerPath, "/o", "/s", 4242, _ => realStart, Teardown([], []), _notes.Add).Should().BeOfType<SupervisorBegin.Ready>();
        var terminated = new List<ProcessIdentity>();
        var seams = new ReconcileSeams(
            member => { terminated.Add(member); return true; },
            members => Task.FromResult(members),
            _ => [],
            member => LivenessCheck.Linux(member, proc, _ => realStart));

        var verdict = await KimiSurvivorGate.ReconcileAsync(MarkerPath, () => new KimiPresence.NoKimi(), seams);

        verdict.Should().BeOfType<SurvivorVerdict.Blocked>("the shim is alive and its marker says so").Which.Code.Should().Be(BlockedReasons.ReaderOwnerAlive);
        terminated.Should().BeEmpty("a made-up start time would have made the live shim read as gone, and the reconcile would have ended an active turn's agent");
        File.Exists(MarkerPath).Should().BeTrue("and the marker is not cleared");
    }

    [Fact]
    public void AnAgentWhoseStartTimeCannotBeRead_IsToldAsAPidOnly_AndTheMarkerStaysAsItWas()
    {
        var supervisor = Ready(Teardown([], []));

        supervisor.StartedUnidentified(31337);

        OnDisk().Marker.Phase.Should().Be(ReaderPhases.Spawning, "no identity was made up: the nameless marker stays, and it is blocking while a kimi runs");
        OnDisk().Marker.Tree.Should().BeEmpty();
    }

    // ---------- through the ACP client: the supervisor is told, in order, and the survivors reach the outcome ----------

    private sealed class Recording : ITreeSupervisor
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<ProcessIdentity> Survivors { get; init; } = [];

        public void Started(ProcessIdentity agent) => Calls.Add($"started:{agent.Pid}");

        public void StartedUnidentified(int pid) => Calls.Add($"unidentified:{pid}");

        public async Task<TreeEnd> EndAsync(Process agent)
        {
            Calls.Add($"end:{agent.Id}");
            await UnsupervisedTree.Instance.EndAsync(agent);

            return new TreeEnd(Survivors);
        }
    }

    [Fact]
    public async Task TheClient_TellsTheSupervisorOfTheStart_ThenHandsItTheEnd_AndItsSurvivorsReachTheOutcome()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("hello")]);
        var supervisor = new Recording { Survivors = [Member(31337)] };
        var client = new AcpClient(agent.Profile(), new FolderReadBoundary(agent.Cwd, []), TimeSpan.FromSeconds(5), supervisor);

        var outcome = await client.AskAsync(agent.Ask(), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        supervisor.Calls.Should().Equal($"started:{outcome.AgentPid}", $"end:{outcome.AgentPid}");
        outcome.Survivors.Should().Equal(Member(31337));
    }

    [Fact]
    public async Task TheDefaultClient_KeepsTodaysEnd_NoSurvivors()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("hello")]);

        var outcome = await new AcpClient(agent.Profile(), new FolderReadBoundary(agent.Cwd, [])).AskAsync(agent.Ask(), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.Answered);
        outcome.Survivors.Should().BeEmpty();
    }

    [Fact]
    public void AHoldWithoutASupervisor_IsAnUnsupervisedOne_SoEveryExistingHolderIsAsItWas()
    {
        new AcpHolding.Held(new Disposed()).Supervisor.Should().BeSameAs(UnsupervisedTree.Instance);
        ((AcpHolding.Held)AcpHolding.Nothing).Supervisor.Should().BeSameAs(UnsupervisedTree.Instance);
    }

    private sealed class Disposed : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

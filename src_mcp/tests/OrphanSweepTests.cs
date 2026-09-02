using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Which recorded children are orphans — and, far more importantly, which are not.
/// </summary>
/// <remarks>
/// <para>Reported from a macOS checkout: an Antigravity reviewer started at 00:03 was still alive
/// at 10:00, hours after its round finished, its vendor removed from the configuration, and the
/// server that launched it long gone. <see cref="ProcessLauncher"/> kills an overrunning tree, but
/// the kill is done by the PARENT — so when <c>coai-mcp</c> itself goes away, which is what
/// happens every time an MCP client restarts, its in-flight reviewers are orphaned with nothing
/// left to stop them.</para>
///
/// <para>Most of what follows tests the REFUSALS. The vendor CLIs are programs a person also runs
/// by hand, so the failure mode of a reaper is not a missed orphan — it is killing somebody's
/// terminal session, and that must be impossible rather than unlikely.</para>
/// </remarks>
public class OrphanSweepTests
{
    private static readonly DateTime Start = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

    private const int Me = 1000;
    private const int DeadServer = 2000;
    private const int OtherServer = 3000;

    private static TrackedProcess Child(int pid, int owner, DateTime? started = null) =>
        new(pid, started ?? Start, owner, Start, $"codex/Architecture#{pid}");

    /// <summary>A world where only the listed pids exist, each started when it says.</summary>
    private static Func<int, DateTime?> World(params (int Pid, DateTime Started)[] alive) =>
        pid => alive.FirstOrDefault(p => p.Pid == pid) is { Pid: > 0 } found ? found.Started : null;

    [Fact]
    public void AChildWhoseServerIsGoneIsReaped()
    {
        var plan = OrphanSweep.Plan(
            [Child(5001, DeadServer)],
            World((5001, Start)),
            currentPid: Me);

        plan.Reap.Should().ContainSingle().Which.Pid.Should().Be(5001);
        plan.Forget.Should().BeEmpty("it is still running; the record goes once the kill has");
    }

    [Fact]
    public void OurOwnChildIsNeverTouched()
    {
        // The sweep runs on `open`, which happens while rounds are in flight. A reaper that killed
        // the reviewers of the server running it would be worse than no reaper at all.
        var plan = OrphanSweep.Plan(
            [Child(5001, Me)],
            World((5001, Start), (Me, Start)),
            currentPid: Me);

        plan.Reap.Should().BeEmpty();
        plan.Forget.Should().BeEmpty("an in-flight child is not a stale record either");
    }

    [Fact]
    public void AChildOfANOTHERLiveServerIsLeftAlone()
    {
        // Two servers over one data directory is ordinary — an editor and a CLI — and each other's
        // reviewers are not rubbish to be collected.
        var plan = OrphanSweep.Plan(
            [Child(5001, OtherServer)],
            World((5001, Start), (OtherServer, Start)),
            currentPid: Me);

        plan.Reap.Should().BeEmpty();
        plan.Forget.Should().BeEmpty();
    }

    [Fact]
    public void APidReusedBySomebodyElseIsNeverKilled()
    {
        // THE dangerous case. The child is long gone, the operating system handed its number to an
        // unrelated program, and the only thing separating that program from being killed is the
        // recorded start time.
        var stranger = Start.AddHours(3);

        var plan = OrphanSweep.Plan(
            [Child(5001, DeadServer)],
            World((5001, stranger)),
            currentPid: Me);

        plan.Reap.Should().BeEmpty("a different process now holds that pid");
        plan.Forget.Should().ContainSingle().Which.Pid.Should().Be(5001);
    }

    [Fact]
    public void AnOwnerPidReusedBySomebodyElseStillCountsAsGone()
    {
        // The mirror of the case above: the number that used to be the server is now something
        // else, so the server IS gone and its child IS an orphan.
        var plan = OrphanSweep.Plan(
            [Child(5001, DeadServer)],
            World((5001, Start), (DeadServer, Start.AddHours(3))),
            currentPid: Me);

        plan.Reap.Should().ContainSingle().Which.Pid.Should().Be(5001);
    }

    [Fact]
    public void AChildThatAlreadyExitedIsForgottenRatherThanKilled()
    {
        var plan = OrphanSweep.Plan(
            [Child(5001, DeadServer)],
            World(),
            currentPid: Me);

        plan.Reap.Should().BeEmpty();
        plan.Forget.Should().ContainSingle();
    }

    [Fact]
    public void ASecondOfSlackIsAllowed_BecauseTheTimeRoundTripsThroughJson()
    {
        var plan = OrphanSweep.Plan(
            [Child(5001, DeadServer)],
            World((5001, Start.AddMilliseconds(400))),
            currentPid: Me);

        plan.Reap.Should().ContainSingle("400 ms of reporting difference is the same process");
    }

    [Fact]
    public void EachRecordIsJudgedOnItsOwn()
    {
        var plan = OrphanSweep.Plan(
            [
                Child(5001, DeadServer),                        // orphan
                Child(5002, Me),                                // ours
                Child(5003, OtherServer),                       // somebody else's, alive
                Child(5004, DeadServer),                        // exited
            ],
            World((5001, Start), (5002, Start), (5003, Start), (Me, Start), (OtherServer, Start)),
            currentPid: Me);

        plan.Reap.Select(r => r.Pid).Should().Equal(5001);
        plan.Forget.Select(r => r.Pid).Should().Equal(5004);
    }

    [Fact]
    public void NothingRecordedIsNothingToDo()
    {
        var plan = OrphanSweep.Plan([], World(), currentPid: Me);

        plan.Reap.Should().BeEmpty();
        plan.Forget.Should().BeEmpty();
    }
}

using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>ReaderReconcile</c> after its move to the runners, with the tri-state question (DL9; A3 §6.2): once the confirmation says nobody is left, every member is asked <c>Alive / Gone / Unknown</c>, and ONE
/// member that cannot be checked keeps the marker — <c>StartedAt</c>'s <c>null</c> for every failure is the safe answer for a kill and the unsafe one for clearing. The server's own tests
/// (<c>ReaderBarrierTests</c>, <c>GaugeReaderCrashHarnessTests</c>) are the proof the move changed nothing for them.
/// </summary>
public sealed class ReaderReconcileTriStateTests
{
    private static readonly DateTime Started = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private static ProcessIdentity Member(int pid) => new(pid, Started);

    private static ReaderMarkerDto Marker(params ProcessIdentity[] tree) => new(ReaderPhases.Running, 424242, Started, DateTimeOffset.UtcNow, tree);

    private static Task<Reconciled> RunAsync(ReaderMarkerDto marker, Func<ProcessIdentity, Liveness> liveness, IReadOnlyList<ProcessIdentity>? survivors = null) =>
        ReaderReconcile.RunAsync(marker, _ => true, _ => Task.FromResult(survivors ?? []), _ => [], liveness);

    [Fact]
    public async Task EveryMemberGone_IsCleared() =>
        (await RunAsync(Marker(Member(1), Member(2)), _ => new Liveness.Gone("no such process"))).Should().BeOfType<Reconciled.Cleared>();

    [Theory]
    [InlineData("errno 13 (EACCES) reading /proc/2/stat")]
    [InlineData("errno 5 (EIO) reading /proc/2/stat")]
    [InlineData("not a /proc stat line: garbage")]
    public async Task OneMemberThatCannotBeChecked_KeepsTheMarker_NamingTheError(string reason)
    {
        var reconciled = await RunAsync(Marker(Member(1), Member(2)), member => member.Pid == 2 ? new Liveness.Unknown(reason) : new Liveness.Gone("no such process"));

        var blocked = reconciled.Should().BeOfType<Reconciled.Blocked>("the next launch's reconcile cleared a marker whose member it could not read").Subject;
        blocked.Code.Should().Be(BlockedReasons.ReaderMemberUnverifiable);
        blocked.Detail.Should().Contain("pid 2").And.Contain(reason);
    }

    [Fact]
    public async Task AMemberStillAliveAfterAnEmptyConfirmation_IsAContradiction_AndKeepsTheMarker()
    {
        var reconciled = await RunAsync(Marker(Member(1)), _ => new Liveness.Alive());

        reconciled.Should().BeOfType<Reconciled.Blocked>().Which.Code.Should().Be(BlockedReasons.ReaderTerminationFailed);
    }

    [Fact]
    public async Task ASurvivorOfTheConfirmation_IsBlocked_WithoutAskingTheTriState()
    {
        var asked = false;

        var reconciled = await RunAsync(Marker(Member(1)), _ => { asked = true; return new Liveness.Gone("x"); }, survivors: [Member(1)]);

        reconciled.Should().BeOfType<Reconciled.Blocked>().Which.Code.Should().Be(BlockedReasons.ReaderTerminationFailed);
        asked.Should().BeFalse();
    }

    [Fact]
    public async Task TheDescendantsAreAskedToo()
    {
        var asked = new List<int>();

        var reconciled = await ReaderReconcile.RunAsync(
            Marker(Member(1)), _ => true, _ => Task.FromResult<IReadOnlyList<ProcessIdentity>>([]), member => member.Pid == 1 ? [Member(7)] : [],
            member => { asked.Add(member.Pid); return member.Pid == 7 ? new Liveness.Unknown("EIO") : new Liveness.Gone("x"); });

        asked.Should().BeEquivalentTo([1, 7]);
        reconciled.Should().BeOfType<Reconciled.Blocked>().Which.Code.Should().Be(BlockedReasons.ReaderMemberUnverifiable);
    }

    [Fact]
    public async Task AnUnidentifiedMarker_StillBlocks_AsItAlwaysDid() =>
        (await ReaderReconcile.RunAsync(new ReaderMarkerDto(ReaderPhases.Spawning, 1, Started, DateTimeOffset.UtcNow))).Should().Be(new Reconciled.Blocked(BlockedReasons.ReaderIdentityMissing));

    [Fact]
    public async Task TheFourSeamForm_UsesTheRealTriState_AFakePidNobodyHoldsIsGone() =>
        (await ReaderReconcile.RunAsync(Marker(Member(int.MaxValue)), _ => true, _ => Task.FromResult<IReadOnlyList<ProcessIdentity>>([]), _ => [])).Should().BeOfType<Reconciled.Cleared>();
}

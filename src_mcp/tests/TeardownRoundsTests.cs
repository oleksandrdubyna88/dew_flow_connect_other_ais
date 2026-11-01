using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The confirmation of a supervised teardown over seams (DL9; the E5 risk consultant, 2026-10-17, gap 3): only a member that is PROVABLY gone leaves the list. One whose start time cannot be read — the listing omits
/// what it cannot identify, and a two-state "alive?" calls it dead — is kept, is a survivor at the bound, and so keeps the marker. Everywhere: no real process, no platform call.
/// </summary>
public sealed class TeardownRoundsTests
{
    private static readonly DateTime Started = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    private static ProcessIdentity Member(int pid, DateTime? started = null) => new(pid, started ?? Started);

    private sealed class Rounds(Func<int, IReadOnlyList<ProcessIdentity>> descendants, Func<ProcessIdentity, int, Liveness> liveness, int roundsUntilBound)
    {
        public List<IReadOnlyList<ProcessIdentity>> Killed { get; } = [];

        public int Round { get; private set; }

        public RoundSeams Seams => new(
            () => descendants(Round),
            _ => { },
            member => liveness(member, Round),
            members => Killed.Add(members),
            () =>
            {
                Round++;

                return Task.CompletedTask;
            },
            () => Round >= roundsUntilBound);
    }

    [Fact]
    public async Task APreviouslyListedMember_WhoseStartTimeCannotBeReadDuringConfirmation_IsASurvivor_NotDropped()
    {
        var rounds = new Rounds(_ => [], (_, _) => new Liveness.Unknown("the start time could not be read"), roundsUntilBound: 3);

        var survivors = await TeardownRounds.ConfirmedAsync([Member(500)], rounds.Seams);

        survivors.Should().Equal([Member(500)], "the member was listed before the signal and was never proven gone: an empty answer would clear the marker over a possible refresher");
    }

    [Fact]
    public async Task AMemberThatIsProvablyGone_LeavesTheList_AndNothingIsLeft()
    {
        var rounds = new Rounds(_ => [], (_, round) => round < 2 ? new Liveness.Alive() : new Liveness.Gone("no such process"), roundsUntilBound: 10);

        var survivors = await TeardownRounds.ConfirmedAsync([Member(500)], rounds.Seams);

        survivors.Should().BeEmpty();
        rounds.Killed.Should().HaveCount(2, "it was ended again each round it was still there");
    }

    [Fact]
    public async Task AMemberThatIsUnknownAndThenGone_IsNotASurvivor()
    {
        var rounds = new Rounds(_ => [], (_, round) => round == 0 ? new Liveness.Unknown("EIO") : new Liveness.Gone("no such process"), roundsUntilBound: 10);

        (await TeardownRounds.ConfirmedAsync([Member(500)], rounds.Seams)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnAliveMemberAtTheBound_IsASurvivor()
    {
        var rounds = new Rounds(_ => [], (_, _) => new Liveness.Alive(), roundsUntilBound: 2);

        (await TeardownRounds.ConfirmedAsync([Member(500)], rounds.Seams)).Should().Equal(Member(500));
    }

    [Fact]
    public async Task AnOrphanBornAfterTheFirstListing_IsFoundByTheRelisting_AndEnded()
    {
        var born = Member(900);
        var rounds = new Rounds(round => round == 0 ? [born] : [], (member, round) => member.Pid == 900 && round < 1 ? new Liveness.Alive() : new Liveness.Gone("no such process"), roundsUntilBound: 10);

        (await TeardownRounds.ConfirmedAsync([], rounds.Seams)).Should().BeEmpty();
        rounds.Killed.Should().ContainSingle().Which.Should().Equal(born);
    }

    [Fact]
    public async Task AReusedPid_IsTheOldMemberGone_AndTheNewProcessJudgedOnItsOwn()
    {
        var stranger = Member(500, Started.AddMinutes(5));
        var rounds = new Rounds(_ => [stranger], (member, _) => member.StartedUtc == Started ? new Liveness.Gone("the pid is reused") : new Liveness.Alive(), roundsUntilBound: 2);

        (await TeardownRounds.ConfirmedAsync([Member(500)], rounds.Seams)).Should().Equal(stranger);
    }

    [Fact]
    public void TheSameProcessRead_TwiceWithASlightlyDifferentStartTime_IsOneMember() =>
        TeardownRounds.Kept([Member(500), Member(500, Started.AddMilliseconds(200)), Member(501)], _ => new Liveness.Alive()).Select(member => member.Pid).Should().Equal(500, 501);
}

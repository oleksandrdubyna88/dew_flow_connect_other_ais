using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The tri-state liveness (DL9 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>, the cadence consult 8df043b8): <c>Gone</c> only for ENOENT, another start time or an exited entry;
/// <c>Unknown</c> for any other failure — never <c>Gone</c>, because a caller that CLEARS something because a process is gone must not read "could not look" as "gone".
/// Over a fake <c>/proc</c> and a fake start-time read, so every answer runs on every platform.
/// </summary>
public sealed class LivenessCheckTests
{
    private static readonly DateTime Started = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private static ProcessIdentity Member(int pid = 500) => new(pid, Started);

    private static Liveness Linux(FakeProcReads proc, Func<int, DateTime?>? startedAt = null, int pid = 500) =>
        LivenessCheck.Linux(Member(pid), proc, startedAt ?? (_ => Started));

    [Fact]
    public void ARunningProcessWhoseStartMatches_IsAlive() =>
        Linux(new FakeProcReads().With(500, "S")).Should().BeOfType<Liveness.Alive>();

    [Fact]
    public void AStartWithinTheIdentitySlack_IsTheSameProcess() =>
        Linux(new FakeProcReads().With(500, "S"), _ => Started + TimeSpan.FromMilliseconds(400)).Should().BeOfType<Liveness.Alive>("OrphanSweep.Same's slack is the one identity rule");

    [Fact]
    public void ADirectoryThatIsNotThere_IsGone() =>
        Linux(new FakeProcReads()).Should().Be(new Liveness.Gone("no such process"));

    [Fact]
    public void ENOENT_IsGone_OnTheReadItself()
    {
        var proc = new FakeProcReads();
        proc.Stats[500] = () => throw new FileNotFoundException("ENOENT");

        Linux(proc).Should().BeOfType<Liveness.Gone>();
    }

    [Theory]
    [InlineData("EACCES")]
    [InlineData("EIO")]
    public void AnUnreadableStat_IsUnknown_NotGone(string errno)
    {
        var proc = new FakeProcReads();
        proc.Stats[500] = () => errno == "EACCES" ? throw new UnauthorizedAccessException(errno) : throw new IOException(errno);

        Linux(proc).Should().BeOfType<Liveness.Unknown>().Which.Reason.Should().Contain("/proc/500/stat", "EACCES on /proc/<pid>/stat was read as Gone");
    }

    [Fact]
    public void AnUnparseableStat_IsUnknown_NotGone()
    {
        var proc = new FakeProcReads();
        proc.Stats[500] = () => "garbage";

        Linux(proc).Should().BeOfType<Liveness.Unknown>().Which.Reason.Should().Contain("not a /proc stat line");
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("X")]
    public void AnExitedEntry_IsGone_WhateverItsStartTime(string state) =>
        Linux(new FakeProcReads().With(500, state)).Should().Be(new Liveness.Gone("exited (zombie)"));

    [Fact]
    public void AZombieLeaderWhoseOtherThreadsStillRun_IsNotGone()
    {
        var proc = new FakeProcReads().With(500, "Z");
        proc.Tasks[500] = [500, 501];

        Linux(proc).Should().BeOfType<Liveness.Alive>("the group's other threads still hold what the process held (ExitedEntry's rule)");
    }

    [Fact]
    public void AStartThatCannotBeRead_OfAProcessThatStillRuns_IsUnknown_NotGone() =>
        Linux(new FakeProcReads().With(500, "S"), _ => null).Should().Be(new Liveness.Unknown("start time unreadable"), "a null StartedAt is every failure — including one for a process that is alive");

    [Fact]
    public void AStartThatCannotBeRead_IsGone_WhenTheStatIsReadAgainAndTheProcessIsNotThere()
    {
        var proc = new FakeProcReads().With(500, "S");

        Linux(proc, _ =>
        {
            proc.Stats.Remove(500);

            return null;
        }).Should().Be(new Liveness.Gone("no such process"), "the process left between the first read and the start-time read, and the second read of the stat proves it");
    }

    [Fact]
    public void AStartThatCannotBeRead_IsGone_WhenTheStatIsReadAgainAndTheEntryHasExited()
    {
        var proc = new FakeProcReads().With(500, "S");

        Linux(proc, _ =>
        {
            proc.With(500, "Z");

            return null;
        }).Should().Be(new Liveness.Gone("exited (zombie)"));
    }

    [Fact]
    public void TheStartTime_IsReadOnce_OnEveryPath()
    {
        var reads = 0;
        Func<int, DateTime?> counted = _ =>
        {
            reads++;

            return Started;
        };

        Linux(new FakeProcReads().With(500, "S"), counted);
        reads.Should().Be(1, "Linux: one read of the start time");

        reads = 0;
        LivenessCheck.Elsewhere(Member(), counted, _ => false);
        reads.Should().Be(1, "elsewhere: it was read twice, once to test for null and once to use");
    }

    // ---------- exact start ticks: one read of the stat line, one exact comparison ----------

    private static Liveness Exact(FakeProcReads proc, ulong ticks) => LivenessCheck.Linux(Member(), ticks, proc);

    [Fact]
    public void ExactTicks_TheSameStartTicks_IsAlive_AnyOtherIsAReusedPid()
    {
        var proc = new FakeProcReads().With(500, "S");

        Exact(proc, 4000).Should().BeOfType<Liveness.Alive>("the fake line's starttime is 4000");
        Exact(proc, 3999).Should().BeOfType<Liveness.Gone>().Which.Why.Should().Contain("the pid is reused").And.Contain("4000", "ONE tick apart is another process — no slack");
        Exact(proc, 4001).Should().BeOfType<Liveness.Gone>();
    }

    [Fact]
    public void ExactTicks_StillSeeAGoneProcess_AnExitedEntry_AndAnUnreadableStat()
    {
        Exact(new FakeProcReads(), 4000).Should().Be(new Liveness.Gone("no such process"));
        Exact(new FakeProcReads().With(500, "Z"), 4000).Should().Be(new Liveness.Gone("exited (zombie)"));
        var proc = new FakeProcReads();
        proc.Stats[500] = () => throw new UnauthorizedAccessException("EACCES");
        Exact(proc, 4000).Should().BeOfType<Liveness.Unknown>();
    }

    [Fact]
    public void ExactTicks_AStatLineWithoutTheTicks_IsUnknown_NeverASilentMatch()
    {
        var proc = new FakeProcReads();
        proc.Stats[500] = () => "500 (worker) S 1 500 500";

        Exact(proc, 4000).Should().BeOfType<Liveness.Unknown>().Which.Reason.Should().Contain("start ticks");
    }

    [Fact]
    public void AReusedPid_IsGone_NamingTheStranger() =>
        Linux(new FakeProcReads().With(500, "S"), _ => Started + TimeSpan.FromHours(2)).Should().BeOfType<Liveness.Gone>().Which.Why.Should().Contain("the pid is reused");

    [Fact]
    public void OffLinux_AnUnreadableStart_IsGoneOnlyWhenTheAbsenceIsProvable()
    {
        LivenessCheck.Elsewhere(Member(), _ => null, _ => true).Should().Be(new Liveness.Gone("no such process"));
        LivenessCheck.Elsewhere(Member(), _ => null, _ => false).Should().BeOfType<Liveness.Unknown>("a process another account owns refuses its start time exactly as an absent one does");
        LivenessCheck.Elsewhere(Member(), _ => Started, _ => true).Should().BeOfType<Liveness.Alive>();
        LivenessCheck.Elsewhere(Member(), _ => Started + TimeSpan.FromHours(1), _ => false).Should().BeOfType<Liveness.Gone>().Which.Why.Should().Contain("reused");
    }

    [Fact]
    public void ThisProcess_IsAlive_OnEveryPlatform() =>
        LivenessCheck.Of(new ProcessIdentity(Environment.ProcessId, ProcessTracking.StartedAt(Environment.ProcessId)!.Value)).Should().BeOfType<Liveness.Alive>();

    [Fact]
    public void ANumberNobodyHolds_IsGone_OnEveryPlatform() =>
        LivenessCheck.Of(new ProcessIdentity(int.MaxValue, Started)).Should().BeOfType<Liveness.Gone>();
}

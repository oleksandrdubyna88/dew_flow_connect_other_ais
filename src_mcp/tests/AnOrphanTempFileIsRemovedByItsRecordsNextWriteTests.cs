using System.Diagnostics;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S27 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c> (E3): a writer killed between its temp file and the
/// move leaves that temp file behind. The owner of the clean-up is the NEXT writer of the same record: before it writes, it
/// removes this record's temp files that are older than one hour AND whose writer's pid (carried in the name,
/// <c>&lt;record&gt;.&lt;pid&gt;.&lt;guid&gt;.tmp</c>) is not alive. Age alone is scope, not proof: a writer suspended
/// after its temp file and before its move (a debugger) holds a temp older than an hour, and its move still has to land.
/// </summary>
public sealed class AnOrphanTempFileIsRemovedByItsRecordsNextWriteTests : IDisposable
{
    private const int DeadPid = 999_999;

    private readonly TempDir _dir = TempDir.For("testsToDelete-orphan-temp-");

    public void Dispose() => _dir.Dispose();

    private string Record => _dir.At("record.json");

    /// <summary>This process is alive; every other pid is proven gone.</summary>
    private static Liveness Of(int pid) => pid == Environment.ProcessId ? new Liveness.Alive() : new Liveness.Gone("a test says so");

    private static AtomicSeams DeadWriters() => new() { LivenessOf = Of };

    private string TempOf(string record, int pid, TimeSpan age)
    {
        var temp = _dir.At($"{record}.{pid}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, "an interrupted write");
        File.SetLastWriteTimeUtc(temp, DateTime.UtcNow - age);

        return temp;
    }

    [Fact]
    public void AnOldTempFileOfADeadWriter_IsGone_AfterTheRecordsNextWrite()
    {
        var orphan = TempOf("record.json", DeadPid, TimeSpan.FromHours(2));

        AtomicFile.Write(Record, "new", default, DeadWriters());

        File.Exists(orphan).Should().BeFalse("its writer is dead and it is two hours old — the next write of the same record owns the clean-up");
        File.ReadAllText(Record).Should().Be("new");
    }

    [Fact]
    public void AFreshTempFile_IsKept_EvenWhenItsWriterIsDead()
    {
        var fresh = TempOf("record.json", DeadPid, TimeSpan.FromSeconds(1));

        AtomicFile.Write(Record, "new", default, DeadWriters());

        File.Exists(fresh).Should().BeTrue("a temp file lives for milliseconds, and one second old is not an orphan");
    }

    [Fact]
    public void AnotherRecordsOldTempFile_IsKept()
    {
        var other = TempOf("other.json", DeadPid, TimeSpan.FromHours(2));
        var longer = TempOf("record.json.bak", DeadPid, TimeSpan.FromHours(2));

        AtomicFile.Write(Record, "new", default, DeadWriters());

        File.Exists(other).Should().BeTrue("another record's clean-up is that record's own write");
        File.Exists(longer).Should().BeTrue("a file that merely starts with this record's name is not this record's temp file");
    }

    [Fact]
    public void AnOldTempFileWithNoPidInItsName_IsKept()
    {
        var older = _dir.At($"record.json.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(older, "written by a build that named no writer");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow - TimeSpan.FromHours(2));

        AtomicFile.Write(Record, "new", default, DeadWriters());

        File.Exists(older).Should().BeTrue("with no pid there is no proof that its writer is dead");
    }

    [Fact]
    public void ThePausedLiveWritersAgedTempFile_IsKept_AndThatWritersMoveThenLands()
    {
        string pausedTemp = string.Empty;
        var paused = new AtomicSeams
        {
            LivenessOf = Of,
            AfterTemp = temp =>
            {
                // Writer A is suspended here — its temp file is written, its move not made — and the hour passes.
                pausedTemp = temp;
                File.SetLastWriteTimeUtc(temp, DateTime.UtcNow - TimeSpan.FromHours(2));
                AtomicFile.Write(Record, "B", default, DeadWriters());
                File.Exists(temp).Should().BeTrue("writer A's pid is alive: age is scope, not proof of death");
            },
        };

        AtomicFile.Write(Record, "A", default, paused);

        File.Exists(pausedTemp).Should().BeFalse("writer A's move consumed its temp file");
        File.ReadAllText(Record).Should().Be("A", "the paused writer's move landed after the other writer's");
    }

    [Fact]
    public void AnOldTempFileOfAProcessThatExited_IsGone_ByTheRealLivenessCheck()
    {
        using var finished = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
        finished.WaitForExit();
        var orphan = TempOf("record.json", finished.Id, TimeSpan.FromHours(2));

        AtomicFile.Write(Record, "new", default);

        File.Exists(orphan).Should().BeFalse("ProcessLiveness.IsAlive — the product's own check — says the exited process is gone");
    }

    [Fact]
    public void AnOrphanThatCannotBeRemoved_DoesNotFailTheWrite()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a temp file held open without delete sharing is Windows'");
        var orphan = TempOf("record.json", DeadPid, TimeSpan.FromHours(2));
        using var holder = new FileStream(orphan, FileMode.Open, FileAccess.Read, FileShare.None);

        var write = () => AtomicFile.Write(Record, "new", default, DeadWriters());

        write.Should().NotThrow("the clean-up is best effort and is tried again on the next write");
        File.ReadAllText(Record).Should().Be("new");
    }

    [Fact]
    public void AnAgedOrphanWhoseWriterCannotBeAskedAbout_IsKept()
    {
        // Code gate G1/G2: "could not ask" (another pid namespace, no access) is not proof of death, and a deletion needs proof.
        var unknown = TempOf("record.json", DeadPid, TimeSpan.FromHours(2));

        AtomicFile.Write(Record, "new", default, new AtomicSeams { LivenessOf = _ => new Liveness.Unknown("another pid namespace") });

        File.Exists(unknown).Should().BeTrue("only a PROVEN Gone removes a file; Unknown keeps it (a leak, never a wrong deletion)");
    }

    [Fact]
    public void AnAgedOrphanWhoseWriterIsProvenGone_IsRemoved_ThroughTheLivenessSeam()
    {
        var gone = TempOf("record.json", DeadPid, TimeSpan.FromHours(2));

        AtomicFile.Write(Record, "new", default, new AtomicSeams { LivenessOf = _ => new Liveness.Gone("no such process") });

        File.Exists(gone).Should().BeFalse();
    }

    [Fact]
    public void TheLivenessCheck_AnswersGoneForAnExitedProcess_AliveForThisOne_AndUnknownForNoPid()
    {
        using var finished = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
        finished.WaitForExit();

        ProcessLiveness.Check(finished.Id).Should().BeOfType<Liveness.Gone>();
        ProcessLiveness.Check(Environment.ProcessId).Should().BeOfType<Liveness.Alive>();
        ProcessLiveness.Check(0).Should().BeOfType<Liveness.Unknown>("pid 0 cannot be asked about; Windows answers access denied for it");
        ProcessLiveness.Check(-5).Should().BeOfType<Liveness.Unknown>();
        ProcessLiveness.IsAlive(Environment.ProcessId).Should().BeTrue();
        ProcessLiveness.IsAlive(0).Should().BeFalse("the sweeps keep their two-state answer");
    }
}

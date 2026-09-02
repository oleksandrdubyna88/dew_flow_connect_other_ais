using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The record on disk, and a real process actually being collected.
/// </summary>
/// <remarks>
/// <see cref="OrphanSweepTests"/> covers the decision without touching a process, which is where
/// the safety rules belong. This covers the half that only a real process can show: that the
/// record survives the server, that a genuine orphan dies, and that a child of THIS process does
/// not.
/// </remarks>
public sealed class ProcessTrackingTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"coai-tracking-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A test directory that will not delete is not a test failure.
        }
    }

    /// <summary>A process that will outlive the test unless something kills it.</summary>
    private static Process Sleeper()
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c ping -n 600 127.0.0.1 > nul")
            : new ProcessStartInfo("sleep", "600");
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        return Process.Start(info)!;
    }

    [Fact]
    public void ARecordedProcessIsReadBackWithEnoughToIdentifyIt()
    {
        var tracking = new ProcessTracking(_dir);
        using var child = Sleeper();
        try
        {
            tracking.Record(child.Id, child.StartTime.ToUniversalTime(), "codex/Architecture");

            var read = tracking.Read().Should().ContainSingle().Subject;
            read.Pid.Should().Be(child.Id);
            read.Label.Should().Be("codex/Architecture");
            read.OwnerPid.Should().Be(Environment.ProcessId, "the owner is what makes it an orphan later");
            read.StartedUtc.Should().BeCloseTo(child.StartTime.ToUniversalTime(), TimeSpan.FromSeconds(2));
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void ForgettingRemovesIt()
    {
        var tracking = new ProcessTracking(_dir);
        tracking.Record(4242, DateTime.UtcNow, "codex/Architecture");

        tracking.Forget(4242);

        tracking.Read().Should().BeEmpty();
    }

    [Fact]
    public void AChildOfTHISProcessSurvivesASweep()
    {
        // The safety property, against a real process rather than a fake: `Sweep` runs at startup
        // and on nothing else's schedule, and killing the reviewers of the server running it would
        // be worse than never sweeping at all.
        var tracking = new ProcessTracking(_dir);
        using var child = Sleeper();
        try
        {
            tracking.Record(child.Id, child.StartTime.ToUniversalTime(), "codex/Architecture");

            tracking.Sweep().Should().Be(0);

            child.HasExited.Should().BeFalse("this process still owns it");
            tracking.Read().Should().ContainSingle("an in-flight child is not a stale record");
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void AReviewerLeftBehindByADeadServerIsKilled()
    {
        // The reported defect, end to end. The record names an owner that does not exist, which is
        // what a server's death leaves behind, and the child is a real running process.
        var tracking = new ProcessTracking(_dir);
        using var orphan = Sleeper();
        var record = new TrackedProcess(
            orphan.Id,
            orphan.StartTime.ToUniversalTime(),
            OwnerPid: 2,                       // a pid that is nothing on any machine this runs on
            OwnerStartedUtc: DateTime.UtcNow,
            Label: "antigravity/SecurityReliability");
        Directory.CreateDirectory(Path.Combine(_dir, "running"));
        File.WriteAllText(
            Path.Combine(_dir, "running", $"{orphan.Id}.json"),
            System.Text.Json.JsonSerializer.Serialize(record));

        var killed = tracking.Sweep();

        killed.Should().Be(1);
        orphan.WaitForExit(TimeSpan.FromSeconds(10)).Should().BeTrue("the orphan must actually die");
        tracking.Read().Should().BeEmpty("and its record goes with it");
    }

    [Fact]
    public void ARecordNamingAProcessThatIsGoneIsSimplyForgotten()
    {
        var tracking = new ProcessTracking(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "running"));
        File.WriteAllText(
            Path.Combine(_dir, "running", "424242.json"),
            System.Text.Json.JsonSerializer.Serialize(
                new TrackedProcess(424242, DateTime.UtcNow, 2, DateTime.UtcNow, "codex/Architecture")));

        tracking.Sweep().Should().Be(0);

        tracking.Read().Should().BeEmpty();
    }

    [Fact]
    public void AHalfWrittenRecordIsDeletedRatherThanCrashingTheSweep()
    {
        // What a server killed mid-write leaves. The whole remedy is deleting it: it names a
        // process nothing can now identify.
        var tracking = new ProcessTracking(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "running"));
        File.WriteAllText(Path.Combine(_dir, "running", "999.json"), "{\"Pid\": 99");

        tracking.Read().Should().BeEmpty();
        File.Exists(Path.Combine(_dir, "running", "999.json")).Should().BeFalse();
    }

    [Fact]
    public void SweepingWithNothingRecordedTouchesNothing()
    {
        new ProcessTracking(_dir).Sweep().Should().Be(0);
    }
}

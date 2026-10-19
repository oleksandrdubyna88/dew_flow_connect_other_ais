using System.ComponentModel;
using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer that answers at once and exits before the launcher reads its start time is a reviewer that ANSWERED — not
/// one that "could not be started" (todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md, C2).
/// </summary>
/// <remarks>
/// <para>On macOS <c>Process.StartTime</c> throws a <see cref="Win32Exception"/> ("Unable to retrieve the specified
/// information about the process or thread") for a child that has already exited and been reaped; .NET's own spelling of
/// the same fact is an <see cref="InvalidOperationException"/>. The launcher used to let either travel, and the reviewer
/// executor turned the first into <c>NotStarted</c>: 17 macOS CI runs of <c>BoundedSchedulerTests</c>, four of
/// <c>APromptFileDoesNotOutliveItsLaunchTests</c>, two of <c>EndToEndTests</c>.</para>
/// <para>The race cannot be won on purpose with a real clock, so the launcher's start-time read is a seam and these tests
/// make it fail the way macOS does — AFTER the child has exited, whatever the operating system running them.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AChildThatExitedBeforeItsStartTimeWasReadTests
{
    private const string Reviewer = "codex/Architecture";

    public static TheoryData<string> BothSpellings => new() { "Win32Exception", "InvalidOperationException" };

    [Theory]
    [MemberData(nameof(BothSpellings))]
    public async Task AChildThatExitedBeforeItsStartTimeWasRead_IsNotReportedAsNotStarted(string spelling)
    {
        var tracker = new RecordingTracker();
        var launcher = new ProcessLauncher(tracker, ThrowsAfterTheChildExited(spelling));
        var request = new ProcessRequest(FakeCliInvocations.Exe, ["emit", "hello"], AppContext.BaseDirectory) { TrackAs = Reviewer };

        var result = await launcher.RunAsync(request, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, "the child ran to its end; only the bookkeeping about it failed");
        result.StdOut.Should().Contain("hello", "what the child said is the launch's result, however its start time read");
        tracker.Recorded.Should().BeEmpty("an exited child has nothing left for an orphan sweep to find");
        tracker.Forgotten.Should().ContainSingle("the launch's normal end still tells the tracker's owner it is over (the Team server's SlotReaderTracker.Forget does nothing; the marker is cleared by GaugeCapture.ReadAsync, see GaugeCaptureTests)");
    }

    [Theory]
    [MemberData(nameof(BothSpellings))]
    public async Task AReviewerThatExitedBeforeItsStartTimeWasRead_IsAnAnswer_NotNotStarted(string spelling)
    {
        var executor = new ReviewerExecutor(new ProcessLauncher(new RecordingTracker(), ThrowsAfterTheChildExited(spelling)));

        var outcome = await executor.RunAsync(
            FakeCliInvocations.Invoke("codex", ["emit", FakeCliInvocations.CleanReview]),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>(
            "a reviewer that answered fast on a Mac was reported as 'could not be started' — the outcome says why if it is not Ok: {0}", outcome);
    }

    [Theory]
    [MemberData(nameof(BothSpellings))]
    public async Task AChildStillAliveWhoseStartTimeCannotBeRead_IsKilled_AndTheErrorReachesTheCaller(string spelling)
    {
        // The guard the fix keeps: a child that is ALIVE and cannot be recorded is the one state nothing can clean up
        // after (story 6.1a of todo/PLAN_every_round_is_counted.md), so the launcher ends it and the failure travels.
        var tracker = new RecordingTracker();
        var seen = new List<(int Pid, DateTime StartedUtc)>();
        var launcher = new ProcessLauncher(tracker, process =>
        {
            seen.Add((process.Id, process.StartTime.ToUniversalTime()));

            throw Failure(spelling);
        });
        var request = new ProcessRequest(FakeCliInvocations.Exe, ["sleep", "20000"], AppContext.BaseDirectory) { TrackAs = Reviewer };

        var act = () => launcher.RunAsync(request, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.GetType().Name.Should().Be(spelling, "the caller still learns that the child could not be recorded");
        tracker.Recorded.Should().BeEmpty();
        seen.Should().ContainSingle();
        IsGone(seen[0].Pid, seen[0].StartedUtc).Should().BeTrue("a child that is alive and unrecorded must not be left running");
    }

    [Theory]
    [MemberData(nameof(BothSpellings))]
    public async Task AChildThatIsStillEndingWhenTheReadFails_IsGivenAMomentToEnd_AndIsNotReportedAsNotStarted(string spelling)
    {
        // The window the first fix left open (code gate, 2026-10-19): ONE `HasExited` read decided "the child has gone", and
        // at the moment of exit that read can still say false — on Unix the reaping is the runtime's own later step. Here
        // the child is provably alive when the read fails (checked inside the seam) and ends a few milliseconds after.
        var tracker = new RecordingTracker();
        var aliveWhenTheReadFailed = false;
        var launcher = new ProcessLauncher(tracker, process =>
        {
            aliveWhenTheReadFailed = !process.HasExited;
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                KillIfStillThere(process);
            });

            throw Failure(spelling);
        });
        var request = new ProcessRequest(FakeCliInvocations.Exe, ["sleep", "20000"], AppContext.BaseDirectory) { TrackAs = Reviewer };

        var act = () => launcher.RunAsync(request, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync("a child that ended within the wait is a child that is gone, not one that could not be recorded");
        aliveWhenTheReadFailed.Should().BeTrue("the test is only a test of the race if the child had NOT yet exited when the read failed");
        tracker.Recorded.Should().BeEmpty("an ended child has nothing left for an orphan sweep to find");
    }

    [Fact]
    public void AProcessWithTheSamePidButAnotherStartTime_IsNotTheChildThatWasLaunched()
    {
        // The pid of a child that ended can be handed to an unrelated process: "the pid is alive" then says nothing about the child.
        // This process stands for it — alive, with a start time that is not the one recorded at launch.
        using var self = Process.GetCurrentProcess();

        IsGone(self.Id, self.StartTime.ToUniversalTime().AddHours(-1)).Should().BeTrue("a live process that started at another time is not the child launched");
        IsGone(self.Id, self.StartTime.ToUniversalTime()).Should().BeFalse("with the recorded start time it IS that process, and it is alive");
    }

    [Fact]
    public void AProcessThatWasNeverStarted_HasEnded_BecauseThereIsNothingAssociatedToWaitFor()
    {
        // `HasExited` and `WaitForExit` both throw InvalidOperationException ("No process is associated with this object")
        // for a handle with nothing behind it; the launcher reads that as "ended" — the same fact in its third spelling.
        using var never = new Process();

        ProcessLauncher.HasEnded(never).Should().BeTrue();
    }

    /// <summary>Waits for the child to exit — so it is gone when the read happens — and then fails the way the platform does.</summary>
    private static Func<Process, DateTime> ThrowsAfterTheChildExited(string spelling) => process =>
    {
        process.WaitForExit(TimeSpan.FromSeconds(30));

        throw Failure(spelling);
    };

    private static void KillIfStillThere(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already ended or disposed: exactly the state this kill was meant to produce.
        }
    }

    private static Exception Failure(string spelling) => spelling switch
    {
        "Win32Exception" => new Win32Exception("Unable to retrieve the specified information about the process or thread. It may have exited or may be privileged."),
        _ => new InvalidOperationException("Cannot process request because the process has exited."),
    };

    /// <summary>
    /// Whether the child launched with this pid and start time is gone. A pid alone is not an identity — an ended child's pid can be handed to
    /// an unrelated process — so a live process whose start time is not the one recorded at launch is a stranger, and the child is gone.
    /// </summary>
    private static bool IsGone(int pid, DateTime startedUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            return process.HasExited || process.StartTime.ToUniversalTime() != startedUtc;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return true;
        }
    }

    /// <summary>What the Team server's reader tracker and the local one both are, to the launcher: told, or not told, in order.</summary>
    private sealed class RecordingTracker : IProcessTracker
    {
        public List<int> Recorded { get; } = [];

        public List<int> Forgotten { get; } = [];

        public void Record(int pid, DateTime startedUtc, string label) => Recorded.Add(pid);

        public void Forget(int pid) => Forgotten.Add(pid);
    }
}

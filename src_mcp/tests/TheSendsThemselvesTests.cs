using CoaiMcp.Core.Collecting;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A send, as something the database remembers rather than something a button hopes about.
/// </summary>
/// <remarks>
/// <para><b>The funnel cannot answer "is a send happening".</b> A pair is marked sent only on the
/// server's acknowledgement — which is the right rule, and the reason it cannot: for the whole of a
/// multi-minute upload the counts say exactly what they said before it started. A panel reading them
/// shows an idle button, a reload shows an idle button, and a second Send starts a second process
/// against the same waiting pairs. Three plan reviewers arrived at that independently, and the
/// durable-status rule says the source of truth is persisted and re-read.</para>
/// <para><b>The same shape as <see cref="TheRunsThemselvesTests"/></b>, deliberately: opened before
/// the first request, a heartbeat that is proof of life rather than a process id — the data directory
/// can be a NAS, where a pid belongs to another machine — and a terminal state a sweep can reach.</para>
/// </remarks>
public sealed class TheSendsThemselvesTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-sends-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;
    private readonly MovableClock _clock = new(new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero));

    private RoundsDb Db() => RoundsDb.Open(_dir, _log, _clock)!;

    [Fact]
    public void ASendIsRunningBeforeAnythingHasBeenSent()
    {
        using var db = Db();

        db.StartUploadRun("s1", "https://bugs.example", offered: 12);

        var run = db.LastUploadRun();
        run.Any.Should().BeTrue();
        run.Running.Should().BeTrue("a send that cannot be seen while it happens is the whole defect");
        run.Server.Should().Be("https://bugs.example");
        run.Offered.Should().Be(12, "the person needs to know how much this run is about");
        run.Sent.Should().Be(0);
        run.FinishedUtc.Should().BeEmpty();
        run.HeartbeatUtc.Should().NotBeEmpty("the sweep has nothing to judge without one");
    }

    /// <summary>
    /// And it is still running after the panel that started it has gone.
    /// </summary>
    /// <remarks>
    /// The whole point: a new connection, as a reloaded window opens, reads the same state. An
    /// in-memory flag on the panel would pass every other test here and fail this one.
    /// </remarks>
    [Fact]
    public void AReloadSeesTheSendThatIsStillHappening()
    {
        using (var starting = Db())
        {
            starting.StartUploadRun("s1", "https://bugs.example", offered: 3);
        }

        using var reopened = Db();

        reopened.LastUploadRun().Running.Should().BeTrue(
            "the window was closed and the process kept going; the button must still say so");
    }

    [Fact]
    public void WhatASendHasAchievedIsWrittenAsItGoes()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);

        _clock.Advance(TimeSpan.FromSeconds(30));
        db.BeatUploadRun("s1", offered: 5, sent: 2, duplicate: 1, refused: 1);

        var run = db.LastUploadRun();
        run.Sent.Should().Be(2);
        run.Duplicate.Should().Be(1, "already held is a success, and it is not the same success as sent");
        run.Refused.Should().Be(1);
        run.Running.Should().BeTrue();
        run.HeartbeatUtc.Should().Be(_clock.GetUtcNow().UtcDateTime.ToString("O"),
            "a beat that does not move cannot tell a live run from an abandoned one");
    }

    [Fact]
    public void AFinishedSendIsNotRunningAnyMore()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);

        db.EndUploadRun(
            "s1", UploadRunState.Done, offered: 5, sent: 5, duplicate: 0, refused: 0, trouble: string.Empty);

        var run = db.LastUploadRun();
        run.Running.Should().BeFalse();
        run.State.Should().Be(UploadRunState.Done);
        run.Sent.Should().Be(5);
        run.FinishedUtc.Should().NotBeEmpty();
    }

    /// <summary>A send that could not reach the server ended, and says why.</summary>
    /// <remarks>
    /// `trouble` rather than a failure flag, because the CLI's own summary carries a sentence and
    /// the panel has to render it: "the server answered 502" sends a person somewhere, and "failed"
    /// sends them nowhere.
    /// </remarks>
    [Fact]
    public void ASendThatCouldNotReachTheServerKeepsTheReason()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);

        db.EndUploadRun(
            "s1", UploadRunState.Failed, offered: 5, sent: 0, duplicate: 0, refused: 0,
            trouble: "the server answered 502");

        var run = db.LastUploadRun();
        run.State.Should().Be(UploadRunState.Failed);
        run.Trouble.Should().Be("the server answered 502");
        run.Running.Should().BeFalse("a failed send must not leave the button saying Sending for ever");
    }

    /// <summary>
    /// A send whose process died is swept, so the button is not stuck for ever.
    /// </summary>
    /// <remarks>
    /// The same rule the collector has, for the same reason and with the same instrument: silence
    /// past the cutoff is presumed gone. A kill leaves no `finally` to run, and without this the
    /// panel would show `Sending…` until somebody deleted a row by hand.
    /// </remarks>
    [Fact]
    public void ASendNobodyIsRunningAnyMoreIsSweptRatherThanLeftRunning()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);

        _clock.Advance(TimeSpan.FromHours(2));
        db.SweepAbandonedUploads();

        var run = db.LastUploadRun();
        run.Running.Should().BeFalse();
        run.State.Should().Be(UploadRunState.Interrupted);
    }

    /// <summary>And a live one is not swept, which is what makes the sweep safe to run on open.</summary>
    [Fact]
    public void ASendThatIsStillBeatingSurvivesTheSweep()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);

        _clock.Advance(TimeSpan.FromMinutes(2));
        db.BeatUploadRun("s1", offered: 5, sent: 1, duplicate: 0, refused: 0);
        db.SweepAbandonedUploads();

        db.LastUploadRun().Running.Should().BeTrue("two minutes of work is not an abandoned run");
    }

    /// <summary>
    /// A SECOND send cannot start while one is running, and the refusal is the INSERT itself.
    /// </summary>
    /// <remarks>
    /// It read the last run, found it idle, and then inserted — two statements with a gap, and two
    /// processes inside that gap both read idle and both inserted, each with its own id, and both
    /// then offered the same waiting pairs. One connection is not a lock. The insert refuses itself
    /// now. (Code round 2, gemini and codex, independently.)
    /// </remarks>
    [Fact]
    public void ASecondSendCannotTakeTheLeaseWhileOneIsRunning()
    {
        using var db = Db();

        db.StartUploadRun("s1", "https://bugs.example", offered: 5).Should().BeTrue();
        db.StartUploadRun("s2", "https://bugs.example", offered: 5).Should().BeFalse(
            "a second send would offer the same waiting pairs a second time");

        db.LastUploadRun().Id.Should().Be("s1", "and the first one is untouched");
    }

    [Fact]
    public void TheLeaseIsFreeAgainOnceTheSendHasEnded()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);
        db.EndUploadRun("s1", UploadRunState.Done, offered: 5, sent: 5, duplicate: 0, refused: 0,
            trouble: string.Empty);

        db.StartUploadRun("s2", "https://bugs.example", offered: 2).Should().BeTrue();
    }

    /// <summary>And a swept run does not fence the next send either.</summary>
    [Fact]
    public void ASweptSendDoesNotHoldTheLease()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);

        _clock.Advance(TimeSpan.FromHours(2));
        db.SweepAbandonedUploads();

        db.StartUploadRun("s2", "https://bugs.example", offered: 5).Should().BeTrue(
            "an abandoned run that fences every later send is the deadlock the sweep exists for");
    }

    /// <summary>A finished run is never swept, whatever its heartbeat says.</summary>
    /// <remarks>
    /// `EndUploadRun` writes the state and `finished_utc` together, so `state = 'running'` is enough
    /// today — and only while that stays true, and while nobody adds a state this sweep has never
    /// heard of. Naming the column costs nothing. (Code round 2, local.)
    /// </remarks>
    [Fact]
    public void AFinishedSendSurvivesASweepWithAnOldHeartbeat()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);
        db.EndUploadRun("s1", UploadRunState.Done, offered: 5, sent: 5, duplicate: 0, refused: 0,
            trouble: string.Empty);

        _clock.Advance(TimeSpan.FromHours(2));
        db.SweepAbandonedUploads();

        db.LastUploadRun().State.Should().Be(UploadRunState.Done, "a done send is not an abandoned one");
    }

    /// <summary>Starting the same run twice is one run — the id is the identity.</summary>
    [Fact]
    public void TheSameRunStartedTwiceIsStillOneRun()
    {
        using var db = Db();

        db.StartUploadRun("s1", "https://bugs.example", offered: 5);
        db.BeatUploadRun("s1", offered: 5, sent: 3, duplicate: 0, refused: 0);
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);

        db.LastUploadRun().Sent.Should().Be(3, "a second start must not erase what the first achieved");
    }

    /// <summary>No send has ever happened here, and that is a state rather than an absence.</summary>
    [Fact]
    public void ADatabaseThatHasNeverSentSaysSo()
    {
        using var db = Db();

        var run = db.LastUploadRun();
        run.Any.Should().BeFalse();
        run.Running.Should().BeFalse();
    }

    /// <summary>A send that THREW is recorded as failed, with what it said, not as a clean ending.</summary>
    /// <remarks>
    /// The <c>finally</c> alone recorded a throw as <c>done, 0 sent</c>, because the summary variable
    /// still held the empty value the assignment never reached — so a crashed send looked exactly
    /// like a successful one with nothing to do. (Code round, codex, twice.)
    /// </remarks>
    [Fact]
    public void ASendThatThrewIsNotRecordedAsADoneOne()
    {
        using var db = Db();
        db.StartUploadRun("s1", "https://bugs.example", offered: 5);

        db.EndUploadRun("s1", UploadRunState.Failed, offered: 5, sent: 0, duplicate: 0, refused: 0,
            trouble: "the process was stopped");

        var run = db.LastUploadRun();
        run.State.Should().Be(UploadRunState.Failed);
        run.Trouble.Should().NotBeEmpty("a crash that reads as success is worse than a crash");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A file the CI runner still holds is not a test failure.
        }
    }
}

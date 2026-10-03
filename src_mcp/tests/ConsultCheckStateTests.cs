using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The consultant check's state, its held lock, and the rules a reader judges liveness by.
/// </summary>
/// <remarks>
/// E4.3 of PLAN_the_consultant_works_on_every_vendor.md, as the risk consultation (faa596bf, 2026-10-03) redesigned
/// it: a check HOLDS <c>&lt;kind&gt;.check.lock</c> open with <c>FileShare.None</c> for its whole duration, and a second
/// check whose exclusive open fails is <c>already-checking</c>. On ITS OWN side a reader decides liveness by trying that
/// open itself; on the OTHER side of the Windows/WSL seam, where the lock cannot be seen, by the heartbeat (the code
/// round of epic 4) — and a reader that cannot open the lock at all falls back to the heartbeat too.
/// </remarks>
public sealed class ConsultCheckStateTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-checkstate-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing test.
        }
    }

    private static ConsultCheckRecord Checking(string kind = "claude") => new()
    {
        CallerKind = kind,
        State = ConsultCheckStates.Checking,
        StartedUtc = "2026-10-03T10:00:00.0000000Z",
        DeadlineUtc = "2026-10-03T10:06:00.0000000Z",
        HeartbeatUtc = "2026-10-03T10:00:00.0000000Z",
    };

    [Fact]
    public void ACheckingStateWhoseLockIsFree_IsAbandoned_AndOneWhoseLockIsHeld_IsStillChecking()
    {
        ConsultCheckState.Settled(Checking(), lockHeld: false).State.Should().Be(ConsultCheckStates.Abandoned);
        ConsultCheckState.Settled(Checking(), lockHeld: true).State.Should().Be(ConsultCheckStates.Checking);
    }

    [Fact]
    public void AFinishedState_IsWhatItSays_WhateverTheLock()
    {
        var answered = Checking() with { State = ConsultCheckStates.Answered };

        ConsultCheckState.Settled(answered, lockHeld: false).Should().Be(answered);
        ConsultCheckState.Settled(answered, lockHeld: true).Should().Be(answered);
    }

    private const string CanaryPath = "C:/tmp/coai-check-1/outside/canary.txt";

    private static string Canary(IReadOnlyList<string> said, params DeniedAction[] denied) =>
        ConsultantCheck.Canary(said, "heron-7", denied, CanaryPath);

    [Fact]
    public void TheCanary_IsReadWhenItsWordIsInTheAnswer_OrInAnyLaunchsStream()
    {
        Canary(["marker: wren-1\ncanary: heron-7"]).Should().Be(CanaryReadings.Read);
        // A leak outranks a denial: the word arrived, whatever else the CLI refused on the way.
        Canary(["canary: heron-7"], new DeniedAction("Read", CanaryPath)).Should().Be(CanaryReadings.Read);
        // The answer said CANNOT, but a tool result in the first launch's stream carried the word: it WAS read.
        Canary(["canary: CANNOT", """{"event":"step_update","tool_result":"heron-7"}""", "{}"]).Should().Be(
            CanaryReadings.Read, "a read whose result came back is a leak whatever the model then answered");
    }

    [Fact]
    public void ADenialCountsAsTheCanarysOnlyWhenItsInputNamesTheCanary()
    {
        Canary(["canary: CANNOT"], new DeniedAction("Read", CanaryPath)).Should().Be(CanaryReadings.DeniedByCli);
        Canary(["canary: CANNOT"], new DeniedAction("Read", @"C:\tmp\coai-check-1\outside\canary.txt")).Should().Be(
            CanaryReadings.DeniedByCli, "a separator is not a different path");
        Canary(["canary: CANNOT"], new DeniedAction("Glob", "C:/tmp/coai-check-1/outside/**")).Should().Be(
            CanaryReadings.DeniedByCli, "a refused search of the canary's directory is a refused read of it");
        Canary(["canary: CANNOT"], new DeniedAction("Glob", "C:/tmp/coai-check-1/repo/**")).Should().Be(
            CanaryReadings.NotAttempted, "a refusal of something else proves nothing about the canary");
    }

    [Fact]
    public void ADenialWithNoInput_IsUnattributed_AndNothingDeniedIsNotAttempted()
    {
        // agy's denied_actions carry no input: the CLI refused SOMETHING, and nobody can say it was the canary.
        Canary(["canary: CANNOT"], new DeniedAction("read_file", string.Empty)).Should().Be(CanaryReadings.DeniedByCliUnattributed);
        Canary(["canary: CANNOT"]).Should().Be(
            CanaryReadings.NotAttempted, "a model that declined on its own proved compliance, not confinement");
    }

    [Fact]
    public void TheDeadlinePromised_IsNeverEarlierThanTheEnd_TeardownIncluded()
    {
        var now = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
        var budget = TimeSpan.FromMinutes(4);

        var deadlines = ConsultantCheck.Deadlines(now, budget);

        deadlines.CancelAtUtc.Should().Be(now + ConsultantCheck.Deadline(budget));
        deadlines.PromisedUtc.Should().BeOnOrAfter(
            deadlines.CancelAtUtc + Runners.Processes.ProcessRequest.DefaultDrainGrace + TimeSpan.FromSeconds(1),
            "a panel killing at the promised time must not land in the drain, the scratch's removal or the final write");
    }
    [Fact]
    public void TheBudget_IsTheReviewerTimeout_CappedAtFourMinutes()
    {
        ConsultantCheck.Budget(TimeSpan.FromMinutes(10)).Should().Be(TimeSpan.FromMinutes(4));
        ConsultantCheck.Budget(TimeSpan.FromSeconds(90)).Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void TheLockIsHeldForTheWholeCheck_ASecondTakerIsRefused_AndAReaderSeesItHeld()
    {
        var store = new ConsultCheckStore(_data);

        using (var first = ConsultCheckLock.TryTake(store, "codex"))
        {
            first.Should().NotBeNull();
            ConsultCheckLock.TryTake(store, "codex").Should().BeNull("one check per caller kind holds the lock");
            HeldFile.IsHeld(store.LockPath("codex")).Should().BeTrue();
            ConsultCheckLock.TryTake(store, "claude").Should().NotBeNull("another caller kind has a lock of its own")
                .And.Subject.As<IDisposable>().Dispose();
        }

        HeldFile.IsHeld(store.LockPath("codex")).Should().BeFalse("the handle went with its holder");
        using var again = ConsultCheckLock.TryTake(store, "codex");
        again.Should().NotBeNull();
    }

    [Fact]
    public void TheStore_ReadsBackWhatTheHolderWrote_AndAReaderSettlesItByTheLock()
    {
        var store = new ConsultCheckStore(_data);
        using (var held = ConsultCheckLock.TryTake(store, "gemini"))
        {
            store.Write(Checking("gemini"));
            store.Current("gemini")!.State.Should().Be(ConsultCheckStates.Checking, "its holder is alive");
        }

        store.Current("gemini")!.State.Should().Be(ConsultCheckStates.Abandoned, "nobody holds the lock any more");
        store.Read("gemini")!.State.Should().Be(ConsultCheckStates.Checking, "a READER rewrites nothing");
        store.Current("other").Should().BeNull("no check was ever run for that kind");
    }

    /// <summary>The side word of a host that is NOT this one — the other half of a Windows/WSL machine.</summary>
    private static string OtherSide() => ConsultHealth.Side() == "wsl" ? "windows" : "wsl";

    /// <summary>
    /// A check written on the OTHER side of the Windows/WSL seam is judged by its heartbeat, never by a probe of its
    /// lock: an advisory <c>flock</c> does not cross the 9P boundary, so the lock always looks free from here
    /// (coai code round of epic 4, 2026-10-03).
    /// </summary>
    [Fact]
    public void AnotherSidesCheck_IsJudgedByItsHeartbeat_NotByALockThisSideCannotSee()
    {
        var store = new ConsultCheckStore(_data);
        var now = DateTime.UtcNow;
        store.Write(Checking("claude") with { Side = OtherSide(), HeartbeatUtc = ConsultationStore.Stamp(now) });
        store.Write(Checking("codex") with { Side = OtherSide(), HeartbeatUtc = ConsultationStore.Stamp(now - TimeSpan.FromMinutes(5)) });
        // HELD on this side — what a lock-only reader would have read as "checking". The other side's record is not
        // this lock's to vouch for, so the stale heartbeat must still win (the codex half can go red that way).
        using var heldHere = ConsultCheckLock.TryTake(store, "codex");

        store.Current("claude")!.State.Should().Be(
            ConsultCheckStates.Checking, "its side refreshed the heartbeat a moment ago, whatever a probe of its lock says");
        store.Current("codex")!.State.Should().Be(
            ConsultCheckStates.Abandoned, "its side stopped beating five minutes ago, whatever this side's lock says");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a time")]
    public void AnotherSidesCheck_WithNoHeartbeatAReaderCanParse_IsAbandoned(string heartbeat)
    {
        var record = Checking() with { Side = OtherSide(), HeartbeatUtc = heartbeat };

        ConsultCheckState.SettledAcross(record, DateTime.UtcNow).State.Should().Be(
            ConsultCheckStates.Abandoned, "a beat nobody can read is no proof of life");
    }

    /// <summary>
    /// A free lock is read AGAIN before the reader says abandoned: a check that finished between the first read and the
    /// probe must show its result, never flash abandoned (the code round of epic 4).
    /// </summary>
    [Fact]
    public void AFreeLock_IsFollowedByASecondRead_SoAFinishedCheckNeverFlashesAbandoned()
    {
        var finished = Checking() with { State = ConsultCheckStates.Answered };

        ConsultCheckState.Settled(Checking(), LockProbe.Free, () => finished).Should().Be(finished);
        ConsultCheckState.Settled(Checking(), LockProbe.Free, () => Checking()).State.Should().Be(ConsultCheckStates.Abandoned);
        ConsultCheckState.Settled(Checking(), LockProbe.Held, () => finished).State.Should().Be(
            ConsultCheckStates.Checking, "a held lock is a live check; nothing is read again");
    }

    /// <summary>
    /// A reader that cannot open the lock at all — no write access, a directory where the file should be — cannot ask
    /// it, and falls back to the heartbeat; it used to read every such check as running for ever.
    /// </summary>
    [Fact]
    public void AReaderThatCannotOpenTheLock_JudgesByTheHeartbeat_NotForeverChecking()
    {
        var store = new ConsultCheckStore(_data);
        Directory.CreateDirectory(store.LockPath("claude"));
        Directory.CreateDirectory(store.LockPath("codex"));
        store.Write(Checking("claude") with { HeartbeatUtc = ConsultationStore.Stamp(DateTime.UtcNow - TimeSpan.FromMinutes(5)) });
        store.Write(Checking("codex") with { HeartbeatUtc = ConsultationStore.Stamp(DateTime.UtcNow) });

        store.Current("claude")!.State.Should().Be(ConsultCheckStates.Abandoned, "its heartbeat stopped five minutes ago");
        store.Current("codex")!.State.Should().Be(ConsultCheckStates.Checking, "its heartbeat is fresh");
    }

    /// <summary>A state file that will not parse is said to be unreadable — never confused with a kind nobody checked.</summary>
    [Fact]
    public void AnUnreadableStateFile_IsUnreadable_NotNeverChecked()
    {
        var store = new ConsultCheckStore(_data);
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.StatePath("gemini"), "{ torn");

        store.Current("gemini")!.State.Should().Be(ConsultCheckStates.Unreadable);
        store.Current("gemini")!.Reason.Should().Contain("gemini.check.json");
        store.Current("other").Should().BeNull("no check was ever run for that kind");
    }

    /// <summary>A sweep on this side must never settle the other side's live check by a lock it cannot see.</summary>
    [Fact]
    public void TheSweep_NeverSettlesAnotherSidesCheck()
    {
        var store = new ConsultCheckStore(_data);
        store.Write(Checking("claude") with { Side = OtherSide(), HeartbeatUtc = ConsultationStore.Stamp(DateTime.UtcNow) });

        store.SettleAbandoned(DateTime.UtcNow, _ => { }).Should().Be(0);
        store.Read("claude")!.State.Should().Be(ConsultCheckStates.Checking);
    }

    [Fact]
    public void TheSweep_RewritesAnAbandonedCheckingState_UnderTheLock_AndLeavesALiveOneAlone()
    {
        var store = new ConsultCheckStore(_data);
        using (ConsultCheckLock.TryTake(store, "claude"))
        {
            store.Write(Checking("claude"));
        }

        using var live = ConsultCheckLock.TryTake(store, "codex");
        store.Write(Checking("codex"));

        store.SettleAbandoned(DateTime.UtcNow, _ => { }).Should().Be(1);

        store.Read("claude")!.State.Should().Be(ConsultCheckStates.Abandoned);
        store.Read("claude")!.FinishedUtc.Should().NotBeEmpty();
        store.Read("codex")!.State.Should().Be(ConsultCheckStates.Checking, "its holder is alive");
    }
}

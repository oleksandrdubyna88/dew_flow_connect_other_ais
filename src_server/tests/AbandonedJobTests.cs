using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A job nobody is listening to any more.
/// </summary>
/// <remarks>
/// The client that submitted it can die without saying so — an extension host killed, a laptop shut —
/// and no promise survives that, which is why cancelling on tab close is best-effort by construction.
/// What is left is that nobody asks about it any more, and that is the only evidence the server has.
/// </remarks>
public sealed class AbandonedJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static JobRecord Queued(DateTimeOffset? polled = null) =>
        new("id", "dev@example.com", "codex", "m", "Architecture", "prompt", JobStatus.Queued,
            Now, Now + JobTransitions.DefaultQueueWait, TimeSpan.FromMinutes(20), LastPolledUtc: polled);

    [Fact]
    public void BeforeTheFirstPollTheSubmitIsWhenWeLastHeardFromThem()
    {
        // Not "never polled, so it lives forever". A client that posts and never comes back is the
        // lost-response case the idempotency key also exists for, and it must not hold a slot for
        // the full queue window on the strength of having said nothing.
        var job = Queued();

        job.HeardFromUtc.Should().Be(Now);
        JobTransitions.IsAbandoned(job, Now.AddMinutes(2)).Should().BeFalse();
        JobTransitions.IsAbandoned(job, Now.AddMinutes(3)).Should().BeTrue();
    }

    [Fact]
    public void AQueuedJobIsDroppedLongBeforeItsQueueDeadline()
    {
        // Three minutes against ten. Nothing has been spent on a queued job, so dropping one early
        // costs a resubmit and saves a whole run on a shared account.
        var job = Queued(Now);

        JobTransitions.IsExpired(job, Now.AddMinutes(3)).Should().BeTrue();
        JobTransitions.ExpiryReason(job, Now.AddMinutes(3))
            .Should().Contain("nobody asking about it")
            .And.Contain("send it again", "a person needs to know what to do about it");
    }

    [Fact]
    public void ARunningJobIsGivenFarLongerBecauseItHasAlreadyBeenPaidFor()
    {
        // The plan round's blocking finding, as a test. A queued job has cost nothing; a running one
        // has already been billed for whatever it has done, so killing it for a network blip destroys
        // work somebody paid for and saves only the remainder. (gemini.)
        var running = JobTransitions.Start(Queued(Now), "a", Now);

        JobTransitions.IsAbandoned(running, Now.AddMinutes(3)).Should().BeFalse(
            "three minutes is the QUEUED window, and a running job is a different question");
        JobTransitions.IsAbandoned(running, Now.AddMinutes(9)).Should().BeFalse();
        JobTransitions.IsAbandoned(running, Now.AddMinutes(10)).Should().BeTrue();

        JobTransitions.RunningAbandonedAfter.Should().BeGreaterThan(
            JobTransitions.QueuedAbandonedAfter,
            "expiring paid work as eagerly as unpaid work is the whole thing this asymmetry prevents");
    }

    [Fact]
    public void AFinishedJobIsNeverAbandoned()
    {
        var done = JobTransitions.Succeed(JobTransitions.Start(Queued(Now), "a", Now), "answer", 1, 2, Now);

        JobTransitions.IsAbandoned(done, Now.AddYears(1)).Should().BeFalse();
        JobTransitions.IsExpired(done, Now.AddYears(1)).Should().BeFalse();
    }

    [Fact]
    public void AbandonmentIsSaidBeforeTheOtherClocks()
    {
        // A job that is both abandoned and out of queue time was abandoned EARLIER — the deadline
        // merely arrived while nobody was watching. Reporting the deadline would send somebody to
        // look at account capacity for a client that had simply gone away.
        var stale = Queued(Now);
        var muchLater = Now + JobTransitions.DefaultQueueWait + TimeSpan.FromMinutes(1);

        JobTransitions.ExpiryReason(stale, muchLater).Should().Contain("nobody asking");
    }

    [Fact]
    public void APollKeepsAJobAlive()
    {
        var jobs = new JobStore();
        jobs.Submit(Queued(Now), Now);

        // Two and a half minutes in, somebody asks. The clock restarts from there, not from submit.
        jobs.Polled("id", "dev@example.com", Now.AddSeconds(150)).Should().NotBeNull();

        jobs.Sweep(Now.AddMinutes(4)).Should().BeEmpty("it was heard from 90 seconds ago");
        jobs.Find("id", "dev@example.com")!.Status.Should().Be(JobStatus.Queued);
    }

    [Fact]
    public void APollThatArrivesTooLateDoesNotReviveTheJob()
    {
        // The plan round's finding, from two reviewers. Stamping first and judging afterwards lets a
        // client that vanished for five minutes resurrect a job the server was entitled to have
        // dropped — and whether it survived would depend on when the sweep timer last happened to
        // fire, which is not a rule anybody can reason about.
        var jobs = new JobStore();
        jobs.Submit(Queued(Now), Now);

        var answered = jobs.Polled("id", "dev@example.com", Now.AddMinutes(5));

        answered.Should().NotBeNull();
        answered!.Status.Should().Be(JobStatus.Failed);
        answered.Failure.Should().Be(FailureKind.Cancelled);
        answered.Reason.Should().Contain("nobody asking");
    }

    [Fact]
    public void ASweepStopsTheVendorOfAnAbandonedRunningJobRatherThanOnlyMarkingIt()
    {
        // Marking the record is not the half that matters: the CLI behind a running job goes on
        // running, goes on spending against the shared account and goes on holding the slot. The
        // token the runner awaits on is what actually frees it. (codex, plan round.)
        var jobs = new JobStore();
        jobs.Submit(Queued(Now), Now);
        var claimed = jobs.TryClaim("codex", "a", Now);
        claimed.Should().NotBeNull();

        var expired = jobs.Sweep(Now + JobTransitions.RunningAbandonedAfter);

        expired.Should().ContainSingle();
        expired[0].Reason.Should().Contain("nobody asking");
        claimed!.Value.Token.IsCancellationRequested.Should().BeTrue(
            "the vendor process must be stopped, not merely disowned");
    }

    [Fact]
    public void AnExpiredJobCarriesItsOwnReasonBackToTheCaller()
    {
        // The sweep returns the ENDED records, so the pump logs the sentence the store decided
        // rather than working out a second one that can disagree with it.
        var jobs = new JobStore();
        jobs.Submit(Queued(Now), Now);

        var expired = jobs.Sweep(Now.AddMinutes(3));

        expired.Should().ContainSingle();
        expired[0].Status.Should().Be(JobStatus.Failed);
        expired[0].Reason.Should().NotBeEmpty();
    }
}

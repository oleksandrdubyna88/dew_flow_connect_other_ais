using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>The rules a job obeys, without a queue, a clock or a filesystem.</summary>
public sealed class JobTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static JobRecord Queued(TimeSpan? budget = null, TimeSpan? queueWait = null) =>
        new("id", "dev@example.com", "codex", "m", "Architecture", "prompt", JobStatus.Queued,
            Now, Now + (queueWait ?? JobTransitions.DefaultQueueWait), budget ?? TimeSpan.FromSeconds(120));

    [Fact]
    public void TheRunClockStartsWhenTheJobStartsNotWhenItWasSubmitted()
    {
        // The plan round's blocking finding, as a test. One deadline stamped at submit burns down
        // while the job waits, so with a global concurrency of one the SECOND job of a pair arrives
        // at its slot with almost no time left — or expires having never run at all, which is a
        // review the caller paid for in latency and got nothing for.
        var job = Queued(TimeSpan.FromSeconds(120));
        var waitedTwoMinutes = Now.AddMinutes(2);

        var started = JobTransitions.Start(job, "a", waitedTwoMinutes);

        started.RunDeadlineUtc.Should().Be(waitedTwoMinutes + TimeSpan.FromSeconds(120) + JobTransitions.RunSlack);
        JobTransitions.IsExpired(started, waitedTwoMinutes.AddSeconds(119)).Should().BeFalse(
            "it has had two seconds of vendor time, not two minutes of queue time");
    }

    [Fact]
    public void AQueuedJobIsJudgedOnHowLongItHasWaited()
    {
        // Polled all the way through, which is what this test is about: how long a job may wait for
        // a free ACCOUNT. Its fixture used to poll never, and that quietly became a different test
        // when abandonment landed — a job nobody asks about now goes at three minutes whatever its
        // queue deadline says, which is `AbandonedJobTests`' subject and not this one's.
        var job = Queued(queueWait: TimeSpan.FromMinutes(10));

        JobTransitions.IsExpired(job with { LastPolledUtc = Now.AddMinutes(9) }, Now.AddMinutes(9))
            .Should().BeFalse();
        JobTransitions.IsExpired(job with { LastPolledUtc = Now.AddMinutes(10) }, Now.AddMinutes(10))
            .Should().BeTrue();
    }

    [Fact]
    public void ARunningJobIsJudgedOnHowLongItHasRun()
    {
        var running = JobTransitions.Start(Queued(TimeSpan.FromSeconds(60)), "a", Now);

        JobTransitions.IsExpired(running, Now.AddSeconds(89)).Should().BeFalse();
        JobTransitions.IsExpired(running, Now.AddSeconds(91)).Should().BeTrue("60s budget plus 30s of slack");
    }

    [Fact]
    public void AFinishedJobNeverExpires()
    {
        var done = JobTransitions.Succeed(JobTransitions.Start(Queued(), "a", Now), "answer", 1, 2, Now);

        JobTransitions.IsExpired(done, Now.AddYears(1)).Should().BeFalse();
    }

    [Fact]
    public void TheExpiryReasonSaysWhichClockRanOut()
    {
        // At `Now`, so abandonment — which is checked first and reads very differently — has not
        // fired: these two sentences are about the clocks the job's OWN deadlines set.
        JobTransitions.ExpiryReason(Queued(), Now).Should().Contain("queue").And.Contain("nothing was sent");
        JobTransitions.ExpiryReason(JobTransitions.Start(Queued(), "a", Now), Now)
            .Should().Contain("still running");
    }
}

/// <summary>What an id says about a job this server does not have.</summary>
public sealed class JobIdTests
{
    [Fact]
    public void AnIdFromAnEarlierRunIsLost()
    {
        var earlier = JobId.New(JobId.Epoch - 1000);

        JobId.Classify(earlier, JobId.Epoch).Should().Be(MissingJob.Lost);
        JobId.Explain(MissingJob.Lost).Should().Contain("restarted");
    }

    [Fact]
    public void AnIdFromThisRunIsMerelyUnknown() =>
        JobId.Classify(JobId.New(), JobId.Epoch).Should().Be(MissingJob.Unknown);

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("-abc")]
    [InlineData("abc-def")]
    [InlineData("-1000-abc")]
    public void RubbishIsUnknownAndNeverLost(string id) =>
        // `lost` is the answer that makes an automated client start recovery, so it must mean
        // something. Comparing for mere inequality made a typo, a truncated id or a skewed clock
        // report a server restart that never happened. (Plan round.)
        JobId.Classify(id, JobId.Epoch).Should().Be(MissingJob.Unknown);

    [Fact]
    public void AnIdFromTheFutureIsUnknownNotLost() =>
        JobId.Classify(JobId.New(JobId.Epoch + 1_000_000), JobId.Epoch).Should().Be(MissingJob.Unknown);
}

/// <summary>The queue: who may submit, what may start, and what must never start.</summary>
public sealed class JobStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static JobRecord Job(string email = "dev@example.com", string vendor = "codex", TimeSpan? queueWait = null, DateTimeOffset? at = null) =>
        new(JobId.New(), email, vendor, "m", "Architecture", "p", JobStatus.Queued,
            at ?? Now, (at ?? Now) + (queueWait ?? TimeSpan.FromMinutes(10)), TimeSpan.FromSeconds(60));

    [Fact]
    public void AnExpiredQueuedJobIsNeverHandedASlot()
    {
        // THE money test. An abandoned job that wins a slot ten minutes later spends the team's
        // subscription on an answer nobody will collect.
        var store = new JobStore();
        store.Submit(Job(queueWait: TimeSpan.FromMinutes(1)));

        store.TryClaim("codex", "a", Now.AddMinutes(2)).Should().BeNull("it ran out of queue time");
    }

    [Fact]
    public void ACancelledJobCannotBeStartedAfterwards()
    {
        var store = new JobStore();
        var (job, _, _) = store.Submit(Job());

        store.Cancel(job!.Id, job.Email, Now).Should().NotBeNull();

        // Telling somebody their review was cancelled and then paying for it anyway is the one
        // outcome a cancel must not produce. (Plan round.)
        store.TryClaim("codex", "a", Now).Should().BeNull();
    }

    [Fact]
    public void TheOldestQueuedJobGoesFirst()
    {
        var store = new JobStore();
        var first = Job(at: Now);
        var second = Job(at: Now.AddSeconds(5));
        store.Submit(second);
        store.Submit(first);

        store.TryClaim("codex", "a", Now.AddSeconds(6))!.Value.Job.Id.Should().Be(first.Id);
    }

    [Fact]
    public void OneCallerCannotFillTheQueue()
    {
        var store = new JobStore(perCallerQueued: 2);
        store.Submit(Job()).Refusal.Should().Be(SubmitRefusal.None);
        store.Submit(Job()).Refusal.Should().Be(SubmitRefusal.None);

        store.Submit(Job()).Refusal.Should().Be(SubmitRefusal.TooManyQueued);
        store.Submit(Job(email: "other@example.com")).Refusal.Should().Be(
            SubmitRefusal.None, "somebody else's queue is their own");
    }

    [Fact]
    public void TheRunningCapDelaysAJobRatherThanRefusingIt()
    {
        // The two caps count different states, which is what the plan round found undefined: queued
        // is how many may WAIT (exceeding it is a 429), running is how many may be on a vendor at
        // once (exceeding it just leaves the job queued).
        var store = new JobStore(perCallerQueued: 10, perCallerRunning: 1);
        store.Submit(Job());
        store.Submit(Job()).Refusal.Should().Be(SubmitRefusal.None, "it is accepted, not refused");

        store.TryClaim("codex", "a", Now).Should().NotBeNull();
        store.TryClaim("codex", "b", Now).Should().BeNull("this caller already has one running");
    }

    [Fact]
    public void AnotherCallerIsNotBlockedByOneBusyPerson()
    {
        var store = new JobStore(perCallerRunning: 1);
        // Distinct submission times, because the point of this test is that the SECOND caller is not
        // blocked by the first — which needs a defined first. Submitted in the same tick, the order
        // falls to the id tie-break, and ids are random: the test then passed about half the time.
        // Found by running the suite repeatedly rather than once.
        store.Submit(Job(at: Now));
        store.Submit(Job(email: "other@example.com", at: Now.AddSeconds(1)));
        store.TryClaim("codex", "a", Now.AddSeconds(2)).Should().NotBeNull();

        store.TryClaim("codex", "b", Now.AddSeconds(2))!.Value.Job.Email.Should().Be("other@example.com");
    }

    [Fact]
    public void AJobIsInvisibleToEverybodyButItsSubmitter()
    {
        var store = new JobStore();
        var (job, _, _) = store.Submit(Job());

        store.Find(job!.Id, "someone-else@example.com").Should().BeNull();
        store.BelongsToSomeoneElse(job.Id, "someone-else@example.com").Should().BeTrue();
        store.BelongsToSomeoneElse(job.Id, job.Email).Should().BeFalse();
    }

    [Fact]
    public void TheSweepExpiresARunningJobThatOverranItsBudget()
    {
        var store = new JobStore();
        store.Submit(Job());
        store.TryClaim("codex", "a", Now);

        var expired = store.Sweep(Now.AddMinutes(5));

        expired.Should().ContainSingle();
        store.All().Single().Status.Should().Be(JobStatus.Failed);
        store.All().Single().Failure.Should().Be(FailureKind.Cancelled);
    }

    [Fact]
    public void FinishedJobsAreForgottenAfterTheirWindow()
    {
        var store = new JobStore(keepFinished: TimeSpan.FromMinutes(30));
        var (job, _, _) = store.Submit(Job());
        store.Finish(JobTransitions.Succeed(job!, "answer", 0, 0, Now));

        store.Sweep(Now.AddMinutes(31));

        store.All().Should().BeEmpty();
    }

    [Fact]
    public async Task ALongPollReturnsTheMomentSomethingChanges()
    {
        var store = new JobStore();
        var (job, _, _) = store.Submit(Job());

        var waiting = store.WaitForChangeAsync(job!.Id, TimeSpan.FromSeconds(20), CancellationToken.None);
        store.Finish(JobTransitions.Succeed(job!, "answer", 0, 0, Now));

        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ALongPollIsNotWokenBySomebodyElsesJob()
    {
        // A single shared waiter is signalled by every submit and finish anywhere on the server, so
        // a client polling its own queued review returned within milliseconds because a stranger’s
        // job moved — a long poll that had become a busy poll. Three reviewers found it. (Code round.)
        var store = new JobStore();
        var (mine, _, _) = store.Submit(Job());
        var (theirs, _, _) = store.Submit(Job(email: "other@example.com"));

        var waiting = store.WaitForChangeAsync(mine!.Id, TimeSpan.FromMilliseconds(400), CancellationToken.None);
        store.Finish(JobTransitions.Succeed(theirs!, "not yours", 0, 0, Now));

        var finishedEarly = await Task.WhenAny(waiting, Task.Delay(120)) == waiting;
        finishedEarly.Should().BeFalse("the other job moving is not this job moving");
        await waiting;
    }

    [Fact]
    public void CancellingARunningJobFiresItsToken()
    {
        // Marking the record terminal is not enough: the CLI goes on running and goes on spending
        // against the account, and the slot is held the whole time. (Blocking, code round.)
        var store = new JobStore();
        store.Submit(Job());
        var claimed = store.TryClaim("codex", "a", Now)!.Value;
        claimed.Token.IsCancellationRequested.Should().BeFalse();

        store.Cancel(claimed.Job.Id, claimed.Job.Email, Now);

        claimed.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void ExpiringARunningJobAlsoFiresItsToken()
    {
        var store = new JobStore();
        store.Submit(Job());
        var claimed = store.TryClaim("codex", "a", Now)!.Value;

        store.Sweep(Now.AddMinutes(5));

        claimed.Token.IsCancellationRequested.Should().BeTrue("a deadline that does not stop the process is not a deadline");
    }

    [Fact]
    public async Task ALongPollGivesUpAtItsBudget()
    {
        var store = new JobStore();
        var started = DateTimeOffset.UtcNow;

        await store.WaitForChangeAsync("any-id", TimeSpan.FromMilliseconds(150), CancellationToken.None);

        (DateTimeOffset.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}

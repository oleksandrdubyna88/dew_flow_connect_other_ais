using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// No start without budget — story 4.3 of <c>todo/PLAN_every_round_is_counted.md</c>, the store's and the runner's half:
/// a job whose caller can no longer wait for a launch's start-up is ENDED at claim with its own name, nothing launched,
/// its one ledger line written, the poller woken — and the claim moves on to the next job in the same pass.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> A job was claimed whenever an account came free, however long its caller had been gone:
/// the shim gives up on its own deadline and DELETEs, but a launch that won a slot a second before that DELETE ran on
/// the company's account for an answer nobody collected (symptom 6 of the plan). And the rule the plan first wrote — a
/// sixty-second floor — would have refused every healthy short review, because a 60 s reviewer's shim advertises at most
/// 50 s before any margin (the risk consultation for epic 4): the rule is "the caller can no longer wait", a small
/// start-up allowance, not a least useful run.</para>
/// <para>The seam: the rule is the STORE's (<c>JobStore.TryClaim</c>) and the line is the RUNNER's, so the fakes sit at
/// <see cref="IReviewLauncher"/> — a launcher that COUNTS, because the guarantee is that nothing launched. The clock is
/// fixed where the arithmetic is the subject and real where the runner is.</para>
/// </remarks>
public sealed class NoStartWithoutBudgetTests : IDisposable
{
    private const string Round = "0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The shipped numbers: 5 s off the stated budget, 10 s for a launch to start.</summary>
    private static readonly ClientBudgetPolicy Policy = ClientBudgetPolicy.Default;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-no-budget-" + Guid.NewGuid().ToString("N"));

    public NoStartWithoutBudgetTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "vendors.json"), """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        registry.MarkSignedIn(registry.Read("codex", "a"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a vendor helper still holds is the OS's to clean, not this test's to fail on.
        }
    }

    // ---------- the refusal, through the real runner ----------

    [Fact]
    public async Task AJobWithoutBudget_IsEndedWithoutStartingTheVendor()
    {
        var launcher = new Counting();
        var (jobs, runner) = Build(launcher);
        // Twelve seconds stated, five taken off at receipt: seven left, and a launch needs ten to start.
        var job = Submitted(jobs, remainingSeconds: 12);
        var waiting = jobs.WaitForChangeAsync(job.Id, TimeSpan.FromSeconds(20), CancellationToken.None);

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeFalse("nothing could be started");

        launcher.Launches.Should().BeEmpty("the whole point: the vendor was never asked");
        var ended = jobs.All().Single();
        ended.Status.Should().Be(JobStatus.Failed);
        ended.Failure.Should().Be(FailureKind.NoBudget, "its OWN name, never a sentence to match");
        ended.Reason.Should().Contain($"at least {Policy.MinStart.TotalSeconds:0}s", "the reason interpolates the configured allowance")
            .And.Contain("nothing was sent to the vendor");
        ended.StartedUtc.Should().BeNull();
        (await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(2)))).Should().BeSameAs(waiting, "the poller is woken by the refusal");

        // The refusal is counted (E4's plan round): exactly one line, an honest zero, kind and round kept.
        var line = Lines().Should().ContainSingle("one refusal, one line — never none and never two").Subject;
        Text(line, "outcome").Should().Be(nameof(FailureKind.NoBudget));
        line.GetProperty("tokensIn").GetInt64().Should().Be(0);
        line.GetProperty("tokensOut").GetInt64().Should().Be(0);
        Text(line, "usageNote").Should().BeEmpty("nothing launched, so nothing is uncaptured — a true zero, not an unknown");
        line.GetProperty("seconds").GetDouble().Should().Be(0);
        Text(line, "kind").Should().Be("review");
        Text(line, "round").Should().Be(Round);
        Text(line, "clientAttempt").Should().Be(LaunchAttempts.Repair);
        Text(line, "turn").Should().Be(job.Id);
        Text(line, "attempt").Should().Be(LaunchAttempts.First);
        Text(line, "slot").Should().BeEmpty("no account carried it");
        Text(line, "endCause").Should().BeEmpty("a refusal is not a cancellation");
    }

    [Fact]
    public async Task TheClaimMovesOnToTheNextJob()
    {
        var launcher = new Counting();
        var (jobs, runner) = Build(launcher);
        var spent = Submitted(jobs, remainingSeconds: 12, submittedUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        var fine = Submitted(jobs, remainingSeconds: 100);

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue("the second job could start, in the same pass");

        launcher.Launches.Should().Equal([fine.Id], "the free slot was not left idle for a tick");
        jobs.Find(spent.Id, spent.Email)!.Failure.Should().Be(FailureKind.NoBudget);
        jobs.Find(fine.Id, fine.Email)!.Status.Should().Be(JobStatus.Done);
        var lines = Lines();
        lines.Should().HaveCount(2);
        lines.Select(l => Text(l, "outcome")).Should().Equal(nameof(FailureKind.NoBudget), LaunchLines.Answered);
        lines.Select(l => Text(l, "turn")).Should().Equal(spent.Id, fine.Id);
    }

    [Fact]
    public async Task AnOldClient_IsNeverRefusedForBudget()
    {
        var launcher = new Counting();
        // An allowance no stated budget could hold, so a refusal is only ever a question of whether one was stated.
        var (jobs, runner) = Build(launcher, new ClientBudgetPolicy(Policy.SafetyMargin, TimeSpan.FromHours(1)));
        var old = Submitted(jobs, remainingSeconds: 0);

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

        old.ClientDeadlineUtc.Should().BeNull("a request that stated no budget has no deadline here");
        launcher.Launches.Should().Equal([old.Id], "an old client behaves exactly as today");
        jobs.All().Single().Status.Should().Be(JobStatus.Done);
    }

    [Fact]
    public async Task ADeleteBeforeTheClaim_LaunchesNothing()
    {
        // Risk amendment 5: a POST then a DELETE is zero-spend only if the runner cannot claim between the two — and a
        // cancel that landed before the claim is never overtaken by it.
        var launcher = new Counting();
        var (jobs, runner) = Build(launcher);
        var job = Submitted(jobs, remainingSeconds: 100);
        jobs.Cancel(job.Id, job.Email, DateTimeOffset.UtcNow).Should().NotBeNull();

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeFalse();

        launcher.Launches.Should().BeEmpty("a review the caller withdrew is never spent");
        File.Exists(Path.Combine(_dir, "usage.jsonl")).Should().BeFalse("a queued job withdrawn before any launch writes no line, as before");
    }

    // ---------- the arithmetic, on a fixed clock ----------

    [Fact]
    public void TheRunBudget_IsCappedAtWhatIsLeft()
    {
        var jobs = new JobStore(budget: Policy);
        // Ten minutes asked of the vendor, a hundred seconds stated: ninety-five left at receipt, seventy-five at the claim.
        jobs.Submit(Job(remainingSeconds: 100, runBudget: TimeSpan.FromSeconds(600)), Now);

        var claimed = jobs.TryClaim("codex", "a", Now.AddSeconds(20));

        claimed.Job.Should().NotBeNull();
        claimed.Job!.RunBudget.Should().Be(TimeSpan.FromSeconds(75), "the vendor gets what the caller can still wait for, not what was asked");
        claimed.Job.RunDeadlineUtc.Should().Be(Now.AddSeconds(20) + TimeSpan.FromSeconds(75) + JobTransitions.RunSlack);
    }

    [Fact]
    public void AStatedBudgetLongerThanTheJobsOwn_LeavesTheRunBudgetAlone()
    {
        var jobs = new JobStore(budget: Policy);
        jobs.Submit(Job(remainingSeconds: 5_000, runBudget: TimeSpan.FromSeconds(60)), Now);

        var claimed = jobs.TryClaim("codex", "a", Now);

        claimed.Job!.RunBudget.Should().Be(TimeSpan.FromSeconds(60), "a caller cannot buy a longer run than it asked for");
    }

    [Fact]
    public void AHugeRemainingSeconds_IsCappedAtTheJobsOwnLifetime()
    {
        var jobs = new JobStore(budget: Policy);
        var (job, _, _) = jobs.Submit(Job(remainingSeconds: int.MaxValue, runBudget: TimeSpan.FromSeconds(60)), Now);

        // The job's lifetime: its vendor budget plus the queue window it was given — never more, whatever was stated.
        job!.ClientDeadlineUtc.Should().Be(Now + TimeSpan.FromSeconds(60) + JobTransitions.DefaultQueueWait - Policy.SafetyMargin,
            "a client cannot buy a longer-lived job than one without the field");
    }

    [Fact]
    public void AJobAtTheEdge_IsRefused_AndOneJustAboveItLaunches()
    {
        // The margin-edge test E4's plan round asked for: the margin comes off first, and what is left must hold the
        // start-up. Fourteen seconds stated → nine left → refused; fifteen → ten → launches.
        var edge = (int)(Policy.SafetyMargin + Policy.MinStart).TotalSeconds;
        var below = new JobStore(budget: Policy);
        below.Submit(Job(remainingSeconds: edge - 1), Now);
        var above = new JobStore(budget: Policy);
        above.Submit(Job(remainingSeconds: edge), Now);

        var refused = below.TryClaim("codex", "a", Now);
        var launched = above.TryClaim("codex", "a", Now);

        refused.Job.Should().BeNull();
        refused.Refused.Should().ContainSingle().Which.Failure.Should().Be(FailureKind.NoBudget);
        launched.Job.Should().NotBeNull("one just above the edge launches");
        launched.Refused.Should().BeEmpty();
    }

    [Fact]
    public void TheNoBudgetReason_MatchesNoRateLimitPhrase()
    {
        var jobs = new JobStore(budget: Policy);
        jobs.Submit(Job(remainingSeconds: 12), Now);
        var reason = jobs.TryClaim("codex", "a", Now).Refused.Single().Reason;

        reason.Should().Contain($"at least {Policy.MinStart.TotalSeconds:0}s");
        // As the NEW shim prints it (exit 79) and as an OLD shim prints it (exit 70, the name inside its sentence): the
        // ladder on the client reads the shim's stderr, and a refusal it mistook for a throttle would be retried.
        RateLimit.Hit(new ProcessResult(RemoteAsk.NoBudget, string.Empty, RemoteAsk.NoBudgetMessage("codex", "https://srv", reason), TimedOut: false))
            .Should().BeFalse("the new shim's sentence is no rate limit");
        RateLimit.Hit(new ProcessResult(RemoteAsk.VendorFailed, string.Empty, RemoteAsk.FailedMessage("codex", RemoteAsk.NoBudgetFailure, reason), TimedOut: false))
            .Should().BeFalse("nor is the old shim's");
        RateLimit.Hopeless(reason).Should().BeFalse();
    }

    [Fact]
    public void TheSettings_AreRefusedAtBoot_WhenTheyCouldNeverBeRight()
    {
        var negativeMargin = () => ClientBudgetPolicy.From(-1, 10);
        var noStart = () => ClientBudgetPolicy.From(5, 0);

        negativeMargin.Should().Throw<InvalidOperationException>().WithMessage("*Coai:DeadlineSafetyMarginSeconds is -1*");
        noStart.Should().Throw<InvalidOperationException>().WithMessage("*Coai:MinStartSeconds is 0*");
        ClientBudgetPolicy.From(0, 1).Should().Be(new ClientBudgetPolicy(TimeSpan.Zero, TimeSpan.FromSeconds(1)), "a margin of nought is a decision");
        ClientBudgetPolicy.Default.Should().Be(new ClientBudgetPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)));
    }

    // ---------- the harness ----------

    private (JobStore Jobs, JobRunner Runner) Build(IReviewLauncher launcher, ClientBudgetPolicy? policy = null)
    {
        var jobs = new JobStore(budget: policy ?? Policy);
        var runner = new JobRunner(jobs, new VendorCatalogHost(_dir), new SlotRegistry(_dir, new JsonFileStore()), launcher, new UsageLedger(_dir));

        return (jobs, runner);
    }

    /// <summary>A review job as the endpoint stores it, stamped <paramref name="submittedUtc"/> on the server's clock.</summary>
    private static JobRecord Job(int remainingSeconds, TimeSpan? runBudget = null, DateTimeOffset? submittedUtc = null)
    {
        var at = submittedUtc ?? Now;

        return new JobRecord(
            JobId.New(), "dev@example.com", "codex", "m", "Architecture", "p", JobStatus.Queued,
            at, at + JobTransitions.DefaultQueueWait, runBudget ?? TimeSpan.FromSeconds(60), JobKind.Review)
        {
            Round = Round,
            ClientAttempt = LaunchAttempts.Repair,
            StatedBudget = remainingSeconds > 0 ? StatedBudget.Of(remainingSeconds) : StatedBudget.Absent,
        };
    }

    /// <summary>Submitted NOW, for the runner's real clock; the stored record, deadline stamped.</summary>
    private static JobRecord Submitted(JobStore jobs, int remainingSeconds, DateTimeOffset? submittedUtc = null)
    {
        var at = submittedUtc ?? DateTimeOffset.UtcNow;
        var (stored, refusal, _) = jobs.Submit(Job(remainingSeconds, submittedUtc: at), at);
        refusal.Should().Be(SubmitRefusal.None);

        return stored!;
    }

    private IReadOnlyList<JsonElement> Lines() => WithdrawnJob.Lines(_dir);

    private static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>A launcher that answers every job and remembers which ones it was asked to run — zero is the assertion.</summary>
    internal sealed class Counting : IReviewLauncher
    {
        private readonly List<string> _launches = [];

        public IReadOnlyList<string> Launches
        {
            get
            {
                lock (_launches)
                {
                    return [.. _launches];
                }
            }
        }

        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job, IReadOnlyDictionary<string, string?> environment, CancellationToken ct)
        {
            lock (_launches)
            {
                _launches.Add(job.Id);
            }

            return Task.FromResult<ReviewAttempt>(new ReviewAttempt.Answered("""{"findings":[]}""", new CoaiMcp.Core.Findings.Usage(300, 9, null)));
        }
    }
}

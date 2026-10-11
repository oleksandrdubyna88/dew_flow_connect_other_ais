using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// How a job ended is said, not guessed — story 3.2 of <c>todo/PLAN_every_round_is_counted.md</c>, the Team server's
/// half: a launch the caller's token killed is <c>Cancelled</c> with its usage not captured, never the vendor's timeout;
/// WHY it was killed is set by the store AT the transition and read by the ledger from the record the store KEPT; and
/// both shapes of a cancellation — a <c>ProcessResult.Cancelled</c> and a thrown <c>OperationCanceledException</c> —
/// write exactly one line, with any earlier launch's usage kept.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> The deployed server's own ledger line for a review its client withdrew after 8.4 s
/// says <c>TimedOut</c> and 0/0 (<c>fixtures/usage/timed-out-withdrawal.jsonl</c>): <c>ReviewerExecutor</c> read only
/// the launcher's <c>TimedOut</c> flag. <c>JobStore.Cancel</c> and <c>JobStore.Expire</c> both stored
/// <c>FailureKind.Cancelled</c>, told apart by a sentence; <c>JobRunner</c> wrote its ledger line from its OWN
/// recomputed record, so a cancel that landed first was written as whatever the runner thought; and a thrown
/// cancellation finished the job and wrote nothing.</para>
/// <para><b>The seam rule.</b> A defect below <see cref="IReviewLauncher"/> runs through the REAL
/// <see cref="ReviewLauncher"/>, <see cref="JobRunner"/> and <see cref="JobStore"/> with a fake
/// <see cref="IProcessLauncher"/> that waits for its token like a real child does; the race and the thrown-at-once
/// shape are runner rules, so their fakes sit at <see cref="IReviewLauncher"/>. Every new test was RED for its real
/// symptom first, then GREEN, then RED again with a line of the fix planted back.</para>
/// </remarks>
public sealed class HowAJobEndedIsSaidTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-how-a-job-ended-" + Guid.NewGuid().ToString("N"));

    public HowAJobEndedIsSaidTests() => Directory.CreateDirectory(_dir);

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

    private IReadOnlyList<JsonElement> Lines() => WithdrawnJob.Lines(_dir);

    private static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    // ---------- the withdrawal the deployed server got wrong ----------

    [Fact]
    public async Task AWithdrawnLaunch_IsRecordedAsCancelled_WithUsageNotCaptured()
    {
        var (job, line) = await WithdrawnJob.RunAsync(_dir);

        job.Failure.Should().Be(FailureKind.Cancelled, "the client withdrew it; the vendor was never slow");
        job.EndCause.Should().Be(EndCauses.Withdrawn);
        Text(line, "outcome").Should().Be(nameof(FailureKind.Cancelled), "the deployed server filed exactly this launch as TimedOut");
        Text(line, "endCause").Should().Be(EndCauses.Withdrawn, "a structured cause, never a sentence to match");
        Text(line, "usageNote").Should().Be(CostText.UsageNotCaptured, "a killed launch's usage is unknown — the deployed line says a free 0/0");
        line.GetProperty("seconds").GetDouble().Should().BeLessThan(10, "the seconds are the store's: when it was withdrawn, not when the kill drained");
    }

    [Fact]
    public async Task ARealDeadline_IsStillATimeout()
    {
        var (jobs, runner, _) = Queue(new ReviewLauncher(new OwnDeadline()), "codex", "m");

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

        var job = jobs.All().Single();
        job.Failure.Should().Be(FailureKind.TimedOut, "TimedOut alone is the launch's own budget running out — the retry ladder's case");
        job.EndCause.Should().BeEmpty("a cause is a cancellation's alone");
        var line = Lines().Should().ContainSingle().Subject;
        Text(line, "outcome").Should().Be(nameof(FailureKind.TimedOut));
        Text(line, "endCause").Should().BeEmpty();
    }

    // ---------- the cause is the store's, set at the transition ----------

    [Fact]
    public async Task AWithdrawalAbandonmentBackstopAndHostStop_EachCarryTheirCause()
    {
        var withdrawn = await EndedWhileRunningAsync("withdrawn", (jobs, job, _) => jobs.Cancel(job.Id, job.Email, DateTimeOffset.UtcNow));
        var abandoned = await EndedWhileRunningAsync("abandoned", (jobs, _, _) => jobs.Sweep(DateTimeOffset.UtcNow + JobTransitions.RunningAbandonedAfter));
        // A one-second budget: the server's deadline (budget + RunSlack) is long past at +40 s, and nobody has been
        // silent for the ten minutes abandonment needs — so the backstop is the clock that ran out.
        var backstop = await EndedWhileRunningAsync("backstop", (jobs, _, _) => jobs.Sweep(DateTimeOffset.UtcNow.AddSeconds(40)), runBudget: TimeSpan.FromSeconds(1));
        var hostStopped = await EndedWhileRunningAsync("host", (_, _, host) => host.Cancel());

        withdrawn.Job.EndCause.Should().Be(EndCauses.Withdrawn);
        abandoned.Job.EndCause.Should().Be(EndCauses.Abandoned, "nobody polled it for the running window");
        backstop.Job.EndCause.Should().Be(EndCauses.Backstop, "the server's own deadline ended it, not the reviewer's");
        hostStopped.Job.EndCause.Should().Be(EndCauses.HostStopped, "no store transition said why, so the runner's own word stands");
        new[] { withdrawn, abandoned, backstop, hostStopped }.Select(ended => ended.Job.Failure)
            .Should().OnlyContain(kind => kind == FailureKind.Cancelled, "every one of them is the same FailureKind — the cause is what tells them apart");
        // The ledger line carries the cause the STORE kept, not one the runner guessed.
        new[] { withdrawn, abandoned, backstop, hostStopped }.Select(ended => Text(ended.Line, "endCause"))
            .Should().Equal(EndCauses.Withdrawn, EndCauses.Abandoned, EndCauses.Backstop, EndCauses.HostStopped);
        new[] { withdrawn, abandoned, backstop, hostStopped }.Select(ended => Text(ended.Line, "outcome"))
            .Should().OnlyContain(outcome => outcome == nameof(FailureKind.Cancelled));
        new[] { withdrawn, abandoned, backstop, hostStopped }.Select(ended => Text(ended.Line, "usageNote"))
            .Should().OnlyContain(note => note == CostText.UsageNotCaptured, "a killed launch never reported");
    }

    [Fact]
    public void AJobThatExpiredInTheQueue_CarriesItsCause_AndWritesNoLine()
    {
        var jobs = new JobStore();
        jobs.Submit(Job("codex", "m", TimeSpan.FromSeconds(60)));

        var expired = jobs.Sweep(DateTimeOffset.UtcNow + JobTransitions.QueuedAbandonedAfter + JobTransitions.DefaultQueueWait);

        expired.Should().ContainSingle().Which.Failure.Should().Be(FailureKind.Cancelled);
        // Abandonment is judged FIRST, and a queued job nobody polled for its window is abandoned before its queue
        // clock is read — so the cause the record keeps is abandoned, as the sentence has always said.
        expired[0].EndCause.Should().Be(EndCauses.Abandoned);
        var queued = JobTransitions.Expiry(
            Job("codex", "m", TimeSpan.FromSeconds(60)) with { LastPolledUtc = DateTimeOffset.UtcNow.AddMinutes(9) },
            DateTimeOffset.UtcNow.AddMinutes(10) + TimeSpan.FromSeconds(1));
        queued.Cause.Should().Be(EndCauses.QueueExpired, "a queued job polled a minute ago whose queue window ran out never launched");
        queued.Reason.Should().Contain("nothing was sent to the vendor");
        File.Exists(Path.Combine(_dir, "usage.jsonl")).Should().BeFalse("nothing was sent to a vendor, so there is nothing to write down as spent");
    }

    // ---------- both shapes, exactly one line ----------

    [Fact]
    public async Task AThrownCancellation_WritesExactlyOneLine()
    {
        var (jobs, runner, job) = Queue(new ThrowsAtOnce(), "codex", "m");

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

        var ended = jobs.All().Single();
        ended.Failure.Should().Be(FailureKind.Cancelled);
        ended.EndCause.Should().Be(EndCauses.HostStopped, "nothing in the store ended it, so the runner's own word stands");
        var line = Lines().Should().ContainSingle("the thrown shape used to finish the job and write NOTHING").Subject;
        Text(line, "outcome").Should().Be(nameof(FailureKind.Cancelled));
        Text(line, "endCause").Should().Be(EndCauses.HostStopped);
        Text(line, "usageNote").Should().Be(CostText.UsageNotCaptured);
        Text(line, "turn").Should().Be(job.Id);
    }

    [Fact]
    public async Task AThrownCancellationOfAFollowUp_KeepsTheFirstLaunchsUsage()
    {
        Vendor("""[{ "id": "antigravity", "runtime": "antigravity", "models": ["gemini-3.8-flash-low"], "slots": ["a"] }]""", "antigravity", "a");
        var child = new DeniedThenThrowsOnTheToken();
        var (jobs, runner, job) = Queue(new ReviewLauncher(child), "antigravity", "gemini-3.8-flash-low");
        var started = new TaskCompletionSource<bool>();
        var pumping = runner.PumpAsync("antigravity", CancellationToken.None, started);
        (await started.Task).Should().BeTrue();
        await WithdrawnJob.UntilAsync(() => child.FollowUpStarted);

        jobs.Cancel(job.Id, job.Email, DateTimeOffset.UtcNow).Should().NotBeNull();
        (await pumping).Should().BeTrue();

        var ended = jobs.All().Single();
        ended.Failure.Should().Be(FailureKind.Cancelled);
        ended.EndCause.Should().Be(EndCauses.Withdrawn);
        ended.Usage.TokensIn.Should().Be(100, "the denied first launch was billed, and a cancellation thrown out of the follow-up used to take it with it");
        ended.Usage.NotCaptured.Should().BeTrue("the follow-up's own share is unknown, which makes the job's usage a floor");
        var lines = Lines();
        lines.Should().HaveCount(2, "two paid launches, two lines — the cancellation is exactly one of them");
        lines.Select(l => Text(l, "attempt")).Should().Equal(LaunchAttempts.First, LaunchAttempts.FollowUp);
        lines.Select(l => Text(l, "outcome")).Should().Equal(nameof(FailureKind.UnparseableByVendor), nameof(FailureKind.Cancelled));
        lines[0].GetProperty("tokensIn").GetInt64().Should().Be(100);
        Text(lines[0], "endCause").Should().BeEmpty("the first launch ended on its own — the cause is the cancelled line's");
        Text(lines[1], "endCause").Should().Be(EndCauses.Withdrawn);
        Text(lines[1], "usageNote").Should().Be(CostText.UsageNotCaptured);
        lines.Select(l => Text(l, "turn")).Should().OnlyContain(turn => turn == job.Id);
    }

    // ---------- the race ----------

    [Fact]
    public async Task ACancelThatLandsWhileFinishRuns_KeepsTheStoresCause_AndTheAttemptsUsage()
    {
        // The vendor answers, and the DELETE lands between the answer and the runner's Finish. The store's record —
        // withdrawn, written first under its lock — is the truth about how the job ended; the answer's usage is the
        // truth about what it cost. The runner used to write its own `ok` line over the store's cancel.
        var jobs = new JobStore();
        var (_, runner, job) = Queue(new CancelsThenAnswers(jobs), "codex", "m", jobs);

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

        var kept = jobs.All().Single();
        kept.Status.Should().Be(JobStatus.Failed, "Finish ignores a job the store already ended");
        kept.EndCause.Should().Be(EndCauses.Withdrawn);
        var line = Lines().Should().ContainSingle("one job, one terminal line — never the runner's and the store's both").Subject;
        Text(line, "outcome").Should().Be(nameof(FailureKind.Cancelled), "the line is written from the record the store KEPT, not the runner's recomputed `ok`");
        Text(line, "endCause").Should().Be(EndCauses.Withdrawn);
        line.GetProperty("tokensIn").GetInt64().Should().Be(300, "and what the attempt reported rides on it — an answer nobody collected was still billed");
        Text(line, "turn").Should().Be(job.Id);
    }

    // ---------- the reader ----------

    [Fact]
    public void AnOldLedgerLine_StillReads_AndANewOneKeepsItsEndCause()
    {
        File.WriteAllText(
            Path.Combine(_dir, "usage.jsonl"),
            """{"utc":"2026-10-09T17:18:50.1516034Z","provider":"codex","model":"gpt-5.6-luna","role":"Architecture","stage":"TeamServer","seconds":8.4,"tokensIn":0,"tokensOut":0,"costUsd":null,"outcome":"TimedOut","email":"dev@example.com","kind":"review"}""" + "\n"
            + """{"utc":"2026-10-09T17:18:51.0000000Z","provider":"codex","model":"gpt-5.6-luna","role":"Architecture","stage":"TeamServer","seconds":8.4,"tokensIn":0,"tokensOut":0,"costUsd":null,"outcome":"Cancelled","email":"dev@example.com","kind":"review","usageNote":"usage not captured","turn":"1760030330-abc","attempt":"first","endCause":"withdrawn"}""" + "\n");
        var at = new DateTimeOffset(2026, 10, 9, 17, 18, 50, TimeSpan.Zero);

        var scan = new UsageReader(_dir).Read(new UsageRange(at.AddMinutes(-1), at.AddMinutes(1)));

        scan.Unreadable.Should().Be(0, "a line older than the field is history, never damage");
        // The reader must not drop the cause the line holds; an older line says nothing.
        scan.Lines.Select(l => l.EndCause).Should().Equal(string.Empty, EndCauses.Withdrawn);
        scan.Lines[1].Failed.Should().BeTrue("a cancelled line cost the same as a failed one");
    }

    // ---------- the harness ----------

    private void Vendor(string json, string vendor, params string[] slots)
    {
        File.WriteAllText(Path.Combine(_dir, "vendors.json"), json);
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        foreach (var slot in slots)
        {
            registry.MarkSignedIn(registry.Read(vendor, slot));
        }
    }

    private static JobRecord Job(string vendor, string model, TimeSpan runBudget) =>
        new(JobId.New(), "dev@example.com", vendor, model, "Architecture", "p", JobStatus.Queued,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), runBudget);

    private (JobStore Jobs, JobRunner Runner, JobRecord Job) Queue(IReviewLauncher launcher, string vendor, string model, JobStore? jobs = null, TimeSpan? runBudget = null)
    {
        if (!File.Exists(Path.Combine(_dir, "vendors.json")))
        {
            Vendor("""[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""", "codex", "a");
        }

        var store = jobs ?? new JobStore();
        var runner = new JobRunner(store, new VendorCatalogHost(_dir), new SlotRegistry(_dir, new JsonFileStore()), launcher, new UsageLedger(_dir));
        var job = Job(vendor, model, runBudget ?? TimeSpan.FromSeconds(60));
        store.Submit(job);

        return (store, runner, job);
    }

    /// <summary>
    /// One job run through the real launcher and a child that waits for its token, ended by <paramref name="end"/> once
    /// it is running — the store's cancel, the sweep, or the host's own token — with the record and its ONE line.
    /// </summary>
    private async Task<(JobRecord Job, JsonElement Line)> EndedWhileRunningAsync(
        string under, Action<JobStore, JobRecord, CancellationTokenSource> end, TimeSpan? runBudget = null)
    {
        var dir = Path.Combine(_dir, under);
        Directory.CreateDirectory(dir);

        return await WithdrawnJob.RunAsync(dir, end, runBudget);
    }

    /// <summary>A child that ran out of its OWN budget: the launcher's one flag, at once.</summary>
    private sealed class OwnDeadline : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true));
    }

    /// <summary>The thrown shape, before anything ran: a launcher that cancels out of its own accord.</summary>
    private sealed class ThrowsAtOnce : IReviewLauncher
    {
        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job, IReadOnlyDictionary<string, string?> environment, CancellationToken ct) =>
            throw new OperationCanceledException("the host is stopping");
    }

    /// <summary>The race: the DELETE lands while the attempt is being recorded, and the attempt still answered.</summary>
    private sealed class CancelsThenAnswers(JobStore jobs) : IReviewLauncher
    {
        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job, IReadOnlyDictionary<string, string?> environment, CancellationToken ct)
        {
            jobs.Cancel(job.Id, job.Email, DateTimeOffset.UtcNow);

            return Task.FromResult<ReviewAttempt>(new ReviewAttempt.Answered("""{"findings":[]}""", new Usage(300, 9, null)));
        }
    }

    /// <summary>
    /// An agy conversation whose first launch was auto-denied (billed 100/5) and whose follow-up waits for its token
    /// and then THROWS the cancellation — the shape a launcher that does not report <c>Cancelled</c> produces.
    /// </summary>
    private sealed class DeniedThenThrowsOnTheToken : IProcessLauncher
    {
        private const string Denied =
            "jetski: no output produced — a tool required the \"command\" permission that headless mode cannot "
            + "prompt for, so it was auto-denied.";

        private static readonly string DeniedStream =
            """{"event":"init","conversation_id":"conv-7","init":{"model":"gemini-3.8-flash-low"}}""" + "\n"
            + """{"event":"result","result":{"conversation_id":"conv-7","status":"SUCCESS","response":"","usage":{"input_tokens":100,"output_tokens":5}}}""" + "\n";

        private volatile bool _followUpStarted;

        public bool FollowUpStarted => _followUpStarted;

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (!request.Arguments.Contains("--conversation"))
            {
                return new ProcessResult(0, DeniedStream, Denied, TimedOut: false);
            }

            _followUpStarted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);

            throw new InvalidOperationException("unreachable: the delay above ends only by cancellation");
        }
    }
}

/// <summary>
/// The withdrawal as this server records it — shared with <c>UsageLedgerFixtureTests</c>, which holds the deployed
/// server's own line for the same event beside it (story 1.2 kept the bytes; story 3.2 changes the server).
/// </summary>
internal static class WithdrawnJob
{
    /// <summary>
    /// A codex job run through the real <see cref="ReviewLauncher"/> and a child that waits for its token, ended by
    /// <paramref name="end"/> (the caller's DELETE by default) once it is running; the record as the store kept it, and
    /// the one ledger line.
    /// </summary>
    public static async Task<(JobRecord Job, JsonElement Line)> RunAsync(
        string dir, Action<JobStore, JobRecord, CancellationTokenSource>? end = null, TimeSpan? runBudget = null)
    {
        File.WriteAllText(Path.Combine(dir, "vendors.json"), """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");
        var registry = new SlotRegistry(dir, new JsonFileStore());
        registry.MarkSignedIn(registry.Read("codex", "a"));
        var jobs = new JobStore();
        var child = new KilledByTheToken();
        var runner = new JobRunner(jobs, new VendorCatalogHost(dir), registry, new ReviewLauncher(child), new UsageLedger(dir));
        var job = new JobRecord(
            JobId.New(), "dev@example.com", "codex", "m", "Architecture", "p", JobStatus.Queued,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), runBudget ?? TimeSpan.FromSeconds(60));
        jobs.Submit(job);
        using var host = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>();

        var pumping = runner.PumpAsync("codex", host.Token, started);
        (await started.Task).Should().BeTrue("the job is claimed and its child is waiting");
        await UntilAsync(() => child.Waiting);
        (end ?? ((store, running, _) => store.Cancel(running.Id, running.Email, DateTimeOffset.UtcNow)))(jobs, job, host);
        (await pumping).Should().BeTrue();

        return (jobs.All().Single(), Lines(dir).Should().ContainSingle("one killed launch is one line, never none and never two").Subject);
    }

    public static IReadOnlyList<JsonElement> Lines(string dir) =>
        [.. File.ReadAllLines(Path.Combine(dir, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    /// <summary>Waits, briefly, for a child to reach its wait — a cancel fired before it would be a cancel of nothing.</summary>
    public static async Task UntilAsync(Func<bool> condition)
    {
        for (var tries = 0; tries < 500 && !condition(); tries++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue("the child never reached its wait");
    }

    /// <summary>A child that runs until the caller's token kills it, reported as the real launcher reports that kill: both flags.</summary>
    private sealed class KilledByTheToken : IProcessLauncher
    {
        private volatile bool _waiting;

        public bool Waiting => _waiting;

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            _waiting = true;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                // The real launcher never throws on cancellation: it kills the tree and reports it.
            }

            return new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: true);
        }
    }
}

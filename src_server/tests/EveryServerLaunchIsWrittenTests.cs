using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// Every paid launch is written — story 3.3 of <c>todo/PLAN_every_round_is_counted.md</c>, the Team server's half: an
/// attempt a rate-limited account handed back to the queue writes its own line, a follow-up in the same conversation
/// writes its own, and every line of one job carries the job's id as its turn and says which attempt it was.
/// </summary>
/// <remarks>
/// <para><b>What was wrong</b> (the paid-path census, <c>research/RESULTS_vendor_quota_gauges.md</c> §5.1, row 10): a
/// follow-up folded into the first launch's line (<c>ReviewLauncher.Continued</c>), and a requeued attempt after one
/// slot's <c>RateLimited</c> wrote NOTHING — <c>JobRunner.RateLimited</c> returned null and <c>Record</c> returned
/// before the ledger. Moot while every vendor has one slot; live the moment a second is added.</para>
/// <para>The seam rule: the follow-up runs through the REAL <see cref="ReviewLauncher"/> and <see cref="JobRunner"/>
/// with a fake <see cref="IProcessLauncher"/>; the rotation is a runner rule, so its fake sits at
/// <see cref="IReviewLauncher"/>, above which nothing about it is decided.</para>
/// </remarks>
public sealed class EveryServerLaunchIsWrittenTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-every-server-launch-" + Guid.NewGuid().ToString("N"));

    public EveryServerLaunchIsWrittenTests() => Directory.CreateDirectory(_dir);

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

    private void Vendor(string json, string vendor, params string[] slots)
    {
        File.WriteAllText(Path.Combine(_dir, "vendors.json"), json);
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        foreach (var slot in slots)
        {
            registry.MarkSignedIn(registry.Read(vendor, slot));
        }
    }

    private (JobStore Jobs, JobRunner Runner, JobRecord Job) Queue(IReviewLauncher launcher, string vendor, string model)
    {
        var jobs = new JobStore();
        var runner = new JobRunner(
            jobs, new VendorCatalogHost(_dir), new SlotRegistry(_dir, new JsonFileStore()), launcher, new UsageLedger(_dir));
        var job = new JobRecord(
            JobId.New(), "dev@example.com", vendor, model, "Architecture", "p", JobStatus.Queued,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60));
        jobs.Submit(job);

        return (jobs, runner, job);
    }

    private IReadOnlyList<JsonElement> Lines() =>
        [.. File.ReadAllLines(Path.Combine(_dir, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    private static string Text(JsonElement line, string name) => line.GetProperty(name).GetString() ?? string.Empty;

    [Fact]
    public async Task ARequeuedAttempt_WritesALine()
    {
        Vendor("""[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a", "b"] }]""", "codex", "a", "b");
        var (jobs, runner, job) = Queue(new BySlot(), "codex", "m");

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue("slot a takes the job and is refused");
        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue("slot b takes it from the queue");

        jobs.All().Single().Status.Should().Be(JobStatus.Done);
        var lines = Lines();
        lines.Should().HaveCount(2, "the refused attempt was a launch on slot a's account — and the ledger wrote nothing for it");
        Text(lines[0], "outcome").Should().Be(nameof(FailureKind.RateLimited));
        Text(lines[0], "attempt").Should().Be(LaunchAttempts.Rotated);
        lines[0].GetProperty("tokensIn").GetInt64().Should().Be(0, "a refusal reported nothing — 0/0, never the answer's tokens");
        Text(lines[1], "outcome").Should().Be("ok");
        Text(lines[1], "attempt").Should().Be(LaunchAttempts.Rotated, "the job took more than one account, so its final attempt was rotated too");
        lines.Select(l => Text(l, "turn")).Should().OnlyContain(turn => turn == job.Id, "on this server a job IS one turn, and its id is the turn's");
    }

    [Fact]
    public async Task AFollowUp_WritesTwoLinesSharingATurn()
    {
        Vendor("""[{ "id": "antigravity", "runtime": "antigravity", "models": ["gemini-3.8-flash-low"], "slots": ["a"] }]""", "antigravity", "a");
        var (jobs, runner, job) = Queue(new ReviewLauncher(new DeniedThenAnswered()), "antigravity", "gemini-3.8-flash-low");

        (await runner.PumpAsync("antigravity", CancellationToken.None)).Should().BeTrue();

        var done = jobs.All().Single();
        done.Status.Should().Be(JobStatus.Done, "the continued conversation answered");
        var lines = Lines();
        lines.Should().HaveCount(2, "the denied launch and its follow-up were two paid launches — the ledger folded them into one line");
        lines.Select(l => Text(l, "attempt")).Should().Equal(LaunchAttempts.First, LaunchAttempts.FollowUp);
        lines.Select(l => Text(l, "outcome")).Should().Equal(nameof(FailureKind.UnparseableByVendor), "ok");
        lines.Select(l => l.GetProperty("tokensIn").GetInt64()).Should().Equal(100, 40);
        lines.Sum(l => l.GetProperty("tokensIn").GetInt64()).Should().Be(done.Usage.TokensIn, "the lines add up to the job's own usage — no total moves");
        lines.Select(l => Text(l, "turn")).Should().OnlyContain(turn => turn == job.Id);
    }

    [Fact]
    public async Task AJobOfOneLaunch_IsOneLine_NamedFirst_WithTheJobAsItsTurn()
    {
        Vendor("""[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["b"] }]""", "codex", "b");
        var (_, runner, job) = Queue(new BySlot(), "codex", "m");

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

        var line = Lines().Should().ContainSingle().Subject;
        Text(line, "attempt").Should().Be(LaunchAttempts.First);
        Text(line, "turn").Should().Be(job.Id);
    }

    [Fact]
    public void AnOldLedgerLine_StillReads_AndANewOneKeepsItsTurnAndAttempt()
    {
        File.WriteAllText(
            Path.Combine(_dir, "usage.jsonl"),
            """{"utc":"2026-10-10T10:00:00.0000000Z","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":8.4,"tokensIn":14508,"tokensOut":5,"costUsd":null,"outcome":"ok","email":"dev@example.com","kind":"review"}""" + "\n"
            + """{"utc":"2026-10-10T10:00:01.0000000Z","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":1.0,"tokensIn":0,"tokensOut":0,"costUsd":null,"outcome":"RateLimited","email":"dev@example.com","kind":"review","tokensCached":0,"tokensReasoning":0,"usageNote":"","turn":"1760090400-abc","attempt":"rotated"}""" + "\n");
        var at = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

        var scan = new UsageReader(_dir).Read(new UsageRange(at.AddMinutes(-1), at.AddMinutes(1)));

        scan.Unreadable.Should().Be(0, "a line older than the fields is history, never damage");
        scan.Lines.Should().HaveCount(2);
        (scan.Lines[0].Turn, scan.Lines[0].Attempt).Should().Be((string.Empty, string.Empty), "an old line says nothing about its turn, which is the truth about it");
        (scan.Lines[1].Turn, scan.Lines[1].Attempt).Should().Be(("1760090400-abc", "rotated"), "the reader dropped the fields the line holds");
    }

    /// <summary>Slot a refuses with a rate limit; any other slot answers. Shared with <c>TheServerRecordsTheRoundTests</c>.</summary>
    internal sealed class BySlot : IReviewLauncher
    {
        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job, IReadOnlyDictionary<string, string?> environment, CancellationToken ct) =>
            Task.FromResult<ReviewAttempt>(slot.Name == "a"
                ? new ReviewAttempt.Failed(new ReviewerOutcome.RateLimited("429 Too Many Requests"))
                : new ReviewAttempt.Answered("""{"findings":[]}""", new Usage(300, 9, null)));
    }

    /// <summary>An agy conversation whose first launch was auto-denied, continued once and answered — each with usage.</summary>
    private sealed class DeniedThenAnswered : IProcessLauncher
    {
        private const string Denied =
            "jetski: no output produced — a tool required the \"command\" permission that headless mode cannot "
            + "prompt for, so it was auto-denied.";

        private static readonly string DeniedStream =
            """{"event":"init","conversation_id":"conv-7","init":{"model":"gemini-3.8-flash-low"}}""" + "\n"
            + """{"event":"result","result":{"conversation_id":"conv-7","status":"SUCCESS","response":"","usage":{"input_tokens":100,"output_tokens":5}}}""" + "\n";

        private static readonly string AnsweredStream =
            """{"event":"result","result":{"conversation_id":"conv-7","status":"SUCCESS","response":"{\"findings\":[]}","usage":{"input_tokens":40,"output_tokens":20}}}""" + "\n";

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(request.Arguments.Contains("--conversation")
                ? new ProcessResult(0, AnsweredStream, string.Empty, TimedOut: false)
                : new ProcessResult(0, DeniedStream, Denied, TimedOut: false));
    }
}

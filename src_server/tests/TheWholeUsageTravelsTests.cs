using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The whole usage travels (story 3.1 of <c>todo/PLAN_every_round_is_counted.md</c>): what a launch reported reaches
/// the job and the server's ledger line whole — cached and reasoning included, and on a failure as well as an answer.
/// </summary>
/// <remarks>
/// <para>The seam rule of the plan's test plan: a defect below <see cref="IReviewLauncher"/> is tested through the
/// REAL <see cref="ReviewLauncher"/> with a fake <see cref="IProcessLauncher"/> — a fake launcher above the defect
/// cannot fail for the right reason. Every one of the 4 091 lines on the live server had 0 cached tokens; a codex
/// <c>turn.completed</c> carries them, and two longs dropped them between the launcher and the ledger.</para>
/// </remarks>
public sealed class TheWholeUsageTravelsTests : IDisposable
{
    /// <summary>What codex-cli prints on stdout for one turn (the shape measured in RESULTS_vendor_quota_gauges.md).</summary>
    private const string CodexStream =
        """{"type":"thread.started","thread_id":"t-1"}""" + "\n"
        + """{"type":"turn.completed","usage":{"input_tokens":14508,"cached_input_tokens":12544,"cache_write_input_tokens":0,"output_tokens":5,"reasoning_output_tokens":0}}""" + "\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-usage-travels-" + Guid.NewGuid().ToString("N"));

    public TheWholeUsageTravelsTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, "vendors.json"),
            """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");
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

    [Fact]
    public async Task TheServerLedger_KeepsCachedAndReasoningTokens()
    {
        var jobs = await PumpOneAsync(new ReviewLauncher(new Codex(0, CodexStream)));

        var job = jobs.All().Single();
        job.Status.Should().Be(JobStatus.Done);
        job.Usage.TokensCached.Should().Be(12_544, "the codex turn reported 12 544 of its input as cached");
        LedgerLine().GetProperty("tokensCached").GetInt64().Should().Be(12_544,
            "the server ledger wrote 0 cached tokens on every line — the launcher's two longs dropped them");
    }

    [Fact]
    public async Task AReasoningCount_ReachesTheServerLedger()
    {
        var jobs = await PumpOneAsync(new Fake(new ReviewAttempt.Answered("""{"findings":[]}""", new Usage(900, 60, null, TokensReasoning: 41))));

        jobs.All().Single().Usage.TokensReasoning.Should().Be(41);
        LedgerLine().GetProperty("tokensReasoning").GetInt64().Should().Be(41, "the reasoning count the vendor reported was dropped");
    }

    [Fact]
    public async Task ANonZeroExit_KeepsItsUsage()
    {
        var jobs = await PumpOneAsync(new ReviewLauncher(new Codex(1, CodexStream)));

        var job = jobs.All().Single();
        job.Failure.Should().Be(FailureKind.NonZeroExit);
        job.Usage.TokensIn.Should().Be(14_508, "a vendor that ran and exited non-zero was billed for what it ran");
        var line = LedgerLine();
        line.GetProperty("tokensIn").GetInt64().Should().Be(14_508, "the ledger said a crashed vendor cost nothing");
        line.GetProperty("tokensCached").GetInt64().Should().Be(12_544);
    }

    [Fact]
    public void TheServersReader_ReadsCachedReasoningAndTheUsageNote()
    {
        File.WriteAllText(
            Path.Combine(_dir, "usage.jsonl"),
            """{"utc":"2026-10-10T10:00:00.0000000Z","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":8.4,"tokensIn":14508,"tokensOut":5,"costUsd":null,"outcome":"TimedOut","email":"dev@example.com","kind":"review","tokensCached":12544,"tokensReasoning":3,"usageNote":"usage not captured"}""" + "\n");
        var at = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

        var line = new UsageReader(_dir).Read(new UsageRange(at.AddMinutes(-1), at.AddMinutes(1))).Lines.Single();

        line.TokensCached.Should().Be(12_544, "the reader dropped the cached count the line holds");
        line.TokensReasoning.Should().Be(3);
        line.UsageNote.Should().Be("usage not captured", "a line whose usage nobody captured must not read as a free 0/0");
    }

    private async Task<JobStore> PumpOneAsync(IReviewLauncher launcher)
    {
        var jobs = new JobStore();
        var runner = new JobRunner(
            jobs, new VendorCatalogHost(_dir), new SlotRegistry(_dir, new JsonFileStore()), launcher, new UsageLedger(_dir));
        jobs.Submit(new JobRecord(
            JobId.New(), "dev@example.com", "codex", "m", "Architecture", "p", JobStatus.Queued,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60)));

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue("the job is claimed and run");

        return jobs;
    }

    private JsonElement LedgerLine()
    {
        var lines = File.ReadAllLines(Path.Combine(_dir, "usage.jsonl"));
        lines.Should().ContainSingle("one terminal job is one ledger line");

        return JsonDocument.Parse(lines[0]).RootElement.Clone();
    }

    /// <summary>A codex process: its event stream on stdout, and — on a clean exit — its answer in the <c>-o</c> file.</summary>
    private sealed class Codex(int exitCode, string stdOut) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var at = request.Arguments.ToList().IndexOf("-o");
            if (exitCode == 0 && at >= 0 && at + 1 < request.Arguments.Count)
            {
                File.WriteAllText(request.Arguments[at + 1], """{"findings":[]}""");
            }

            return Task.FromResult(new ProcessResult(exitCode, stdOut, exitCode == 0 ? string.Empty : "it failed", TimedOut: false));
        }
    }

    /// <summary>Hands the runner one whole attempt — for what happens ABOVE the launcher.</summary>
    private sealed class Fake(ReviewAttempt attempt) : IReviewLauncher
    {
        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job,
            IReadOnlyDictionary<string, string?> environment, CancellationToken ct) =>
            Task.FromResult(attempt);
    }
}

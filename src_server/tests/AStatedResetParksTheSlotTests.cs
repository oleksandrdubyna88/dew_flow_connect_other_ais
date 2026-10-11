using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A stated reset parks the account until then — and not after (story 5.3a of
/// <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para>The scenario as deep as this server's harness reaches without a product seam: the real <see cref="JobRunner"/>,
/// the real <see cref="ReviewLauncher"/> and antigravity adapter, the real <see cref="SlotRegistry"/> on disk — and a
/// fake <see cref="IProcessLauncher"/> standing in for the agy process, the one collaborator a test cannot have (a paid
/// vendor). The HTTP surface adds nothing here: the cooldown is set after the claim, and the in-process host builds its
/// launcher inline. What no harness covers — a real refusal on the deployed box — is checked live at the deploy.</para>
/// <para>The positive control is the consultation's: a slot accidentally parked for good would pass the "nothing
/// before the reset" half alone. "Resets in 2s" is the verbatim line with a smaller number, so the test can watch the
/// instant pass without a clock seam.</para>
/// </remarks>
public sealed class AStatedResetParksTheSlotTests : IDisposable
{
    /// <summary>Captured verbatim 2026-10-10 (research/RESULTS_vendor_quota_gauges.md §3.1), the error id redacted there.</summary>
    private const string Refusal = """AGY_ERROR: {"short_error":"RESOURCE_EXHAUSTED (code 429): Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.","status":"RESOURCE_EXHAUSTED","error_code":429,"code_kind":"http","retryable":true,"error_id":"<redacted>"}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-stated-reset-" + Guid.NewGuid().ToString("N"));

    public AStatedResetParksTheSlotTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, "vendors.json"),
            """[{ "id": "antigravity", "runtime": "antigravity", "models": ["gemini-3.8-flash-low"], "slots": ["a"] }]""");
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        registry.MarkSignedIn(registry.Read("antigravity", "a"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a stray handle still holds is the OS's to clean, not this test's to fail on.
        }
    }

    [Fact]
    public async Task TheVerbatimAgyRefusal_ParksTheSlotForTheStatedHours_AndTheNextJobLaunchesNothing()
    {
        var agy = new Refusing(Refusal);
        var (jobs, runner) = Build(agy);
        var before = DateTimeOffset.UtcNow;
        jobs.Submit(Job());

        await runner.PumpAsync("antigravity", CancellationToken.None);

        var parked = new SlotRegistry(_dir, new JsonFileStore()).Read("antigravity", "a").CooldownUntilUtc;
        parked.Should().NotBeNull();
        parked!.Value.Should().BeCloseTo(before + new TimeSpan(88, 49, 9), TimeSpan.FromSeconds(30),
            "the vendor said 88h49m9s, and the state on disk is what every later claim reads");

        jobs.Submit(Job());
        (await runner.PumpAsync("antigravity", CancellationToken.None)).Should().BeFalse("the only account is parked");
        agy.Launches.Should().Be(1, "a second job must not ask an exhausted account again");
    }

    [Fact]
    public async Task AStatedReset_ParksTheSlot_UntilThen_AndNotAfter()
    {
        var agy = new Refusing(Refusal.Replace("88h49m9s", "2s", StringComparison.Ordinal), refuseOnce: true);
        var (jobs, runner) = Build(agy);
        jobs.Submit(Job());
        await runner.PumpAsync("antigravity", CancellationToken.None);
        jobs.Submit(Job());

        (await runner.PumpAsync("antigravity", CancellationToken.None)).Should().BeFalse("before the stated instant");
        agy.Launches.Should().Be(1);

        var launched = await Until(async () => await runner.PumpAsync("antigravity", CancellationToken.None));

        launched.Should().BeTrue("after the stated two seconds the account is used again — not parked on a 30-minute guess");
        agy.Launches.Should().Be(2);
    }

    private (JobStore Jobs, JobRunner Runner) Build(IProcessLauncher agy)
    {
        var jobs = new JobStore();
        var runner = new JobRunner(
            jobs, new VendorCatalogHost(_dir), new SlotRegistry(_dir, new JsonFileStore()), new ReviewLauncher(agy), new UsageLedger(_dir));

        return (jobs, runner);
    }

    private static JobRecord Job() =>
        new(JobId.New(), "dev@example.com", "antigravity", "gemini-3.8-flash-low", "PlanCritique", "review this",
            JobStatus.Queued, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60));

    /// <summary>Pumps until a job is claimed or ten seconds pass — the stated instant is two.</summary>
    private static async Task<bool> Until(Func<Task<bool>> claimed)
    {
        var stop = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < stop)
        {
            if (await claimed())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return false;
    }

    /// <summary>An agy process refusing with the vendor's own line on stderr — and, when asked, answering after that.</summary>
    private sealed class Refusing(string said, bool refuseOnce = false) : IProcessLauncher
    {
        private int _launches;

        public int Launches => _launches;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var n = Interlocked.Increment(ref _launches);

            return Task.FromResult(refuseOnce && n > 1
                ? new ProcessResult(0, """{"event":"result","result":{"conversation_id":"c","status":"SUCCESS","response":"{\"findings\":[]}","usage":{"input_tokens":40,"output_tokens":20}}}""" + "\n", string.Empty, TimedOut: false)
                : new ProcessResult(1, string.Empty, said, TimedOut: false));
        }
    }
}

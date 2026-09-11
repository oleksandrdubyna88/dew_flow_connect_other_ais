using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using CoaiServer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The loop that starts every review, and what happens when one slot cannot be opened.
/// </summary>
/// <remarks>
/// <para>Product audit of 2026-09-09, finding 6. <c>JobPump.CanStartAsync</c> awaited ONLY the
/// promise <c>JobRunner.PumpAsync</c> completes, and <c>PumpAsync</c> completed it on its four
/// ordinary exits and on nothing else. Between the first and the last of those sit three calls that
/// touch the filesystem — reading every slot's <c>state.json</c>, creating the account directory,
/// chmod-ing it, opening <c>.lock</c> — and a throw from any of them left that promise pending for
/// ever.</para>
/// <para>What it cost: the tick stuck inside the vendor loop, so no later vendor was pumped again and
/// <c>Expire</c> never ran again — a running job whose deadline passed went on spending, which is the
/// failure the sweep exists to prevent. The pump's own <c>catch (Exception)</c>, written so that it
/// "survives anything, loudly", never saw it, because the exception went into a task nobody awaited.
/// And <c>/api/health</c> kept answering while the server accepted reviews and ran none.</para>
/// <para>The broken slot here is the audit's own: a DIRECTORY where the <c>.lock</c> file goes.
/// <c>SlotRegistry.TryOpen</c> catches <c>IOException</c> — which is how "somebody else holds it" is
/// reported — and a directory raises <c>UnauthorizedAccessException</c>, which travels.</para>
/// </remarks>
public sealed class JobPumpTests : IDisposable
{
    /// <summary>Long enough to be sure the answer is not coming, short enough to fail fast.</summary>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-pump-" + Guid.NewGuid().ToString("N"));

    public JobPumpTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, "vendors.json"),
            """
            [{ "id": "broken", "runtime": "codex", "models": ["m"], "slots": ["a"] },
             { "id": "healthy", "runtime": "codex", "models": ["m"], "slots": ["a"] }]
            """);

        // Signed in, both — an account nobody signed in is NeedsSignIn and is never picked, which
        // would make this test pass for a reason that has nothing to do with its subject.
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        foreach (var vendor in new[] { "broken", "healthy" })
        {
            registry.MarkSignedIn(registry.Read(vendor, "a"));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task AThrowBeforeTheClaimStillAnswersTheStartSignal()
    {
        BreakTheLockOf("broken");
        var (jobs, runner) = Build();
        jobs.Submit(Job("broken"));
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = runner.PumpAsync("broken", CancellationToken.None, started);

        var answered = await Task.WhenAny(started.Task, Task.Delay(Promptly, TestContext.Current.CancellationToken));
        answered.Should().BeSameAs(started.Task,
            "the pump waits on this promise and on nothing else — one that is never kept is a tick "
            + "that never ends, and after it no vendor is pumped and nothing is ever expired again");
        (await started.Task).Should().BeFalse("nothing was claimed, which is what the pump needs to know");

        var faulted = async () => await run;
        await faulted.Should().ThrowAsync<Exception>(
            "the promise being answered must not swallow the failure — the continuation still logs it");
    }

    [Fact]
    public async Task ABrokenSlotOnOneVendorDoesNotStopTheNextVendor()
    {
        // The whole point, through the real JobPump: the vendors are walked in order, `broken` is
        // first, and `healthy` has a job waiting behind it.
        BreakTheLockOf("broken");
        var (jobs, runner) = Build();
        jobs.Submit(Job("broken"));
        jobs.Submit(Job("healthy"));

        using var pump = new JobPump(jobs, new VendorCatalogHost(_dir), runner, NullLogger<JobPump>.Instance, Tick);
        await pump.StartAsync(CancellationToken.None);

        var ran = await Until(() => jobs.All().Any(j => j.Vendor == "healthy" && j.IsTerminal));
        ran.Should().BeTrue(
            "a vendor whose slot cannot be opened must cost that vendor its turn and nothing else");

        await pump.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ThePumpStopsWhenTheHostStops()
    {
        // A wedged tick used to wedge shutdown too: the wait took no cancellation token, so StopAsync
        // sat out the host's whole timeout and a planned stop became a hard kill.
        BreakTheLockOf("broken");
        var (jobs, runner) = Build();
        jobs.Submit(Job("broken"));

        using var pump = new JobPump(jobs, new VendorCatalogHost(_dir), runner, NullLogger<JobPump>.Instance, Tick);
        await pump.StartAsync(CancellationToken.None);
        await Task.Delay(Tick * 3, TestContext.Current.CancellationToken);

        var stopping = pump.StopAsync(CancellationToken.None);

        var stopped = await Task.WhenAny(stopping, Task.Delay(Promptly, TestContext.Current.CancellationToken));
        stopped.Should().BeSameAs(stopping, "a host that asked to stop must not wait out its own timeout");
    }

    /// <summary>A directory where the lock FILE goes — the audit's own reproduction.</summary>
    private void BreakTheLockOf(string vendor) =>
        Directory.CreateDirectory(Path.Combine(_dir, "accounts", vendor, "a", ".lock"));

    private (JobStore Jobs, JobRunner Runner) Build()
    {
        var jobs = new JobStore();
        var runner = new JobRunner(
            jobs,
            new VendorCatalogHost(_dir),
            new SlotRegistry(_dir, new JsonFileStore()),
            new Answering());

        return (jobs, runner);
    }

    private static JobRecord Job(string vendor) =>
        new(JobId.New(), "dev@example.com", vendor, "m", "Architecture", "p", JobStatus.Queued,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60));

    /// <summary>Waits for a condition the pump reaches on its own clock, or gives up.</summary>
    private static async Task<bool> Until(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Promptly;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(Tick);
        }

        return false;
    }

    private sealed class Answering : IReviewLauncher
    {
        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job,
            IReadOnlyDictionary<string, string?> environment, CancellationToken ct) =>
            Task.FromResult<ReviewAttempt>(new ReviewAttempt.Answered("the answer", 1, 1));
    }
}

using System.Diagnostics;
using Xunit;
using FluentAssertions;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Tests;

/// <summary>
/// One local engine, one caller — across processes, which is the half the scheduler cannot do.
/// </summary>
/// <remarks>
/// <para>The in-process cap shipped in 0.12.4 and its own record says what it does not cover: two
/// MCP clients each running their own <c>coai-mcp</c>. This machine does that all day.</para>
/// <para><b>The lock is the operating system's.</b> A file held with <see cref="FileShare.None"/> is
/// exclusive between .NET processes and is released by the kernel when the holder dies — so the
/// tests below can kill a holder rather than simulate one, and there is no heartbeat, no pid and no
/// stealing rule to test because there is none to get wrong.</para>
/// </remarks>
/// <remarks>
/// In the <c>fakecli-env</c> collection because it LAUNCHES the fake CLI, whose behaviour is
/// steered by process-wide environment variables that other classes set and clear. A verb this
/// class passes in argv is only read when <c>FAKECLI_MODE</c> is unset, so running beside a class
/// that sets it is a race with nothing holding it off.
/// </remarks>
[Collection("fakecli-env")]
public sealed class EngineLeaseTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-lease-").FullName;
    private readonly string _previous = EngineLease.Directory;

    public EngineLeaseTests() => EngineLease.Directory = _dir;

    public void Dispose()
    {
        EngineLease.Directory = _previous;
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static DateTime In(int seconds) => DateTime.UtcNow.AddSeconds(seconds);

    [Fact]
    public async Task OneEngine_IsHeldByOneCallerAtATime()
    {
        using var first = await EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(5));
        first.Should().NotBeNull();

        var second = await EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(1));

        second.Should().BeNull("the card was busy for the whole deadline, and the caller is told so");
    }

    [Fact]
    public async Task ReleasingIt_LetsTheNextCallerIn()
    {
        var first = await EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(5));
        first!.Dispose();

        using var second = await EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(5));

        second.Should().NotBeNull();
        second!.Waited.Should().BeLessThan(TimeSpan.FromSeconds(2), "nobody was holding it");
    }

    [Fact]
    public async Task TwoEngines_DoNotWaitForEachOther()
    {
        using var one = await EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(5));
        using var two = await EngineLease.AcquireAsync("http://127.0.0.1:8000/v1", In(5));

        one.Should().NotBeNull();
        two.Should().NotBeNull("two cards are two queues");
    }

    [Fact]
    public async Task AWaiterGetsIn_AsSoonAsTheHolderLetsGo()
    {
        var holder = await EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(5));
        var waiting = EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(20));

        await Task.Delay(300, TestContext.Current.CancellationToken);
        waiting.IsCompleted.Should().BeFalse("the card is taken");
        holder!.Dispose();

        using var second = await waiting;
        second.Should().NotBeNull();
        second!.Waited.Should().BeGreaterThan(TimeSpan.FromMilliseconds(200), "it queued, and says so");
    }

    [Fact]
    public async Task Ahead_CountsTheHolderAndEveryoneQueuedBehindIt()
    {
        EngineLease.Ahead("http://127.0.0.1:11434/v1").Should().Be(0, "nobody is on it");

        using var holder = await EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(5));
        EngineLease.Ahead("http://127.0.0.1:11434/v1").Should().Be(1);

        using var cancel = new CancellationTokenSource();
        var queued = EngineLease.AcquireAsync("http://127.0.0.1:11434/v1", In(20), null, cancel.Token);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        EngineLease.Ahead("http://127.0.0.1:11434/v1").Should().Be(2, "the waiter holds its own file while it waits");

        await cancel.CancelAsync();
        await Task.Delay(400, TestContext.Current.CancellationToken);
        try
        {
            await queued;
        }
        catch (OperationCanceledException)
        {
        }

        EngineLease.Ahead("http://127.0.0.1:11434/v1").Should().Be(1, "a waiter that left is not still ahead of anyone");
    }

    [Fact]
    public async Task ADeadlineThatExpiredWhileWaiting_LeavesNobodyInTheQueue()
    {
        // Five reviewers of this change's code round found the same leak, one as Blocking: the
        // timeout path returned without releasing the waiter file, so every expired deadline added
        // a phantom to the queue that every later reviewer was told to wait behind.
        const string engine = "http://127.0.0.1:11434/v1";
        using var holder = await EngineLease.AcquireAsync(engine, In(10));

        var expired = await EngineLease.AcquireAsync(engine, In(1));

        expired.Should().BeNull();
        EngineLease.Ahead(engine).Should().Be(1, "the holder, and nobody else");
    }

    [Fact]
    public async Task AWaiterIsNotAheadOfItself()
    {
        const string engine = "http://127.0.0.1:11434/v1";
        using var holder = await EngineLease.AcquireAsync(engine, In(10));
        using var cancel = new CancellationTokenSource();
        var seen = new List<int>();
        var queued = EngineLease.AcquireAsync(engine, In(20), (_, ahead) => seen.Add(ahead), cancel.Token);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        EngineLease.Ahead(engine).Should().Be(2, "the holder and one waiter are both on the card");

        await cancel.CancelAsync();
        try
        {
            await queued;
        }
        catch (OperationCanceledException)
        {
        }

        EngineLease.Ahead(engine).Should().Be(1, "a cancelled waiter is not still in the queue");
    }

    [Fact]
    public async Task TwoEnginesWhoseNamesFoldTogether_AreStillTwoEngines()
    {
        // `http://host/a` and `http://host-a` both slugged to `http---host-a` before the code round
        // caught it: two different engines sharing one lock and one history.
        using var one = await EngineLease.AcquireAsync("http://host/a", In(5));
        using var two = await EngineLease.AcquireAsync("http://host-a", In(2));

        one.Should().NotBeNull();
        two.Should().NotBeNull("one engine's queue is not another engine's");
    }

    [Fact]
    public async Task AnEstimate_NeedsMoreThanTwoRunsBeforeItSaysAnything()
    {
        const string engine = "http://127.0.0.1:11434/v1";
        using var lease = await EngineLease.AcquireAsync(engine, In(5));

        lease!.Record(engine, "qwen", TimeSpan.FromSeconds(30));
        lease.Record(engine, "qwen", TimeSpan.FromSeconds(40));
        EngineLease.Typical(engine, "qwen").Should().BeNull("two runs is not a rate");

        lease.Record(engine, "qwen", TimeSpan.FromSeconds(50));
        EngineLease.Typical(engine, "qwen")!.Value.TotalSeconds.Should().BeApproximately(40, 0.1);
    }

    [Fact]
    public async Task AnEstimate_IsPerModel_BecauseOneAverageOverTwoIsAnEstimateOfNeither()
    {
        // Raised in this change's gate round: a ten-second check and a five-hundred-second analysis
        // on one card average to a number neither of them will take.
        const string engine = "http://127.0.0.1:11434/v1";
        using var lease = await EngineLease.AcquireAsync(engine, In(5));
        foreach (var _ in Enumerable.Range(0, 3))
        {
            lease!.Record(engine, "small", TimeSpan.FromSeconds(10));
            lease.Record(engine, "big", TimeSpan.FromSeconds(500));
        }

        EngineLease.Typical(engine, "small")!.Value.TotalSeconds.Should().BeApproximately(10, 0.1);
        EngineLease.Typical(engine, "big")!.Value.TotalSeconds.Should().BeApproximately(500, 0.1);
    }

    [Fact]
    public async Task TheWaitNote_SaysTheQueueEvenWithNoHistory()
    {
        const string engine = "http://127.0.0.1:11434/v1";
        using var holder = await EngineLease.AcquireAsync(engine, In(5));

        var note = EngineLease.WaitNote(engine, "qwen");

        note.Should().Be("1 ahead on this engine", "a count is always true; a time without samples is not");
    }

    [Fact]
    public async Task TheWaitNote_AddsATimeOnceThereIsHistory()
    {
        const string engine = "http://127.0.0.1:11434/v1";
        using var holder = await EngineLease.AcquireAsync(engine, In(5));
        foreach (var _ in Enumerable.Range(0, 3))
        {
            holder!.Record(engine, "qwen", TimeSpan.FromSeconds(120));
        }

        EngineLease.WaitNote(engine, "qwen").Should().Be("1 ahead on this engine, about 2 min");
    }

    /// <summary>
    /// The cross-PROCESS claim, made with real processes rather than with two objects in one.
    /// </summary>
    /// <remarks>
    /// Five of them, because five is what the person asking for this runs: several Claude windows,
    /// each with its own server. Every one must finish — slower is the point, failing is not.
    /// </remarks>
    [Fact]
    public async Task FiveProcesses_TakeTheCardOneAtATime_AndAllOfThemFinish()
    {
        var runs = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => HoldFor(600, i)));

        runs.Should().OnlyContain(r => r.ExitCode == 0, "a queue is not a failure");
        var overlaps = runs.SelectMany(a => runs.Where(b => a != b && a.Start < b.End && b.Start < a.End));
        overlaps.Should().BeEmpty("two processes on one card is the defect this exists to prevent");
    }

    private sealed record Held(DateTime Start, DateTime End, int ExitCode);

    /// <summary>One child process that takes the lease, holds it briefly, and prints its window.</summary>
    private async Task<Held> HoldFor(int millis, int index)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");
        var process = Process.Start(new ProcessStartInfo(exe, ["lease", _dir, millis.ToString(), index.ToString()])
        {
            RedirectStandardOutput = true,
        })!;
        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var parts = output.Trim().Split(' ');

        return new Held(DateTime.Parse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind),
            process.ExitCode);
    }
}

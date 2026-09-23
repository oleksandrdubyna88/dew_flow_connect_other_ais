using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Tests;

/// <summary>
/// One local engine is one GPU, and a round must not put three reviewers on it at once.
/// </summary>
/// <remarks>
/// <para><b>Measured 2026-09-03</b>, from this machine's own log. A code round started
/// <c>local/Architecture</c> at 16:04:26 and it answered in <b>30.6 s</b>; it started
/// <c>local/SecurityReliability</c> at 16:04:33 and <c>local/UxDxPerformance</c> at 16:04:35, and
/// both were cancelled at <b>590 s</b> having produced nothing. The engine was neither down nor
/// unreachable: it was serving three requests at once (`COAI_MAX_PER_PROVIDER=3`,
/// Ollama 0.33.2, a 35B MoE resident on one card), so each got a third of the throughput and two
/// of them spent the whole round deadline.</para>
/// <para>Three concurrent calls is the right cap for a hosted vendor and the wrong one for a
/// card, so the cap that matters here is keyed by the ENGINE rather than by the vendor: two
/// vendors pointed at one Ollama share it, and two engines on different ports do not.</para>
/// </remarks>
public sealed class SharedEngineTests
{
    [Fact]
    public async Task ReviewersOnOneEngine_RunOneAtATime()
    {
        var scheduler = new BoundedScheduler(globalCap: 4, perProviderCap: 3, sharedResourceCap: 1);
        var work = new[]
        {
            OnEngine("local", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1"),
            OnEngine("local", RoleCatalog.SecurityRole, "http://127.0.0.1:11434/v1"),
            OnEngine("local", RoleCatalog.UxDxRole, "http://127.0.0.1:11434/v1"),
        };

        await scheduler.RunAllAsync(work, Executor());

        scheduler.PeakPerResource.Should().ContainKey("http://127.0.0.1:11434/v1");
        scheduler.PeakPerResource["http://127.0.0.1:11434/v1"].Should().Be(
            1,
            "three reviewers sharing one card is what made two of them miss a ten-minute deadline");
    }

    [Fact]
    public async Task TwoEnginesOnOneMachine_AreNotSerialisedAgainstEachOther()
    {
        var scheduler = new BoundedScheduler(globalCap: 4, perProviderCap: 3, sharedResourceCap: 1);
        var work = new[]
        {
            OnEngine("local", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1"),
            OnEngine("second", RoleCatalog.ArchitectureRole, "http://127.0.0.1:8000/v1"),
        };

        await scheduler.RunAllAsync(work, Executor());

        scheduler.PeakPerResource["http://127.0.0.1:11434/v1"].Should().Be(1);
        scheduler.PeakPerResource["http://127.0.0.1:8000/v1"].Should().Be(1);
    }

    [Fact]
    public async Task TwoVendorsPointedAtOneEngine_StillShareIt()
    {
        // The case a per-VENDOR cap cannot see: two vendor ids, one card. Raised by Gemini against
        // this plan, which had proposed keying the cap on the provider name.
        var scheduler = new BoundedScheduler(globalCap: 4, perProviderCap: 3, sharedResourceCap: 1);
        var work = new[]
        {
            OnEngine("qwen", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1"),
            OnEngine("llama", RoleCatalog.SecurityRole, "http://127.0.0.1:11434/v1"),
        };

        await scheduler.RunAllAsync(work, Executor());

        scheduler.PeakPerResource["http://127.0.0.1:11434/v1"].Should().Be(1);
    }

    [Fact]
    public async Task AHostedVendor_KeepsItsConcurrency()
    {
        // Nothing about this change may slow down a vendor that is somebody else's fleet.
        var scheduler = new BoundedScheduler(globalCap: 4, perProviderCap: 3, sharedResourceCap: 1);
        var work = new[]
        {
            Hosted("codex", RoleCatalog.ArchitectureRole),
            Hosted("codex", RoleCatalog.SecurityRole),
            Hosted("codex", RoleCatalog.UxDxRole),
        };

        await scheduler.RunAllAsync(work, Executor());

        scheduler.PeakPerProvider["codex"].Should().Be(3);
        scheduler.PeakPerResource.Should().BeEmpty("a hosted reviewer holds no engine of ours");
    }

    [Fact]
    public async Task TwoConcurrentRounds_StillShareTheEngine()
    {
        // The semaphores used to be built inside the run, so a second round in the same process
        // got its own set and the cap was per ROUND rather than per machine. Raised by codex
        // against this plan; the fix is that the engine limiter outlives one run.
        var scheduler = new BoundedScheduler(globalCap: 4, perProviderCap: 3, sharedResourceCap: 1);
        var first = scheduler.RunAllAsync(
            [OnEngine("local", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1")], Executor(50));
        var second = scheduler.RunAllAsync(
            [OnEngine("local", RoleCatalog.SecurityRole, "http://127.0.0.1:11434/v1")], Executor(50));

        await Task.WhenAll(first, second);

        scheduler.PeakPerResource["http://127.0.0.1:11434/v1"].Should().Be(
            1,
            "one card does not care which round asked");
    }

    [Fact]
    public async Task TheCapIsWhatSerialises_NotLuck()
    {
        // The positive control, and it is what gives the four tests above their teeth: with the cap
        // raised, the SAME work overlaps. A serialisation test that passes because the fake launcher
        // is fast would pass here too, and this asserts it does not.
        var scheduler = new BoundedScheduler(globalCap: 4, perProviderCap: 3, sharedResourceCap: 3);
        var work = new[]
        {
            OnEngine("local", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1"),
            OnEngine("local", RoleCatalog.SecurityRole, "http://127.0.0.1:11434/v1"),
            OnEngine("local", RoleCatalog.UxDxRole, "http://127.0.0.1:11434/v1"),
        };

        await scheduler.RunAllAsync(work, Executor(60));

        scheduler.PeakPerResource["http://127.0.0.1:11434/v1"].Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ACapOfZero_DoesNotStopEveryReviewerForEver()
    {
        // `IntVar` refuses a zero before it reaches here, but a caller of this class is not the
        // settings file — and a semaphore of zero is a reviewer that never runs and never reports.
        var scheduler = new BoundedScheduler(globalCap: 2, perProviderCap: 2, sharedResourceCap: 0);
        var run = scheduler.RunAllAsync(
            [OnEngine("local", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1")], Executor(10));

        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.Should().Be(run, "a cap below one is a configured mistake, not a way to disable reviewers");
        (await run).Should().HaveCount(1);
    }

    /// <summary>
    /// The next local reviewer starts the moment the last one ends — not after the hosted ones.
    /// </summary>
    /// <remarks>
    /// <para>Issue #155's second half, restated by the operator on 2026-09-23 with a screenshot: four
    /// local roles in one round, the three tail ones waiting behind codex and gemini while the card sat
    /// idle. A local reviewer used to take a MACHINE slot before its engine, and the tail ones waited
    /// for one behind every hosted reviewer.</para>
    /// <para>The hosted launches here BLOCK until the test lets them go, so the only way for all three
    /// locals to finish is for none of them to need a slot a hosted reviewer holds. Before the change
    /// this timed out; the failure message says why.</para>
    /// </remarks>
    [Fact]
    public async Task TheNextLocalReviewer_StartsWhenTheLastOneEnds_NotAfterTheHostedOnes()
    {
        var scheduler = new BoundedScheduler(globalCap: 3, perProviderCap: 2, sharedResourceCap: 1);
        var hosted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var localsDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = 0;
        ReviewerWork[] work =
        [
            OnEngine("local", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1"),
            Hosted("codex", RoleCatalog.ArchitectureRole, blocks: true),
            Hosted("codex", RoleCatalog.SecurityRole, blocks: true),
            Hosted("codex", RoleCatalog.UxDxRole, blocks: true),
            Hosted("gemini", RoleCatalog.ArchitectureRole, blocks: true),
            Hosted("gemini", RoleCatalog.SecurityRole, blocks: true),
            OnEngine("local", RoleCatalog.SecurityRole, "http://127.0.0.1:11434/v1"),
            OnEngine("local", RoleCatalog.UxDxRole, "http://127.0.0.1:11434/v1"),
        ];

        var run = scheduler.RunAllAsync(work, new ReviewerExecutor(new GatedLauncher(hosted.Task)), onProgress: p =>
        {
            if (p.Provider == "local" && p.Status == "done" && Interlocked.Increment(ref done) == 3)
            {
                localsDone.TrySetResult();
            }
        });
        var first = await Task.WhenAny(localsDone.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var finishedWhileHostedBusy = Volatile.Read(ref done);
        hosted.TrySetResult();
        await run;

        first.Should().Be(
            localsDone.Task,
            $"only {finishedWhileHostedBusy} of 3 local reviewers finished while the hosted ones were busy: a local "
            + "reviewer waited for a machine slot although the card was free");
        scheduler.PeakPerResource["http://127.0.0.1:11434/v1"].Should().Be(1, "still one at a time on the card");
    }

    [Fact]
    public async Task ALocalReviewer_TakesNoMachineSlot()
    {
        // The other half of the same lane: the hosted vendors keep ALL of the machine's slots while
        // local reviewers run. The locals BLOCK here until every hosted reviewer is done, so the
        // hosted ones can only have been three at once if no local was holding a slot. Before the
        // change `local/1` held one while running and `local/2` another while waiting for the card,
        // and the hosted vendors got one slot between them.
        var scheduler = new BoundedScheduler(globalCap: 3, perProviderCap: 2, sharedResourceCap: 1);
        var locals = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hostedDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = 0;
        ReviewerWork[] work =
        [
            OnEngine("local", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1", blocks: true),
            OnEngine("local", RoleCatalog.SecurityRole, "http://127.0.0.1:11434/v1", blocks: true),
            Hosted("codex", RoleCatalog.ArchitectureRole),
            Hosted("codex", RoleCatalog.SecurityRole),
            Hosted("gemini", RoleCatalog.ArchitectureRole),
            Hosted("gemini", RoleCatalog.SecurityRole),
        ];

        var run = scheduler.RunAllAsync(work, new ReviewerExecutor(new GatedLauncher(locals.Task, holdFor: 150)), onProgress: p =>
        {
            if (p.Provider != "local" && p.Status == "done" && Interlocked.Increment(ref done) == 4)
            {
                hostedDone.TrySetResult();
            }
        });
        await Task.WhenAny(hostedDone.Task, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        locals.TrySetResult();
        await run;

        scheduler.PeakConcurrency.Should().Be(3, "the machine's three slots are the hosted vendors' own");
        scheduler.PeakPerResource["http://127.0.0.1:11434/v1"].Should().Be(1);
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1", "http://127.0.0.1:11434/v1/")]
    [InlineData("http://127.0.0.1:11434", "http://127.0.0.1:11434/v1")]
    [InlineData("http://LOCALHOST:11434/v1", "http://localhost:11434/v1")]
    public void OneEngineHasOneKey_WhateverItWasTypedAs(string a, string b) =>
        LocalRuntime.EngineKey(a).Should().Be(
            LocalRuntime.EngineKey(b),
            "two keys would put both requests on one card, which is the timeout this cap prevents");

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1", "http://127.0.0.1:8000/v1")]
    [InlineData("http://127.0.0.1:11434/v1", "http://192.168.1.9:11434/v1")]
    public void TwoEnginesKeepTwoKeys(string a, string b) =>
        LocalRuntime.EngineKey(a).Should().NotBe(LocalRuntime.EngineKey(b));

    [Fact]
    public async Task ARoundCancelledWhileAReviewerWaits_StillReportsThatReviewer()
    {
        // A cancellation escaping one of the fan-out's tasks faults `Task.WhenAll`, which discards
        // every sibling's result with it — so a round cancelled with five reviewers finished would
        // have reported none of them. Raised twice in this change's code round.
        using var cancel = new CancellationTokenSource();
        var scheduler = new BoundedScheduler(globalCap: 1, perProviderCap: 1, sharedResourceCap: 1);
        var work = new[]
        {
            OnEngine("local", RoleCatalog.ArchitectureRole, "http://127.0.0.1:11434/v1"),
            OnEngine("local", RoleCatalog.SecurityRole, "http://127.0.0.1:11434/v1"),
        };

        var run = scheduler.RunAllAsync(work, Executor(400), cancel.Token);
        await Task.Delay(80, TestContext.Current.CancellationToken);
        await cancel.CancelAsync();

        var outcomes = await run;
        outcomes.Should().HaveCount(2, "every reviewer of the round gets an outcome, cancelled or not");
        outcomes.Should().Contain(o => o.Outcome is ReviewerOutcome.NotStarted);
        outcomes
            .Select(o => o.Outcome)
            .OfType<ReviewerOutcome.NotStarted>()
            .Should()
            .OnlyContain(
                o => o.Reason.Contains("still queued") || o.Reason.Contains("while it was running"),
                "the sentence says what happened to it");
    }

    private static ReviewerWork OnEngine(string provider, string role, string engine, bool blocks = false) =>
        new(new ReviewerInvocation(
            provider,
            role,
            new ProcessRequest("dotnet", [blocks ? GatedLauncher.Blocks : "--version"], "."),
            SharedResource: engine));

    private static ReviewerWork Hosted(string provider, string role, bool blocks = false) =>
        new(new ReviewerInvocation(
            provider, role, new ProcessRequest("dotnet", [blocks ? GatedLauncher.Blocks : "--version"], ".")));

    /// <summary>
    /// Launches marked to block wait for the gate; everything else answers after <paramref name="holdFor"/>
    /// milliseconds — long enough, where it matters, for overlap to be observable.
    /// </summary>
    private sealed class GatedLauncher(Task gate, int holdFor = 20) : IProcessLauncher
    {
        public const string Blocks = "--blocks-until-released";

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            await (request.Arguments.Contains(Blocks) ? gate.WaitAsync(ct) : Task.Delay(holdFor, ct));
            return new ProcessResult(0, "{\"findings\":[]}", string.Empty, false);
        }
    }

    /// <summary>An executor whose reviewers all "answer" after a beat, so overlap is observable.</summary>
    private static ReviewerExecutor Executor(int millis = 30) =>
        new(new DelayLauncher(TimeSpan.FromMilliseconds(millis)));

    private sealed class DelayLauncher(TimeSpan delay) : IProcessLauncher
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            return new ProcessResult(0, "{\"findings\":[]}", string.Empty, false);
        }
    }
}

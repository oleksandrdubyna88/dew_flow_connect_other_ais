using System.Diagnostics;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A notice offered in the last second of a session still lands — and the promise says what it delivers.
/// </summary>
/// <remarks>
/// <para><b>What was broken.</b> <see cref="NoticeWriter.Offer"/> hands a record to one background
/// thread and returns; that is the whole point of it, and story 2.2's code round is why. But
/// <c>ServeAsync</c> has two clean exits — after <c>server.RunAsync()</c> and in the
/// <c>catch (IOException or ObjectDisposedException)</c> that is the ordinary "the client hung up"
/// road — and neither waited for the queue. A refusal answered as the client disconnects was a
/// refusal the panel never showed.</para>
///
/// <para><b>And two documents contradicted each other.</b> The plan and <c>module_server.md</c> both
/// said "every refusal now leaves a line" while <c>NoticeWriter</c>'s own docstring said a full queue
/// drops. §8 of the parent plan is about exactly that: a claim a list can make and a codebase can
/// quietly break. The promise is one paragraph now, and it says what is delivered.</para>
///
/// <para><b>What the plan round changed.</b> Thirteen findings, all accepted, and three of them were
/// the same thing: waiting on the in-flight COUNT is not waiting on the writer. If an append hangs,
/// the count never reaches zero and the draining task is still holding the file when the process
/// leaves. <see cref="NoticeWriter.Drain"/> waits on the TASK. codex added the honest name: a record
/// dequeued and mid-append may already be on disk, so what the drain answers is what is
/// UNRESOLVED, not what is lost.</para>
/// </remarks>
public sealed class TheWriterDrainsBeforeTheProcessLeavesTests : IDisposable
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    private readonly string _dir = Directory.CreateTempSubdirectory("coai-drain-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ResolvedDataDir Data => ResolvedDataDir.For(_dir);

    private string[] Lines()
    {
        var file = Path.Combine(_dir, ServerNotices.Name);

        return File.Exists(file) ? File.ReadAllLines(file) : [];
    }

    private static NoticeWriter Writing(Func<ResolvedDataDir, ServerNotice, bool>? append = null) =>
        new(append ?? ((dir, notice) => ServerNotices.Append(dir, notice)));

    private static ServerNotice Notice(string title) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = "refusal",
        Source = "coai-mcp",
        Code = ServerNoticeCodes.Refused,
        Title = title,
    };

    private bool Offer(NoticeWriter writer, string title, Serilog.ILogger? log = null) =>
        writer.Offer(() => Data, Notice(title), log ?? Silent);

    [Fact]
    public void EverythingQueued_IsOnDiskAfterTheDrain()
    {
        var slow = Writing((dir, notice) =>
        {
            Thread.Sleep(10);

            return ServerNotices.Append(dir, notice);
        });

        for (var each = 0; each < 50; each++)
        {
            Offer(slow, $"notice {each}").Should().BeTrue();
        }

        slow.Drain(Generous).Should().Be(0, "a clean exit waits for what it queued");
        Lines().Should().HaveCount(50, "and every one of them is on disk, not in a dead process");
    }

    [Fact]
    public void AWriterThatHangs_DoesNotHoldTheProcess()
    {
        // Three findings of the plan round were the same thing: waiting on the in-flight COUNT is not
        // waiting on the writer. An append that hangs never decrements it, so the wait has to be on
        // the task and it has to be bounded. The comparison is against a GENEROUS ceiling rather than
        // the bound itself — a test that asserts "returned within 200 ms" of a 200 ms bound is a test
        // that fails on a loaded machine and proves nothing on a fast one.
        var stuck = new ManualResetEventSlim(false);
        try
        {
            var wedged = Writing((_, _) => { stuck.Wait(Generous); return true; });
            Offer(wedged, "into the void");
            var clock = Stopwatch.StartNew();

            var unresolved = wedged.Drain(TimeSpan.FromMilliseconds(200));

            clock.Stop();
            unresolved.Should().BeGreaterThan(0, "the notice is unresolved, and the count says so");
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
                "a wedged share must not hold a process that is trying to leave");
        }
        finally
        {
            stuck.Set();
        }
    }

    [Fact]
    public void AWriterWithNothingQueued_PaysNothing()
    {
        // gemini: a budget paid on every clean exit is a budget paid by the release smoke, which runs
        // a real `initialize` over stdio and exits. A session that wrote no notices must not wait.
        var clock = Stopwatch.StartNew();

        Writing().Drain(TimeSpan.FromSeconds(30)).Should().Be(0);

        clock.Stop();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "an empty queue is already drained; the bound is a ceiling, never a wait");
    }

    [Fact]
    public void AfterTheDrain_AnOfferSaysTheWriterIsClosing()
    {
        // The plan round, local: today's sentence — "256 are already waiting and the disk is not
        // answering" — would be a lie on this road, and a lie in a log is worse than silence.
        var said = new List<string>();
        var writer = Writing();
        writer.Drain(Generous);

        Offer(writer, "too late", Watching(said)).Should().BeFalse();

        said.Should().ContainSingle()
            .Which.Should().Contain("closing", "the reason a notice was refused must be the real one")
            .And.NotContain("256", "which is the OTHER reason, and this is not it");
    }

    [Fact]
    public async Task AnOfferRacingTheDrain_IsEitherWrittenOrRefused_NeverLostSilently()
    {
        // The plan round, local: `Drain` completes the writer side and then waits, so an `Offer` from
        // another thread in that window must resolve one way or the other — accepted and drained, or
        // refused and said out loud. What it must never be is accepted and dropped.
        var said = new List<string>();
        var writer = Writing();
        var racing = new List<bool>();
        var start = new ManualResetEventSlim(false);

        // One offer BEFORE the race, accepted for certain. codex was right that a test where the
        // drain wins every time passes on zero accepted and zero written and proves nothing — and
        // measured, the drain does win every time, because the racing task has not been scheduled
        // when the main thread reaches it. So the accepted path is pinned here and the race then
        // only has to show that what it accepts is drained and what it refuses is refused.
        Offer(writer, "before the race").Should().BeTrue();

        var offering = Task.Run(() =>
        {
            start.Wait(Generous);
            for (var each = 0; each < 20; each++)
            {
                racing.Add(Offer(writer, $"racing {each}", Watching(said)));
            }
        });

        start.Set();
        writer.Drain(Generous);
        await offering.WaitAsync(Generous);

        Lines().Length.Should().Be(1 + racing.Count(accepted => accepted),
            "every offer that was ACCEPTED reached the disk, and every one refused was refused");
        said.Should().HaveCount(racing.Count(accepted => !accepted),
            "and a refusal is never silent — one sentence for each");
    }

    [Fact]
    public void AFullQueue_RefusesTheOverflowAndSaysWhy()
    {
        // codex: the promise says a full queue drops and logs, and no test filled it and read the
        // sentence. An off-by-one in the capacity or a misleading warning would have passed
        // everything else.
        var stuck = new ManualResetEventSlim(false);
        var said = new List<string>();
        try
        {
            var wedged = Writing((_, _) => { stuck.Wait(Generous); return true; });
            var accepted = Enumerable.Range(0, NoticeWriter.Depth + 50)
                .Count(each => Offer(wedged, $"burst {each}", Watching(said)));

            accepted.Should().BeGreaterThan(0,
                "an implementation that refused EVERY offer would satisfy the rest of this test — "
                + "a fixture the code rejects proves nothing (codex, on the code round)");
            accepted.Should().BeLessThan(NoticeWriter.Depth + 50, "past the depth it must refuse");
            said.Should().NotBeEmpty("and every refusal is said out loud");
            said[0].Should().Contain(NoticeWriter.Depth.ToString(),
                "the sentence names the depth, so a person knows what filled");
        }
        finally
        {
            stuck.Set();
        }
    }

    [Fact]
    public void TheHostDrainsOnEveryRoadOut()
    {
        // codex was right that every other test here exercises the writer directly, so a `finally`
        // attached to the wrong `try` — or missing from one of the two `return 0` roads — would pass
        // all of them while a last-second refusal is lost from a real session.
        //
        // This is the structural half: the drain is inside `ServeAsync`'s `finally`, which is what
        // covers BOTH returns and an exception unwinding out. The live half — a real refusal over
        // stdio against the published binary, asserted on the bytes — is story 2.4, which the parent
        // plan owes by name and which now has something to check.
        //
        // Since epic 3 the `finally` hands the whole end of the run to `EndedAsync`, and what is pinned
        // is the ORDER in there, because each step depends on the one before it: the beat stops before
        // the marker is cleared (or a late beat re-creates it), the queue drains before the crash is
        // judged, the marker is cleared only after the crash is known to have landed, and the log is
        // flushed last so everything above still has somewhere to be said.
        var code = ProductionSources.CodeOf("src_mcp/src/Program.cs");

        code.Should().Contain("finally { await EndedAsync(life, notices, crash, run, recorded, log); }",
            "one end, in the finally of the try that wraps every exit — a `finally` is what a "
            + "`return` cannot escape, and it sits below story 3.2's crash catch");
        string[] order =
        [
            "await life.StopAsync();",
            "Unresolved(Draining(notices, log), log);",
            "if (crash is null || CrashRecorded(crash, run, recorded)) { life.Clear(); }",
            "HostCrash.Flushed(log);",
        ];
        var at = order.Select(step => code.IndexOf(step, StringComparison.Ordinal)).ToList();
        at.Should().AllSatisfy(i => i.Should().BeGreaterThanOrEqualTo(0), "every step of the end is there");
        at.Should().BeInAscendingOrder("stop, drain, clear only over a crash that landed, flush — in that order");
    }

    [Fact]
    public void NoSecondProductionCallerDrainsTheWriter()
    {
        // The prohibition, in its own test — the convention asks for two, because a scan that finds
        // nothing passes a prohibition and proves nothing. The one above is the known instance.
        ProductionSources.FilesMentioning("NoticeWriter.DrainBudget").Keys
            .Should().Equal(["src_mcp/src/Program.cs"],
                "a second production caller is a second exit road somebody did not write down");
    }

    [Fact]
    public void WhatCouldNotBeWritten_IsNamedWithItsCount()
    {
        // The host turns `Drain`'s answer into one sentence, and nothing asserted that sentence —
        // so a change to the wording or to the "only when non-zero" rule would have been invisible
        // (the code round). Asserted on the WRITER's own contract, which is what the host formats.
        var stuck = new ManualResetEventSlim(false);
        try
        {
            var wedged = Writing((_, _) => { stuck.Wait(Generous); return true; });
            Offer(wedged, "into the void");

            wedged.Drain(TimeSpan.FromMilliseconds(200)).Should().Be(1,
                "one notice was outstanding, so the host says one — a count, not a flag");
        }
        finally
        {
            stuck.Set();
        }
    }

    [Fact]
    public void AFaultedWriterIsAFinishedWriter_AndTheDrainDoesNotThrowOutOfAFinally()
    {
        // gemini, on the code round: `Task.Wait` RETHROWS a faulted task as an AggregateException,
        // and `Drain` runs inside a `finally` during process exit — a throw there skips the warning
        // saying what was unresolved and turns a clean `return 0` into a crash. The task cannot
        // fault today, because `Wrote` catches everything, which is exactly why the guard needed a
        // seam: a `catch` no test can reach is a guarantee nobody has checked, and Sonar said so.
        var faulted = Task.FromException(new InvalidOperationException("the writer died"));

        var waiting = () => NoticeWriter.Waited(faulted, Generous);

        waiting.Should().NotThrow("the contract says never throws, and a finally is where that matters");
        waiting().Should().BeTrue("a faulted writer is a FINISHED writer — nothing is still outstanding");
    }

    [Fact]
    public void ADrainWithNothingOutstanding_SaysNothing()
    {
        // The host turns the count into one sentence and prints it ONLY when it is non-zero; an
        // ordinary session that wrote a few notices must not end with a warning about none.
        var said = new List<string>();

        Program.Unresolved(0, Watching(said));

        said.Should().BeEmpty("zero unresolved notices is not news");
    }

    [Fact]
    public void ADrainThatLeftSomethingOutstanding_NamesTheCount()
    {
        var said = new List<string>();

        Program.Unresolved(3, Watching(said));

        said.Should().ContainSingle().Which.Should().Contain("3",
            "a count, not a flag — the number is what a person compares with the file");
    }

    [Fact]
    public void AHostDrainingAnEmptyWriter_DoesNotAnnounceIt()
    {
        // The code round: a session closing on a slow share sat in the drain with no message and
        // looked hung, so the host says it is draining BEFORE the wait — but only when something is
        // queued. Announcing an empty drain on every clean exit is noise on every clean exit.
        var said = new List<string>();

        Program.Draining(Writing(), Watching(said)).Should().Be(0);

        said.Should().BeEmpty("there was nothing to wait for, so there was nothing to say");
    }

    private static Serilog.ILogger Silent => Serilog.Core.Logger.None;

    private static Serilog.ILogger Watching(List<string> said) =>
        new Serilog.LoggerConfiguration().WriteTo.Sink(new Collecting(said)).CreateLogger();

    private sealed class Collecting(List<string> said) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            lock (said)
            {
                said.Add(logEvent.RenderMessage());
            }
        }
    }
}

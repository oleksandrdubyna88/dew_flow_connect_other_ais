using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every refusal this server returns to a calling AI leaves a line — and no line can cost a refusal.
/// </summary>
/// <remarks>
/// <para><b>What was broken.</b> <c>server-notices.jsonl</c> has been named in the extension's
/// <c>notificationsFile.ts</c> since 2026-09-17, its path derived, and three modules already merge it
/// into the panel section, the page and the derived count. Nothing wrote it, so every one of those
/// surfaces reported on half the product: what the extension did, never what the server refused.</para>
///
/// <para><b>Why ONE point is enough.</b> Story 2.1 holds <c>ErrorAnswer</c> — the only shape a refusal
/// takes on the wire — to a single construction in a single file. So instrumenting
/// <see cref="Refusal"/> IS the guarantee, and <c>TheRefusalRoadsAreCountedTests</c> is what keeps it
/// true as the codebase moves.</para>
///
/// <para><b>The one thing this must not get wrong, in two rounds.</b> The plan round put five findings
/// on one sentence, from all three providers: the first design resolved the data directory OUTSIDE the
/// failure boundary, so a misconfigured <c>COAI_DATA_DIR</c> would have thrown past the return and the
/// calling AI would have received nothing. The code round put TWELVE on the fix: waiting two seconds
/// for a thread-pool append only stops WAITING, so a wedged share kept the worker forever and every
/// refusal cost two threads. The answer to both is <see cref="NoticeWriter"/> — one writer thread, a
/// bounded queue — and the tests below drive a resolver that throws, a writer that throws, a writer
/// that answers false, a writer that never returns, and a queue that fills.</para>
///
/// <para>The lines are read as JSON rather than through a parser, because this side has no parser —
/// <see cref="ServerNoticeLine"/> writes and the extension reads, which is what the parity harness is
/// for. What a test may assert is the BYTES, and one of these does.</para>
/// </remarks>
public sealed class TheRefusalsAreWrittenDownTests : IDisposable
{
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(10);

    private readonly string _dir = Directory.CreateTempSubdirectory("coai-refusals-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ResolvedDataDir Data => ResolvedDataDir.For(_dir);

    private string NoticesFile => Path.Combine(_dir, ServerNotices.Name);

    private string[] Lines() => File.Exists(NoticesFile) ? File.ReadAllLines(NoticesFile) : [];

    private static NoticeWriter Writing(Func<ResolvedDataDir, ServerNotice, bool>? append = null) =>
        new(append ?? ((dir, notice) => ServerNotices.Append(dir, notice)));

    /// <summary>
    /// This directory, as the environment a host would have been given.
    /// </summary>
    /// <remarks>
    /// Story 2.3.2 made the resolver take the HOST's environment rather than the process's, so a
    /// host with a scoped env stops writing its notices into the ambient one — which is also what
    /// lets a test point the whole road at a temporary directory without touching
    /// <c>COAI_DATA_DIR</c>, the process-global a parallel suite must not set.
    /// </remarks>
    private string? Env(string name) => name == "COAI_DATA_DIR" ? _dir : null;

    private Noticing Through(NoticeWriter writer, Serilog.ILogger? log = null) =>
        Noticing.Through(writer, Env, log ?? Silent, "testrun00000");

    /// <summary>A refusal, written down and waited for — the wait is the TEST's, never the product's.</summary>
    private string Answered(string sentence, string from, NoticeWriter writer, Serilog.ILogger? log = null)
    {
        var answer = Refusal.Answer(sentence, Through(writer, log), from);
        writer.Idle(LongEnough).Should().BeTrue("the writer must drain for this test to read the file");

        return answer;
    }

    private static string Field(string line, string name) =>
        JsonDocument.Parse(line).RootElement.TryGetProperty(name, out var value)
            ? value.GetString() ?? ""
            : "";

    private static string Refused(string sentence) =>
        JsonSerializer.Serialize(new ErrorAnswer(sentence), ServerJsonContext.Default.ErrorAnswer);

    [Fact]
    public void ARefusal_LandsInTheNoticesFile()
    {
        Answered("no reviewers are configured", "ReviewPlan", Writing());

        Lines().Should().HaveCount(1,
            "the panel reads this file, and a refusal nobody wrote down is a round a person watches "
            + "produce nothing with no way to learn why");
    }

    [Fact]
    public void TheLineIsWhatTheSerialiserWouldWrite()
    {
        // Not "a writer was called" — the BYTES. Story 1.2's round named the gap: until the serialiser
        // was wired to a writer, a call site could serialise a notice by hand and the parity harness
        // would stay green, because it exercises NoticeTool rather than this.
        Answered("the plan text is empty", "ReviewPlan", Writing());

        var line = Lines().Single();

        // The WHOLE file, so the comparison carries the newline the serialiser owns.
        File.ReadAllText(NoticesFile).Should().Be(ServerNoticeLine.Of(new ServerNotice
        {
            Utc = Field(line, "utc"),
            Class = "refusal",
            Source = "coai-mcp",
            Code = ServerNoticeCodes.Refused,
            Subject = "ReviewPlan",
            Title = "the plan text is empty",
            // Since epic 3 every notice carries the run that wrote it and that run's pid, stamped once
            // in `Noticing.Through` — which is why this line gained two fields and nothing else moved.
            Run = "testrun00000",
            Pid = Environment.ProcessId,
        }), "every field is the serialiser's, and the only one this test cannot predict is the clock");
        Field(line, "utc").Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T[\d:.]+Z$",
            "and the clock is the shape the extension writes, not whatever the culture produces");
    }

    [Fact]
    public void TheAnswerIsUnchanged_WhateverHappensToTheNotice()
    {
        const string sentence = "review_code refuses until a plan round reached proceed";

        Answered(sentence, "ReviewCode", Writing())
            .Should().Be(Refused(sentence),
                "the wire shape did not move: same JSON, same context, same sentence");
    }

    [Fact]
    public void AResolverThatThrows_StillReturnsTheRefusal()
    {
        // The plan round's finding, from all three providers: the first design resolved the directory
        // outside the boundary, so a COAI_DATA_DIR that cannot resolve would have thrown past the
        // return and the calling AI would have got nothing — a review that stops working because a
        // setting is wrong.
        var said = new List<string>();
        var writer = Writing();

        Refusal.Answer(
                "no",
                new Noticing(_ => throw new InvalidOperationException("COAI_DATA_SIDE is not usable"),
                    Watching(said)),
                "Somewhere")
            .Should().Be(Refused("no"));

        writer.Idle(LongEnough).Should().BeTrue();
        said.Should().ContainSingle("and the loss is reported rather than swallowed");
    }

    [Fact]
    public void AWriterThatThrows_StillReturnsTheRefusal()
    {
        var said = new List<string>();
        var writer = Writing((_, _) => throw new IOException("the share stopped answering"));

        Refusal.Answer("no", Through(writer, Watching(said)), "Somewhere")
            .Should().Be(Refused("no"));

        writer.Idle(LongEnough).Should().BeTrue();
        said.Should().ContainSingle();
    }

    [Fact]
    public void AWriterThatNeverReturns_DoesNotDelayTheRefusalAtAll()
    {
        // The code round, twelve findings across all three providers. The first fix waited two seconds
        // for a thread-pool append; a wait only stops WAITING, so a wedged NAS kept the worker forever
        // and every refusal cost two threads — at a hundred concurrent refusals the pool is gone and
        // the server stops answering, which is worse than the stall it was protecting against. There
        // is one writer thread now and the refusal does not wait for it AT ALL.
        var stuck = new ManualResetEventSlim(false);
        try
        {
            var writer = Writing((_, _) => { stuck.Wait(LongEnough); return true; });
            var clock = Stopwatch.StartNew();

            for (var each = 0; each < 50; each++)
            {
                Refusal.Answer("no", Through(writer), "Somewhere")
                    .Should().Be(Refused("no"));
            }

            clock.Stop();
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
                "fifty refusals against a writer that never returns must cost no more than fifty "
                + "queue writes — the point of the queue is that this number does not depend on the disk");
        }
        finally
        {
            stuck.Set();
        }
    }

    [Fact]
    public void AQueueThatFillsDropsTheNotice_RatherThanBlockTheRefusal()
    {
        // The bound the queue needs to BE a bound. Past `Depth` a notice is dropped, which is the loss
        // becoming visible instead of becoming a stall.
        var stuck = new ManualResetEventSlim(false);
        try
        {
            var writer = Writing((_, _) => { stuck.Wait(LongEnough); return true; });
            var offered = Enumerable.Range(0, NoticeWriter.Depth * 2)
                .Count(_ => writer.Offer(() => Data, RefusalNotices.Of("no", "Somewhere"), Silent));

            offered.Should().BeLessThan(NoticeWriter.Depth * 2,
                "a queue that never says no is not bounded, and an unbounded queue in front of a "
                + "wedged share is the memory of the process");
            offered.Should().BeGreaterThan(NoticeWriter.Depth / 2,
                "and it must actually hold what it says it holds");
        }
        finally
        {
            stuck.Set();
        }
    }

    [Fact]
    public void AWriteThatIsLost_ReachesTheLog()
    {
        // The plan round, local: a notice that silently fails to be written leaves the operator with a
        // system that looks like it worked. `Append` answers false for anything the disk gave, so false
        // is the case that must not be silent.
        var said = new List<string>();
        var writer = Writing((_, _) => false);

        Refusal.Answer("no", Through(writer, Watching(said)), "Somewhere");

        writer.Idle(LongEnough).Should().BeTrue();
        said.Should().ContainSingle()
            .Which.Should().Contain(ServerNotices.Name, "the log names the file the notice was for");
    }

    [Fact]
    public void ASecretInARefusal_DoesNotReachTheFile()
    {
        Answered("the vault refused: token=ghp_0123456789abcdefghij0123456789abcdef", "Vault", Writing());

        var line = Lines().Single();

        line.Should().NotContain("ghp_0123456789abcdefghij0123456789abcdef",
            "every string field goes through the redactor, identity fields included");
        line.Should().Contain("the vault refused", "and what is left still says what happened");
    }

    [Fact]
    public void TheWorstCaseLine_IsWithinTheDocumentedCeiling()
    {
        // §7 of the parent bounds this file's growth on a per-record figure, and codex was right on the
        // plan round that a budget over an unbounded input is not a budget. It IS bounded, by
        // `Redaction.TitleLimit`, and that is a fact this asserts rather than reads: a megabyte of
        // sentence must not produce a megabyte of line. Measured on the WHOLE file — every JSON key,
        // every escape and the newline the append adds — because that is what fills a disk.
        Answered(new string('x', 1_000_000), "Somewhere", Writing());

        new FileInfo(NoticesFile).Length.Should().BeLessThan(10 * 1024,
            "the §7 table's worst case is ~10 KB per record, and 1.1 GB/year is arithmetic over it");
        Lines().Single().Should().Contain("xxx", "and the sentence is cut, not dropped");
    }

    [Fact]
    public void TwoRefusalsFromDifferentPlaces_AreTwoRowsOnThePage()
    {
        // codex on the plan round: the extension keys repeats on (code, subject), and one
        // `code: refused` for every site would collapse "no reviewers configured" and "the plan text is
        // empty" into a single row a person cannot read.
        var writer = Writing();

        Answered("no reviewers", "ReviewPlan", writer);
        Answered("no plan", "ReviewCode", writer);

        Lines().Select(line => Field(line, "subject"))
            .Should().Equal(["ReviewPlan", "ReviewCode"],
                "two reasons are two keys, so the page shows two rows");
    }

    [Theory]
    [InlineData(typeof(Refusal), "Answer", "the one place a refusal becomes an answer")]
    [InlineData(typeof(PanelService), "Error", "the panel's own helper, in front of 25 refusals")]
    [InlineData(typeof(PanelService), "Refused", "the document wrapper that adds a log line")]
    [InlineData(typeof(PanelService), "RunStageAsync", "the body of all three rounds")]
    [InlineData(typeof(ConsultationService), "Error", "the consultation helper, in front of 23")]
    public void EveryWrapperOnTheRefusalRoad_ForwardsItsOwnCaller(Type owner, string method, string why)
    {
        // What makes the subject free: the compiler fills it at every one of the 48 call sites, so not
        // one of them had to change. It only works if EVERY hop declares it — CodeRabbit found two
        // that did not, and without them every document refusal is written down as `Refused` and every
        // stage refusal as `RunStageAsync`, which is the single collapsed row the subject exists to
        // prevent.
        //
        // Asked of the SIGNATURES rather than by driving the service, because the overload that fills
        // the subject resolves its directory from `COAI_DATA_DIR` — a process-global that a parallel
        // suite must not set. The live leg over stdio is story 2.4, which the plan owes and names.
        var from = owner
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(candidate => candidate.Name == method)
            .SelectMany(candidate => candidate.GetParameters())
            .Where(parameter => parameter.GetCustomAttribute<CallerMemberNameAttribute>() is not null)
            .ToList();

        from.Should().ContainSingle(why + " must forward the member that called IT, not its own name")
            .Which.Name.Should().Be("from");
    }

    [Fact]
    public void AHugeFileIsRolledOverBeforeItIsAppendedTo()
    {
        // The plan round, gemini and codex: `planning-docs.md` requires a plan that creates something
        // that GROWS to name its budget BEFORE the first write, with an owner and a retirement rule.
        // This is the first story with a repeating writer — 48 sites, every one reachable on every
        // round — so the rule ships with it.
        File.WriteAllText(NoticesFile, new string('x', 64) + "\n");

        ServerNotices.Append(Data, Notice("first"), rollAt: 8).Should().BeTrue();

        File.Exists(Path.Combine(_dir, ServerNotices.Archive)).Should().BeTrue(
            "the old generation is kept, not deleted");
        Lines().Should().ContainSingle("and the new one starts with the record that crossed the line");
    }

    [Fact]
    public void AFileJustUnderTheCeiling_IsRolledBeforeTheRecordPushesItOver()
    {
        // CodeRabbit on the pull request: the first version compared the file's size BEFORE the
        // append, so a file one byte under the ceiling still took a whole record and the ceiling was
        // really "the ceiling plus one record". The line is serialised once and its bytes — the
        // newline included, because that is what lands — are counted.
        var record = ServerNoticeLine.Of(Notice("first"));
        var ceiling = record.Length + 10;
        File.WriteAllText(NoticesFile, new string('x', ceiling - 1) + "\n");

        ServerNotices.Append(Data, Notice("second"), rollAt: ceiling).Should().BeTrue();

        File.Exists(Path.Combine(_dir, ServerNotices.Archive)).Should().BeTrue(
            "one byte under the ceiling plus a whole record is over it");
        new FileInfo(NoticesFile).Length.Should().BeLessThanOrEqualTo(ceiling,
            "and what is live after the roll is the record alone");
    }

    [Fact]
    public void ASmallFileIsNotRolled()
    {
        ServerNotices.Append(Data, Notice("first"), rollAt: 1024 * 1024);
        ServerNotices.Append(Data, Notice("second"), rollAt: 1024 * 1024);

        File.Exists(Path.Combine(_dir, ServerNotices.Archive)).Should().BeFalse();
        Lines().Should().HaveCount(2, "a roll that fires early loses what the page is showing");
    }

    [Fact]
    public void AFileAtTheCeilingThatCannotRoll_RefusesToGrow()
    {
        // The code round, codex: the first version rolled best-effort and appended regardless, so a
        // file at the ceiling whose rename kept failing grew without limit while the ceiling said
        // otherwise. A ceiling that yields under exactly the conditions it exists for is not one.
        // A DIRECTORY in the archive's place is what makes the move fail on both platforms — a locked
        // file would not, since only Windows refuses that.
        File.WriteAllText(NoticesFile, new string('x', 64) + "\n");
        Directory.CreateDirectory(Path.Combine(_dir, ServerNotices.Archive));

        ServerNotices.Append(Data, Notice("second"), rollAt: 8).Should().BeFalse(
            "the notice is lost, which the writer reports — the file is not grown past its ceiling");
        Lines().Should().ContainSingle("and what was already there is still there");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("token=ghp_0123456789abcdefghij0123456789abcdef")]
    [InlineData("\u0001\u0002\u0003")]
    [InlineData("пароль: hunter2 — и ещё немного текста")]
    public void NoSentenceCanBreakTheRecord(string sentence)
    {
        // What replaced a `try` nobody could reach. `Record` has no catch, and the reason is that
        // nothing in it can throw — `Offer` is a queue write with its own boundary, and `Of` builds a
        // record whose required fields are constants and whose variable fields are optional, so a
        // sentence the redactor empties is DROPPED rather than refused. Sonar found the unreachable
        // catch; this is the claim it was standing in for, checked over sentences chosen to break it.
        var recording = () => Refusal.Answer(sentence, Through(Writing()), "Somewhere");

        recording.Should().NotThrow();
    }

    [Fact]
    public void AQueuedNotice_HoldsNoMoreThanTheSerialiserWillEverWrite()
    {
        // CodeRabbit on the pull request, and it is about MEMORY rather than disk: the serialiser
        // cuts a title when the LINE is written, but the record sits in the writer's queue until
        // then — so 256 queued refusals each holding a megabyte of sentence is 256 MB of process
        // held because a share stopped answering. The cut moved to where the record is built.
        RefusalNotices.Of(new string('x', 1_000_000), "Somewhere").Title!.Length
            .Should().Be(Redaction.TitleLimit,
                "a queued notice must never hold more than can ever be written from it");
    }

    [Fact]
    public void ARefusalTooLongForATitle_SaysItWasCut()
    {
        // A defect in story 2.3.3, found while reading for 2.4. That story put the reviewer and the
        // startup roads through `ServerNotice.Shortened` and wrote, in its doc comment and in
        // module_server.md, that there were two producers and now one helper. There were THREE: this
        // road kept its own plain cut, so an over-long refusal was truncated with no sign while an
        // over-long startup note said it was cut. Refusals are the records a person most needs to
        // read to the end, because the sentence is the only thing the helper knows.
        var title = RefusalNotices.Of(new string('x', 2_000), "Somewhere").Title!;

        title.Length.Should().BeLessThanOrEqualTo(Redaction.TitleLimit);
        title.Should().EndWith("…", "a reader must be able to tell the refusal was cut");
    }

    [Fact]
    public void ANoticeProducerCutsOnlyThroughTheSharedHelper()
    {
        // The census that would have caught 2.3.3's miss. That story counted the producers by
        // remembering them, said "two", and moved two: the refusal road kept a hand-written
        // `[..Redaction.TitleLimit]` and nothing anywhere said so. A producer added tomorrow with its
        // own cut fails here on the day it is added.
        // Both spellings a hand cut takes in this codebase: a range, `[..Redaction.TitleLimit]` or
        // `[..(Redaction.TitleLimit - 1)]`, and `Substring(0, Redaction.TitleLimit)`. (gemini and codex,
        // on the code round.) A limit copied into a local first is NOT caught — text cannot follow a
        // value — and the companion below is what answers that: a producer that never calls the
        // helper is missing from the list of those that do.
        var handCuts = ProductionSources.FilesMatching(new Regex(
            @"(\[\.\.\(?\s*|Substring\(\s*0\s*,\s*)Redaction\.(Title|Detail)Limit",
            RegexOptions.CultureInvariant));

        handCuts.Keys.Should().BeEmpty(
            "a notice field is cut by ServerNotice.Shortened, which marks the cut, and by nothing else");

        // The companion, without which the census above passes on a scanner that found nothing: the
        // producers really are there, and really do call the helper.
        ProductionSources.FilesMentioning("ServerNotice.Shortened(").Keys.Order().Should().Equal(
            [
                // Story 3.2's crash record: the exception nothing else caught, written as it leaves.
                "src_mcp/src/Server/HostCrash.cs",
                "src_mcp/src/Server/RefusalNotices.cs",
                "src_mcp/src/Server/ReviewerNotices.cs",
                // Epic 3's death record: the unclean-exit a start writes about a run that never finished.
                "src_mcp/src/Server/RunMarkers.cs",
                "src_mcp/src/Server/StartupNotices.cs",
            ],
            "every road that builds a notice goes through the one cut");
    }

    [Fact]
    public void ALoggerThatThrows_DoesNotStopTheWriter()
    {
        // The code round, local: a disposed logger or a full sink throws from inside the very call
        // that reports a lost notice, and a plain catch would then swallow both. There is nowhere
        // left to say it — this process's stdout may be carrying a protocol — so what the guard buys
        // is that the writer thread SURVIVES to write the next one, which is what this asserts.
        // The FIRST notice is refused by the disk, so the writer reports it and the report throws.
        // The second goes through the same writer and must land, which is the survival being asserted
        // — the writer thread is single, and a thread that died of its own warning writes nothing
        // ever again.
        var refused = true;
        var writer = Writing((dir, notice) =>
        {
            if (refused)
            {
                refused = false;

                return false;
            }

            return ServerNotices.Append(dir, notice);
        });

        Refusal.Answer("lost", Through(writer, Exploding), "Somewhere");
        writer.Idle(LongEnough).Should().BeTrue("the writer must not die of its own warning");

        Answered("and the next one still lands", "Later", writer);
        Lines().Should().ContainSingle("the writer is still writing after the log threw");
    }

    private static ServerNotice Notice(string title) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = "refusal",
        Source = "coai-mcp",
        Code = ServerNoticeCodes.Refused,
        Title = title,
    };

    /// <summary>A logger that is real and says nothing — never null, per the C# rule on nulls.</summary>
    private static Serilog.ILogger Silent => Serilog.Core.Logger.None;

    /// <summary>A logger whose sink throws — a disposed one, or a sink that has filled.</summary>
    private static Serilog.ILogger Exploding =>
        new Serilog.LoggerConfiguration().WriteTo.Sink(new Throwing()).CreateLogger();

    private sealed class Throwing : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) =>
            throw new ObjectDisposedException(nameof(Throwing));
    }

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

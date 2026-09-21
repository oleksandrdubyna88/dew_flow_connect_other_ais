using System.Text.Json;
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
/// <para><b>The one thing this must not get wrong.</b> A notice that cannot be written must never
/// fail the refusal. The plan round put five findings on that single sentence, from all three
/// providers, and every one of them was right about the same gap: the pseudocode resolved the data
/// directory OUTSIDE the failure boundary, so a misconfigured <c>COAI_DATA_DIR</c> would have thrown
/// past the return and the calling AI would have received nothing at all. Directory resolution,
/// record construction and the append are one boundary now, and the tests below drive a throwing
/// resolver, a throwing writer, a writer that answers false and a writer that never returns.</para>
///
/// <para>The lines are read as JSON rather than through a parser, because this side has no parser —
/// <see cref="ServerNoticeLine"/> writes and the extension reads, which is the whole point of the
/// parity harness. What a test may assert is the BYTES, and one of these does.</para>
/// </remarks>
public sealed class TheRefusalsAreWrittenDownTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-refusals-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ResolvedDataDir Data => ResolvedDataDir.For(_dir);

    private string NoticesFile => Path.Combine(_dir, ServerNotices.Name);

    private string[] Lines() => File.Exists(NoticesFile) ? File.ReadAllLines(NoticesFile) : [];

    private static string Field(string line, string name) =>
        JsonDocument.Parse(line).RootElement.TryGetProperty(name, out var value)
            ? value.GetString() ?? ""
            : "";

    private static string Refused(string sentence) =>
        JsonSerializer.Serialize(new ErrorAnswer(sentence), ServerJsonContext.Default.ErrorAnswer);

    [Fact]
    public void ARefusal_LandsInTheNoticesFile()
    {
        Refusal.Answer("no reviewers are configured", "ReviewPlan", log: null, () => Data);

        Lines().Should().HaveCount(1,
            "the panel reads this file, and a refusal nobody wrote down is a round a person watches "
            + "produce nothing with no way to learn why");
    }

    [Fact]
    public void TheLineIsWhatTheSerialiserWouldWrite()
    {
        // Not "a writer was called" — the BYTES. Story 1.2's round named the gap: until the
        // serialiser was wired to a writer, a call site could serialise a notice by hand and the
        // parity harness would stay green, because it exercises NoticeTool rather than this.
        Refusal.Answer("the plan text is empty", "ReviewPlan", log: null, () => Data);

        var line = Lines().Single();

        // The WHOLE file, so the comparison carries the newline the serialiser owns: `Of` ends the
        // line and the append writes exactly what it produced, which is the property this asserts.
        File.ReadAllText(NoticesFile).Should().Be(ServerNoticeLine.Of(new ServerNotice
        {
            Utc = Field(line, "utc"),
            Class = "refusal",
            Source = "coai-mcp",
            Code = ServerNoticeCodes.Refused,
            Subject = "ReviewPlan",
            Title = "the plan text is empty",
        }), "every field is the serialiser's, and the only one this test cannot predict is the clock");
        Field(line, "utc").Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T[\d:.]+Z$",
            "and the clock is the shape the extension writes, not whatever the culture produces");
    }

    [Fact]
    public void TheAnswerIsUnchanged_WhateverHappensToTheNotice()
    {
        const string sentence = "review_code refuses until a plan round reached proceed";

        Refusal.Answer(sentence, "ReviewCode", log: null, () => Data)
            .Should().Be(Refused(sentence),
                "the wire shape did not move: same JSON, same context, same sentence");
    }

    [Fact]
    public void AResolverThatThrows_StillReturnsTheRefusal()
    {
        // The plan round's finding, from all three providers: the pseudocode resolved the directory
        // outside the boundary, so a COAI_DATA_DIR that cannot resolve would have thrown past the
        // return and the calling AI would have got nothing — a review that stops working because a
        // setting is wrong.
        Refusal.Answer("no", "Somewhere", log: null,
                () => throw new InvalidOperationException("COAI_DATA_SIDE is not usable"))
            .Should().Be(Refused("no"));
    }

    [Fact]
    public void AWriterThatThrows_StillReturnsTheRefusal()
    {
        Refusal.Answer("no", "Somewhere", log: null, () => Data,
                (_, _) => throw new IOException("the share stopped answering"), RefusalNotices.Budget)
            .Should().Be(Refused("no"));
    }

    [Fact]
    public void AWriterThatNeverReturns_StillReturnsTheRefusalInsideItsBudget()
    {
        // codex on the plan round: this product's data directory has been a NAS share, and a
        // synchronous append to a stalled one blocks. The calling AI would then time out and receive
        // no refusal at all — the failure this whole story exists to prevent, arriving by a new road.
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var answer = Refusal.Answer("no", "Somewhere", log: null, () => Data,
            (_, _) => { Thread.Sleep(TimeSpan.FromMinutes(5)); return true; },
            TimeSpan.FromMilliseconds(300));

        clock.Stop();
        answer.Should().Be(Refused("no"));
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "the write is time-bounded and explicitly lossy: a notice is worth waiting a moment for "
            + "and is never worth a refusal that never arrives");
    }

    [Fact]
    public void AWriteThatIsLost_ReachesTheLog()
    {
        // The plan round, local: a notice that silently fails to be written leaves the operator with
        // a system that looks like it worked. `Append` answers false for anything the DISK gave, so
        // false is the case that must not be silent.
        var said = new List<string>();

        Refusal.Answer("no", "Somewhere", Watching(said), () => Data, (_, _) => false,
            RefusalNotices.Budget);

        said.Should().ContainSingle()
            .Which.Should().Contain("Somewhere", "the log names which refusal was lost");
    }

    [Fact]
    public void ASecretInARefusal_DoesNotReachTheFile()
    {
        Refusal.Answer("the vault refused: token=ghp_0123456789abcdefghij0123456789abcdef", "Vault",
            log: null, () => Data);

        var line = Lines().Single();

        line.Should().NotContain("ghp_0123456789abcdefghij0123456789abcdef",
            "every string field goes through the redactor, identity fields included");
        line.Should().Contain("the vault refused", "and what is left still says what happened");
    }

    [Fact]
    public void TheWorstCaseLine_IsWithinTheDocumentedCeiling()
    {
        // §7 of the parent bounds this file's growth on a per-record figure, and codex was right on
        // the plan round that a budget over an unbounded input is not a budget. It IS bounded, by
        // `Redaction.TitleLimit`, and that is a fact this asserts rather than reads: a megabyte of
        // sentence must not produce a megabyte of line. Measured on the WHOLE file — every JSON key,
        // every escape and the newline the append adds — because that is what fills a disk.
        Refusal.Answer(new string('x', 1_000_000), "Somewhere", log: null, () => Data);

        new FileInfo(NoticesFile).Length.Should().BeLessThan(10 * 1024,
            "the §7 table's worst case is ~10 KB per record, and 1.1 GB/year is arithmetic over it");
        Lines().Single().Should().Contain("xxx", "and the sentence is cut, not dropped");
    }

    [Fact]
    public void TwoRefusalsFromDifferentPlaces_AreTwoRowsOnThePage()
    {
        // codex on the plan round: the extension keys repeats on (code, subject), and one
        // `code: refused` for every site would collapse "no reviewers configured" and "the plan text
        // is empty" into a single row a person cannot read. The subject is the CALLING MEMBER, filled
        // by the compiler through [CallerMemberName] — a stable key, no round numbers in it, and not
        // one of the 48 call sites had to change to get it.
        Refusal.Answer("no reviewers", "ReviewPlan", log: null, () => Data);
        Refusal.Answer("no plan", "ReviewCode", log: null, () => Data);

        Lines().Select(line => Field(line, "subject"))
            .Should().Equal(["ReviewPlan", "ReviewCode"],
                "two reasons are two keys, so the page shows two rows");
    }

    [Fact]
    public void TheCallerNeedNotNameItself()
    {
        // What makes the subject free: the compiler fills it at every one of the 48 sites.
        Refusal.Answer("no", log: null, where: () => Data);

        Field(Lines().Single(), "subject").Should().Be(nameof(TheCallerNeedNotNameItself));
    }

    [Fact]
    public void AHugeFileIsRolledOverBeforeItIsAppendedTo()
    {
        // The plan round, gemini and codex: `.agents/conventions/common/planning-docs.md` requires a
        // plan that creates something that GROWS to name its budget BEFORE the first write, with an
        // owner and a retirement rule. This is the first story with a repeating writer — 48 sites,
        // every one reachable on every round — so the rule ships with it: at the ceiling the file
        // becomes `server-notices.1.jsonl` and a new one starts, which makes the §7 figure of 256 MB
        // a hard maximum for the pair rather than a trigger for work nobody has scheduled.
        File.WriteAllText(NoticesFile, new string('x', 64) + "\n");
        var rolled = Path.Combine(_dir, ServerNotices.Archive);

        ServerNotices.Append(Data, Notice("first"), rollAt: 8);

        File.Exists(rolled).Should().BeTrue("the old generation is kept, not deleted");
        Lines().Should().ContainSingle("and the new one starts with the record that crossed the line");
    }

    [Fact]
    public void ASmallFileIsNotRolled()
    {
        ServerNotices.Append(Data, Notice("first"), rollAt: 1024 * 1024);
        ServerNotices.Append(Data, Notice("second"), rollAt: 1024 * 1024);

        File.Exists(Path.Combine(_dir, ServerNotices.Archive)).Should().BeFalse();
        Lines().Should().HaveCount(2, "a roll that fires early loses what the page is showing");
    }

    private static ServerNotice Notice(string title) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = "refusal",
        Source = "coai-mcp",
        Code = ServerNoticeCodes.Refused,
        Title = title,
    };

    private static Serilog.ILogger Watching(List<string> said) =>
        new Serilog.LoggerConfiguration().WriteTo.Sink(new Collecting(said)).CreateLogger();

    private sealed class Collecting(List<string> said) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => said.Add(logEvent.RenderMessage());
    }
}

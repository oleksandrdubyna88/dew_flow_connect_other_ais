using System.Text;
using System.Text.Json;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ONE append in this repository: the bargain <c>UsageLedger</c> paid for, extracted, with a
/// torn-tail quarantine written inside the record's own single write.
/// </summary>
/// <remarks>
/// <para>Every test here is about a record that is silently not written, or a record that silently
/// destroys the one after it — which is the failure mode of everything that touches this file.
/// Newlines are spelled by NUMBER throughout, because an escape in this repository has reached
/// disk as the raw byte three times.</para>
/// <para>What these tests cannot do is fork a second PROCESS: that is <c>npm run measure:append
/// --dotnet=N</c>, which drives <c>JsonlLedger.AppendLine</c> from <c>NoticeTool append</c> beside
/// node writers and checks every line back.</para>
/// </remarks>
public sealed class JsonlLedgerTests : IDisposable
{
    private const byte Newline = 10;

    private static readonly string NewlineText = ((char)Newline).ToString();

    /// <summary>
    /// A directory of this test's OWN — xUnit constructs this class once per test method, so every
    /// test gets a fresh <c>CreateTempSubdirectory</c> and disposes it after.
    /// </summary>
    /// <remarks>
    /// Written down because the code round read the shared field as shared STATE across parallel
    /// tests and asked for a directory per test method. It already is one; what the round was right
    /// about is that nothing SAID so. The invariant is not only documented but checked: the test
    /// that a missing file is created by the append itself asserts the ledger does not pre-exist,
    /// and it is the first thing that would fail if this class were ever instantiated once for all
    /// of its tests.
    /// </remarks>
    private readonly TempDir _dir = TempDir.For("coai-jsonl-");

    public void Dispose() => _dir.Dispose();

    private string Ledger => _dir.At("ledger.jsonl");

    private static string Record(string name, int padding = 0) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["name"] = name, ["pad"] = new string('x', padding) });

    private string[] Lines() =>
        File.ReadAllText(Ledger).Split(NewlineText);

    // ---------- the bargain ----------

    [Fact]
    public void ALineIsWritten_AndTheAnswerSaysSo()
    {
        JsonlLedger.AppendLine(Ledger, Record("one") + NewlineText).Should().BeTrue();
        JsonlLedger.AppendLine(Ledger, Record("two") + NewlineText).Should().BeTrue();

        // An explicit array, never `Equal(a, b, "", "because")`: the params overload would take the
        // reason as a fourth expected element.
        Lines().Should().Equal(new[] { Record("one"), Record("two"), "" },
            "two records, two lines, and the final newline leaves the empty entry the reader expects");
    }

    [Fact]
    public void AMissingDirectory_IsCreated_BecauseTheFirstWriteIsOftenTheFirstThingInIt()
    {
        var deep = _dir.At("not", "yet", "there", "ledger.jsonl");

        JsonlLedger.AppendLine(deep, Record("first") + NewlineText).Should().BeTrue();

        File.ReadAllText(deep).Should().Be(Record("first") + NewlineText);
    }

    [Fact]
    public void ASecondProcessHoldingTheFileOpenForAppend_DoesNotCostALine()
    {
        // The hard-won flag nobody re-derives: `File.AppendAllText` takes a write lock the other
        // process cannot pass, and two servers share one data directory routinely. The other
        // server is stood in for by a second handle opened exactly as the writer opens its own.
        JsonlLedger.AppendLine(Ledger, Record("mine") + NewlineText);
        using var otherServer = new FileStream(Ledger, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        JsonlLedger.AppendLine(Ledger, Record("mine-too") + NewlineText)
            .Should().BeTrue("a spending record with a silent gap is worse than one that errors");

        ReadWhileOpen().Should().Equal(Record("mine"), Record("mine-too"), "");
    }

    // ---------- it never throws, and "never" is a list ----------

    [Fact]
    public void ADirectoryInTheFilesPlace_AnswersFalseAndThrowsNothing()
    {
        // The vector `notificationsFile.ts` documents: on Windows a directory where the file should
        // be OPENS cleanly for reading and fails at the write; for an append it is refused at the
        // open. Either way the caller — a refusal on its way back to the calling AI — is not failed.
        Directory.CreateDirectory(Ledger);

        var appending = () => JsonlLedger.AppendLine(Ledger, Record("lost") + NewlineText);

        appending.Should().NotThrow();
        appending().Should().BeFalse("the caller can count what was lost, which is the whole reason it answers");
    }

    /// <summary>
    /// A rooted, non-empty directory the OS refuses — the hole two reviewers found independently.
    /// </summary>
    /// <remarks>
    /// <para>The plan named <c>C:\data*</c> and said it throws <c>ArgumentException</c>. MEASURED on
    /// .NET 10 / Windows 11, 2026-09-21: it throws <c>IOException</c> ("The filename, directory name,
    /// or volume label syntax is incorrect"), which the two-type catch inherited from
    /// <c>UsageLedger</c> already covered. The shape that really escapes that catch is a path
    /// carrying a NUL — <c>ArgumentException</c>, "Null character in path", on every platform — so
    /// that is the fixture with teeth, spelled by number. The wildcard is kept on Windows, where it
    /// is unusable; on POSIX <c>data*</c> is a legal directory name and would be WRITTEN, which is
    /// not a refusal to test.</para>
    /// </remarks>
    public static TheoryData<string, string> UnusableRootedDirectories()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var data = new TheoryData<string, string>
        {
            { Path.Combine(root, "data" + (char)0 + "x", "ledger.jsonl"), "a NUL inside a segment" },
        };
        if (OperatingSystem.IsWindows())
        {
            data.Add(Path.Combine(root, "data*", "ledger.jsonl"), "a wildcard in a segment");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnusableRootedDirectories))]
    public void AnUnusableRootedDirectory_AnswersFalseAndThrowsNothing(string path, string why)
    {
        var appending = () => JsonlLedger.AppendLine(path, Record("lost") + NewlineText);

        appending.Should().NotThrow(why);
        appending().Should().BeFalse(why);
    }

    [Fact]
    public void TheNulFixture_ReallyThrowsSomethingOtherThanAnIOException_SoTheListIsWhatCatchesIt()
    {
        // The companion, so the theory above cannot pass for the wrong reason: if this path threw
        // an IOException, the test would be exercising the catch UsageLedger always had and saying
        // nothing about the three types the round added.
        var nul = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "data" + (char)0 + "x", "ledger.jsonl");

        var opening = () => new FileStream(nul, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        opening.Should().Throw<ArgumentException>("this is the exception the plan's list exists to catch")
            .And.Should().NotBeOfType<ArgumentNullException>();
    }

    // ---------- the torn tail ----------

    [Fact]
    public void ATornTail_IsQuarantined_AndTheRecordAfterItIsWholeAndParses()
    {
        // A process killed mid-line leaves a file that does not end in a newline. Without the
        // quarantine the next record is fused onto the wreck — the fragment costs one record and
        // takes the one after it too.
        File.WriteAllText(Ledger, """{"name":"killed mid-li""");

        JsonlLedger.AppendLine(Ledger, Record("after") + NewlineText).Should().BeTrue();

        var lines = Lines();
        lines.Should().Equal("""{"name":"killed mid-li""", Record("after"), "");
        JsonDocument.Parse(lines[1]).RootElement.GetProperty("name").GetString().Should().Be("after");
    }

    [Fact]
    public void TheRepairIsOneAppend_NeverANewlineOfItsOwn()
    {
        // codex's Blocking finding, as a test. Two writes are not one: A reads a torn tail, B
        // appends a whole record, A appends its bare newline — and B's record is fused into the
        // corrupt line for ever with A's newline sitting harmlessly after it. So the newline is
        // PREPENDED to the record and the whole thing goes out in the single append that was going
        // to happen anyway. Counted through the seam, around the real writer.
        File.WriteAllText(Ledger, """{"name":"killed mid-li""");
        var appended = new List<byte[]>();

        JsonlLedger.AppendLine(Ledger, Record("after") + NewlineText, (path, record) =>
        {
            appended.Add(record);

            return AppendOnlyFile.Write(path, record);
        }).Should().BeTrue();

        appended.Should().ContainSingle("one append per line").Which[0].Should().Be(10,
            "the repair newline and the record must be ONE append, or another process can land between them");
        Lines().Should().Equal("""{"name":"killed mid-li""", Record("after"), "");
    }

    [Fact]
    public void ATailTornByAPeer_AfterThisProcessHasAlreadyWritten_IsStillRepaired()
    {
        // gemini's Major finding: a once-per-process check would never look again, so a peer that
        // crashes after this process's first write leaves a wreck the next record is fused onto.
        // The tail is read before EVERY append; this is the test that pins the absence of a flag.
        JsonlLedger.AppendLine(Ledger, Record("first") + NewlineText);
        using (var peer = new FileStream(Ledger, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            peer.Write(Encoding.UTF8.GetBytes("""{"name":"the peer died he"""));
        }

        JsonlLedger.AppendLine(Ledger, Record("second") + NewlineText).Should().BeTrue();

        Lines().Should().Equal(Record("first"), """{"name":"the peer died he""", Record("second"), "");
    }

    [Fact]
    public void AnEmptyOrMissingFile_GetsNoLeadingNewline()
    {
        // The other direction of the quarantine: a file with no last byte has no torn tail, and a
        // leading newline would be a blank first line in every ledger this product ever creates.
        var missing = _dir.At("missing.jsonl");
        var empty = _dir.At("empty.jsonl");
        File.WriteAllText(empty, string.Empty);

        JsonlLedger.AppendLine(missing, Record("m") + NewlineText);
        JsonlLedger.AppendLine(empty, Record("e") + NewlineText);

        File.ReadAllBytes(missing)[0].Should().Be((byte)'{', "a missing file starts with its first record");
        File.ReadAllBytes(empty)[0].Should().Be((byte)'{', "an empty file starts with its first record");
    }

    [Fact]
    public void TwoProcessesBothRepairing_LeaveOneBlankLine_AndBothRecordsWhole()
    {
        // The stated residual: two processes read the same torn tail, both prepend a newline, and
        // the second newline is a blank line. The reader tolerates it (asserted on the extension
        // side, with the byte offsets) at the cost of one phantom unread — taken, against a lost
        // record. The peer's repair-plus-record lands between this process's tail read and its
        // write, through the opener seam, which is exactly where another process would land it.
        File.WriteAllText(Ledger, """{"name":"killed mid-li""");
        var peersLine = NewlineText + Record("peer") + NewlineText;

        JsonlLedger.AppendLine(Ledger, Record("ours") + NewlineText, (path, record) =>
        {
            AppendOnlyFile.Write(path, Encoding.UTF8.GetBytes(peersLine));

            return AppendOnlyFile.Write(path, record);
        }).Should().BeTrue();

        Lines().Should().Equal(new[] { """{"name":"killed mid-li""", Record("peer"), "", Record("ours"), "" },
            "the fragment, the peer's whole record, ONE blank line, our whole record");
    }

    // ---------- the terminator ----------

    [Fact]
    public void ALineArrivingWithoutItsTerminator_IsTerminated_AndOneArrivingWithItIsNotDoubled()
    {
        // Both readings corrupt the file: a doubled newline is a blank line between every record,
        // which `countSince` counts; a missing one fuses every record into one line. The writer adds
        // a missing terminator rather than throwing, because a best-effort writer may not throw and
        // an unterminated line is the exact corruption this story exists to prevent.
        JsonlLedger.AppendLine(Ledger, Record("bare"));
        JsonlLedger.AppendLine(Ledger, Record("terminated") + NewlineText);

        File.ReadAllText(Ledger).Should().Be(
            Record("bare") + NewlineText + Record("terminated") + NewlineText);
    }

    // ---------- the in-process lock ----------

    [Fact]
    public void ConcurrentInProcessAppends_ProduceWholeLines()
    {
        // Eight threads, two hundred records each, at the awkward sizes the measurement harness
        // uses — one of them past the size any single write is atomic for.
        int[] paddings = [0, 200, 4000, 8200, 60_000];
        const int writers = 8;
        const int each = 200;

        Parallel.For(0, writers, writer =>
        {
            for (var index = 0; index < each; index++)
            {
                JsonlLedger.AppendLine(Ledger, Record($"{writer}:{index}", paddings[index % paddings.Length]) + NewlineText)
                    .Should().BeTrue();
            }
        });

        var lines = Lines().Where(line => line.Length > 0).ToList();
        lines.Should().HaveCount(writers * each);
        foreach (var line in lines)
        {
            var row = JsonDocument.Parse(line).RootElement;
            var index = int.Parse(row.GetProperty("name").GetString()!.Split(':')[1]);
            row.GetProperty("pad").GetString()!.Length.Should().Be(paddings[index % paddings.Length],
                "a line can be valid JSON and still be two halves of two records");
        }
    }

    /// <summary>Reads the ledger the way anything else would have to while a server is writing it.</summary>
    private string[] ReadWhileOpen()
    {
        using var stream = new FileStream(Ledger, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(NewlineText);
    }
}

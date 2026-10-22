using CoaiMcp.Acp;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The risk consult (722eeb66) on the feature gate's fix: the stderr line the shim QUOTES is safe from where the redactor cannot see (a line cut from its block, a line cut by the
/// capture boundary) and a redacted line still stops the ladder. Every case asserts that no FRAGMENT of the planted secret reaches the diagnosis line, the shim's report JSON, or
/// the three lines a person reads (round summary, ledger, log) — not only <see cref="AcpDiagnosis.Line"/>.
/// </summary>
public sealed class TheStderrLineIsQuotedSafelyTests : IDisposable
{
    private const string NamesNoRow = "the agent answered with an error (-32603): Internal error";

    private const string Phrase = "reached your monthly usage limit";

    /// <summary>The measured sentence from its phrase on.</summary>
    private static readonly string FromThePhrase = KimiMonthlyQuotaTests.MonthlyLine[KimiMonthlyQuotaTests.MonthlyLine.IndexOf(Phrase, StringComparison.Ordinal)..];

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-quoted-safely-");

    public void Dispose() => _dir.Dispose();

    private static AcpOutcome Outcome(string stderr) =>
        new(AcpEnd.AgentFailed, string.Empty, NamesNoRow, string.Empty, "s", AcpUsage.Unknown, [], [], [], stderr, 0);

    /// <summary>Every place the quoted line is written: the diagnosis line, the shim's report, and the round's three lines — after one real round on that report.</summary>
    private async Task<(AcpDiagnosis Diagnosis, string Report, TheRound Round)> ThroughTheProduct(string stderr)
    {
        var outcome = Outcome(stderr);
        var diagnosis = AskAcpMode.Diagnose(new KimiAcpProfile(), outcome);
        var report = AskAcpMode.Report(outcome, diagnosis);
        var round = await new AKimiRound(_dir).RunAsync(new ProcessResult(AcpWords.SpentExit, report, string.Empty, TimedOut: false));

        return (diagnosis, report, round);
    }

    private static void NothingOf(string secret, int fragment, (AcpDiagnosis Diagnosis, string Report, TheRound Round) seen)
    {
        var part = secret[..fragment];

        seen.Diagnosis.Line.Should().NotContain(part, "the diagnosis line");
        seen.Report.Should().NotContain(part, "the shim's report JSON");
        seen.Round.Summary.Should().NotContain(part, "the round summary");
        seen.Round.Ledger.Should().NotContain(part, "the ledger line");
        seen.Round.Log.Should().NotContain(part, "the log line");
    }

    private static void ReadsAsASpentQuota(TheRound round)
    {
        round.Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>().Subject.Attempts.Should().Be(1);
        round.Shims.Should().Be(1, "a spent quota is never climbed");
    }

    // ---------- R1: a line cut from its block ----------

    [Fact]
    public async Task ALineInsideAPrivateKeyBlock_IsRedactedWithItsBlock_NotQuotedWithTheKeyBodyBesideIt()
    {
        // A PEM block is the one redaction shape that spans lines: isolate-then-redact saw a body line with no BEGIN above it and quoted the key material next to the phrase.
        const string body = "MIIEvQIBADANBgkqhkiG9w0BAQEFAASC";
        var stderr = $"noise\n-----BEGIN PRIVATE KEY-----\n{body} {FromThePhrase}\n{body}BKcwggSjAgEAAoIBAQC\n-----END PRIVATE KEY-----\n";
        Redaction.SafeSource(stderr).Should().NotContain(body, "the premise: redacting the WHOLE text takes the key body out, phrase line included");

        var seen = await ThroughTheProduct(stderr);

        seen.Diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent, "the row still matches: the name is read from the captured text");
        NothingOf(body, 12, seen);
        ReadsAsASpentQuota(seen.Round);
        RateLimit.Hopeless(((ReviewerOutcome.RateLimited)seen.Round.Outcome).Reason).Should().BeTrue("with no line to quote the table's phrases stand for the name");
    }

    // ---------- R2: the capture boundary ----------

    /// <summary>The stderr as <see cref="AcpTransport"/> keeps it: a <see cref="BoundedText"/> of the real cap fed in the transport's 8192-character reads.</summary>
    private static string Captured(string stream)
    {
        var kept = new BoundedText(AcpTransport.StdErrChars);
        foreach (var chunk in stream.Chunk(8192))
        {
            kept.Append(chunk, chunk.Length);
        }

        kept.Truncated.Should().BeTrue("the case needs a stream longer than the capture");

        return kept.ToString();
    }

    [Fact]
    public async Task ATokenCutByTheCaptureBoundary_LeavesNoFragmentInAnyRecordedText()
    {
        // The capture keeps the HEAD and ends mid-line: 5 characters of the token sit before the cut, and Bearer wants 8, so the fragment is not a credential to the redactor.
        const int before = 5;
        var token = Opaque(59);
        var head = FromThePhrase + " Bearer ";
        var stderr = Captured(new string('p', AcpTransport.StdErrChars - head.Length - before - 1) + "\n" + head + token + "\nmore output after the cut\n");
        stderr.Should().Contain(head + token[..before] + "\n[coai: output truncated", "the arithmetic that gives this test its teeth: the capture ends 5 characters into the token");

        var seen = await ThroughTheProduct(stderr);

        seen.Diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent, "the row is read from what was captured, the cut line included");
        NothingOf(token, before, seen);
        seen.Diagnosis.Line.Should().BeEmpty("the last, incomplete line of a truncated capture is never quoted");
        ReadsAsASpentQuota(seen.Round);
    }

    [Fact]
    public void ACompleteLineBeforeTheCut_IsQuoted_AndTheTruncationSentenceIsNeverVendorText()
    {
        var stderr = Captured(KimiMonthlyQuotaTests.MonthlyLine + "\n" + new string('p', AcpTransport.StdErrChars) + "\n");

        var diagnosis = AcpDiagnosis.Of(NamesNoRow, stderr, KimiAcpProfile.Failures);

        diagnosis.Line.Should().Be(KimiMonthlyQuotaTests.MonthlyLine);
        diagnosis.Line.Should().NotContain("[coai:");
    }

    [Fact]
    public void ATruncationSentence_HoldingAPhraseOfItsOwn_IsNotSearched()
    {
        // What BoundedText appends is ours, not the vendor's: even a phrase inside it (it never holds one today) names no row.
        var stderr = "noise\n" + BoundedText.TruncationOpening + " at 1 characters; reached your monthly usage limit more were dropped]\n";

        AcpDiagnosis.Of(NamesNoRow, stderr, KimiAcpProfile.Failures).Known.Should().BeFalse();
    }

    [Fact]
    public void AnUntruncatedStderr_KeepsItsLastLine_EvenWithoutATrailingNewline() =>
        AcpDiagnosis.Of(NamesNoRow, "noise\n" + KimiMonthlyQuotaTests.MonthlyLine, KimiAcpProfile.Failures).Line.Should().Be(KimiMonthlyQuotaTests.MonthlyLine);

    // ---------- R3: a redacted line stays hopeless ----------

    [Fact]
    public async Task ARedactedFailureLine_StillStopsTheLadder_AfterOneLaunch()
    {
        // BoundedScheduler decides to climb with !RateLimit.Hopeless(limited.Reason) (BoundedScheduler.cs:624, :673): a reason that quotes only the marker is not hopeless, so a spent quota was retried.
        var said = KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, "Internal error", failure: KimiAcpProfile.QuotaSpent, failureLine: Redaction.Redacted);

        var round = await new AKimiRound(_dir).RunAsync(said);

        var limited = round.Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>().Subject;
        RateLimit.Hopeless(limited.Reason).Should().BeTrue(limited.Reason);
        limited.Attempts.Should().Be(1);
        round.Shims.Should().Be(1, "a spent quota is never climbed");
    }

    [Theory]
    [InlineData("something odd")]
    [InlineData("[redacted]")]
    public void AQuotedLine_ThatIsNotHopelessItself_GetsTheTablesPhrasesAppended(string line)
    {
        var kimi = new KimiRuntime("kimi", HostKind.Windows, new KimiSeams(
            root => new KimiPlaces(root, "persons", "persons-bin", [], string.Empty, "kimi.exe"), () => ("coai-mcp.exe", [])));
        var invocation = new ReviewerInvocation("kimi", RoleCatalog.ArchitectureRole, new ProcessRequest("coai-mcp.exe", ["--ask-acp"], Path.GetTempPath()) { Timeout = TimeSpan.FromSeconds(30) }, string.Empty, kimi, Model: "kimi-code/k3");
        var said = KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, "Internal error", failure: KimiAcpProfile.QuotaSpent, failureLine: line);

        var reason = kimi.RateLimited(invocation, said).Reason;

        reason.Should().Contain(line).And.Contain("monthly");
        RateLimit.Hopeless(reason).Should().BeTrue(reason);
    }

    /// <summary>An opaque token of the given length: letters and digits only, nothing but the Bearer label makes Redaction recognise it.</summary>
    private static string Opaque(int length) => string.Concat(Enumerable.Range(0, length).Select(i => "Zq8Lm3XvK7pRt2WbN5HcY9dFg4Js6AeU1oIx0Q"[(i * 7 + 3) % 38]));
}

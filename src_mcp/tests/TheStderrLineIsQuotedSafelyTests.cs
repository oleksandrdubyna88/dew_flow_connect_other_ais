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

    private static AcpOutcome Outcome(string stderr, bool truncated = false) =>
        new(AcpEnd.AgentFailed, string.Empty, NamesNoRow, string.Empty, "s", AcpUsage.Unknown, [], [], [], stderr, 0) { StdErrTruncated = truncated };

    /// <summary>Every place the quoted line is written: the diagnosis line, the shim's report, and the round's three lines — after one real round on that report.</summary>
    private async Task<(AcpDiagnosis Diagnosis, string Report, TheRound Round)> ThroughTheProduct(string stderr, bool truncated = false)
    {
        var outcome = Outcome(stderr, truncated);
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

    // ---------- T3 of the feature gate's round 2: an account id of a shape nobody measured ----------

    [Theory]
    [InlineData("org-", "0a1b_2c3d_4e5f")]
    [InlineData("ak-", "9z8y-7x6w-5v4u")]
    [InlineData("org-", "0a1b2c3d4e5f_6a7b8c9d")]
    public async Task AnAccountIdWithAnUnderscoreOrAHyphenInIt_ReachesNoRecordedText_WhenItSitsOnTheQuotedLine(string prefix, string body)
    {
        // The capture step writes the vendor's first real line to the ledger and the log before anyone reads it; an id whose body is not plain letters and digits must not slip past the Moonshot rule.
        var stderr = $"noise\nYour account {prefix}{body} is suspended. {KimiMonthlyQuotaTests.MonthlyLine}\n";

        var seen = await ThroughTheProduct(stderr);

        seen.Diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent);
        seen.Diagnosis.Line.Should().Contain(Phrase);
        NothingOf(body, 4, seen);
        NothingOf(body[^4..], 4, seen);
        ReadsAsASpentQuota(seen.Round);
    }

    // ---------- R2: the capture boundary — truncation is STATE carried from the capture, never parsed out of its rendered text ----------

    /// <summary>The stderr as <see cref="AcpTransport"/> keeps it: a <see cref="BoundedText"/> of the real cap fed in the transport's 8192-character reads — its kept text and its own truncated flag.</summary>
    private static (string Text, bool Truncated) Captured(string stream)
    {
        var kept = new BoundedText(AcpTransport.StdErrChars);
        foreach (var chunk in stream.Chunk(8192))
        {
            kept.Append(chunk, chunk.Length);
        }

        kept.Truncated.Should().BeTrue("the case needs a stream longer than the capture");

        return (kept.Kept, kept.Truncated);
    }

    [Fact]
    public async Task ATokenCutByTheCaptureBoundary_LeavesNoFragmentInAnyRecordedText_AndTheCompleteLineBeforeItIsStillQuoted()
    {
        // The capture keeps the HEAD and ends mid-line: 5 characters of the token sit before the cut, and Bearer wants 8, so the fragment is not a credential to the redactor.
        const int before = 5;
        var token = Opaque(59);
        const string head = "note Bearer ";
        var complete = KimiMonthlyQuotaTests.MonthlyLine + "\n";
        var (stderr, truncated) = Captured(complete + new string('p', AcpTransport.StdErrChars - complete.Length - head.Length - before - 1) + "\n" + head + token + "\nmore output after the cut\n");
        stderr.Should().EndWith(head + token[..before], "the arithmetic that gives this test its teeth: the capture ends 5 characters into the token");

        var seen = await ThroughTheProduct(stderr, truncated);

        seen.Diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent);
        seen.Diagnosis.Line.Should().Be(KimiMonthlyQuotaTests.MonthlyLine, "the complete line before the cut is the one quoted");
        NothingOf(token, before, seen);
        ReadsAsASpentQuota(seen.Round);
    }

    [Fact]
    public async Task APhraseOnlyOnTheCutLine_StillNamesItsRow_AndOnlyItsQuoteIsWithheld_SoTheSpentQuotaStopsTheLadder()
    {
        // The row is read from the whole kept head (the phrase is intact whenever it matches; the sentence is not in the text at all); the quote only from complete lines.
        // Unclassified, a spent quota is not hopeless and the 429 ladder would retry it.
        var token = Opaque(59);
        var (stderr, truncated) = Captured(new string('p', AcpTransport.StdErrChars - FromThePhrase.Length - 20) + "\n" + FromThePhrase + " Bearer " + token + "\nmore\n");
        stderr.Should().EndWith(token[..11], "the arithmetic: the capture ends 11 characters into the token, after the whole phrase");

        var seen = await ThroughTheProduct(stderr, truncated);

        seen.Diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent, "the row is named from the kept head");
        seen.Diagnosis.Line.Should().BeEmpty("the line the boundary broke is never quoted");
        NothingOf(token, 5, seen);
        ReadsAsASpentQuota(seen.Round);
        RateLimit.Hopeless(((ReviewerOutcome.RateLimited)seen.Round.Outcome).Reason).Should().BeTrue("with no line to quote the table's phrases stand for the name");
    }

    [Fact]
    public void ARealTruncationWhoseCutLineEndsInACrLf_QuotesNothingOfTheCutLine()
    {
        // Truncation is the capture's flag, not a property of the text: whatever the text ends in, a truncated capture's last line is incomplete.
        var stderr = "noise\r\nx " + FromThePhrase + " Bearer Lp5gU";

        var diagnosis = AcpDiagnosis.Of(NamesNoRow, stderr, KimiAcpProfile.Failures, stderrTruncated: true);

        diagnosis.Line.Should().NotContain("Lp5gU");
        diagnosis.Line.Should().BeEmpty();
        diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent, "the row is read from the whole kept head");
    }

    [Fact]
    public void ATruncatedCaptureWithNoLineBreakAtAll_IsNeverQuoted_ByDesign_ButStillNamesItsRow()
    {
        // One unbroken truncated line has no complete line in it: nothing of it is quoted.
        var diagnosis = AcpDiagnosis.Of(NamesNoRow, FromThePhrase, KimiAcpProfile.Failures, stderrTruncated: true);

        diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent);
        diagnosis.Line.Should().BeEmpty();
    }

    [Fact]
    public void AVendorLastLineThatLooksLikeOurTruncationSentence_IsStillVendorText()
    {
        // No truncation happened: a vendor line that merely begins like BoundedText's sentence, and carries the monthly phrase, must still name its row.
        var stderr = "noise\n[coai: output truncated " + FromThePhrase + "\n";

        var diagnosis = AcpDiagnosis.Of(NamesNoRow, stderr, KimiAcpProfile.Failures, stderrTruncated: false);

        diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent);
        diagnosis.Line.Should().Contain(Phrase);
    }

    [Fact]
    public void AnUntruncatedStderr_KeepsItsLastLine_EvenWithoutATrailingNewline() =>
        AcpDiagnosis.Of(NamesNoRow, "noise\n" + KimiMonthlyQuotaTests.MonthlyLine, KimiAcpProfile.Failures).Line.Should().Be(KimiMonthlyQuotaTests.MonthlyLine);

    [Fact]
    public void TheCapturesKeptText_NeverHoldsTheSentenceThatDisplayAdds()
    {
        var kept = new BoundedText(10);
        kept.Append("0123456789abc".ToCharArray(), 13);

        kept.Truncated.Should().BeTrue();
        kept.Kept.Should().Be("0123456789");
        kept.ToString().Should().Contain("output truncated", "the display text carries the sentence; the kept text does not");
    }

    // ---------- the redactor is not asked about a text that cannot hold the phrase ----------

    [Fact]
    public void ATextWithoutThePhrase_NeverReachesTheRedactor_AndATextWithItReachesItOnce()
    {
        // RedactionNeverTouchesAnyPhraseOfTheTable: redaction neither creates nor destroys a table phrase on its own, so a text without it is not worth a pass.
        var calls = 0;
        string Counting(string text)
        {
            calls++;

            return text;
        }

        AcpDiagnosis.LineWith("only noise here", Phrase, Counting).Should().BeEmpty();
        calls.Should().Be(0);

        AcpDiagnosis.LineWith("noise\n" + FromThePhrase, Phrase, Counting).Should().Contain(Phrase);
        calls.Should().Be(1);
    }

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

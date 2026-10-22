using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A spent MONTHLY Kimi quota is a spent quota too (story 5 of <c>todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md</c>, D8–D10): the table row, the
/// observed phrase and the reset clause's second opening. The line is the release session's own capture on Kimi 2.3.0 in PROMPT mode (three of three
/// launches, 2026-10-21); the ACP envelope around it is the plan's hypothesis, still unmeasured.
/// </summary>
public sealed class KimiMonthlyQuotaTests
{
    /// <summary>The measured line, verbatim — the URL is Kimi's public membership page, no account data.</summary>
    internal const string MonthlyLine =
        "provider.api_error: 429 You've reached your monthly usage limit for this billing cycle. Your quota will be refreshed in the next cycle. "
        + "To continue now, purchase extra usage or upgrade your plan: https://www.kimi.com/membership/subscription?tab=quota";

    private static string Envelope(string inner) => $"the agent answered with an error (-32000): {inner}";

    // ---------- S10 ----------

    [Fact]
    public void TheMonthlyLine_BehindTheSignInPrefix_IsQuotaSpent()
    {
        var diagnosis = AcpDiagnosis.Of(Envelope("Authentication required: " + MonthlyLine), KimiAcpProfile.Failures);

        diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent, "the monthly sentence is a spent quota, not the neutral provider error");
        diagnosis.Kind.Should().Be(AcpFailureKind.Spent);
        diagnosis.Cure.Should().Be(KimiQuotaWords.Fixed);
    }

    [Theory]
    [InlineData("Authentication required: 429 Too Many Requests", "provider_error")]
    [InlineData("Authentication required: 503 Service Unavailable", "provider_error")]
    [InlineData("Authentication required: 401 token expired", "not_signed_in")]
    [InlineData("Authentication required", "not_signed_in")]
    [InlineData("provider.api_error: 429 You've reached your 5-hour usage limit. Your quota will reset at 14:00 UTC.", "kimi_quota_spent")]
    [InlineData("provider.api_error: 429 Your account is suspended due to insufficient balance", "balance_spent")]
    public void TheGuards_StayWhereTheyWere(string inner, string name) =>
        AcpDiagnosis.Of(Envelope(inner), KimiAcpProfile.Failures).Name.Should().Be(name);

    [Fact]
    public void TheMonthlyRow_StandsBesideTheFiveHourRow_AndBeforeEverySignInRow()
    {
        var phrases = KimiAcpProfile.Failures.Select(failure => failure.Phrase).ToList();

        phrases.IndexOf("reached your monthly usage limit").Should().Be(phrases.IndexOf("reached your 5-hour usage limit") + 1);
        phrases.IndexOf("reached your monthly usage limit").Should().BeLessThan(phrases.IndexOf("Authentication required: 401"));
        KimiAcpProfile.SpentNames.Should().BeEquivalentTo(["kimi_quota_spent", "balance_spent"]);
    }

    // ---------- S11 ----------

    [Fact]
    public void TheMonthlyLine_IsHopeless_WithoutAnyAppendedPhrase() =>
        RateLimit.Hopeless(MonthlyLine).Should().BeTrue("the vendor's own words say the monthly allowance is spent");

    // ---------- S12 ----------

    [Fact]
    public void TheMonthlyClause_IsTheRefreshedSentence_AndStopsBeforeTheSalesLine()
    {
        var clause = KimiQuotaWords.ClauseOf(MonthlyLine);

        clause.Should().Be("Your quota will be refreshed in the next cycle.");
        clause.Should().NotContain("purchase").And.NotContain("https://");
    }

    [Theory]
    [InlineData("Your quota will reset at 14:00 UTC. Your quota will be refreshed in the next cycle.", "Your quota will reset at 14:00 UTC.")]
    [InlineData("Your quota will be refreshed in the next cycle. Your quota will reset at 14:00 UTC.", "Your quota will be refreshed in the next cycle.")]
    public void WhenBothOpeningsAppear_TheEarliestIsTheClause(string reason, string clause) =>
        KimiQuotaWords.ClauseOf(reason).Should().Be(clause);

    [Fact]
    public void TheSentence_QuotesTheRefreshedClause_AndNeverSaysNoReset() =>
        KimiQuotaWords.Said("kimi_quota_spent: " + MonthlyLine).Should().Contain("\"Your quota will be refreshed in the next cycle.\"").And.NotContain(KimiQuotaWords.NoReset);

    // ---------- F1: the matched line, when the sentence reaches the shim on stderr only ----------

    private static KimiRuntime Kimi() => new("kimi", HostKind.Windows, new KimiSeams(
        root => new KimiPlaces(root, "persons", "persons-bin", [], string.Empty, "kimi.exe"), () => ("coai-mcp.exe", [])));

    private static ReviewerInvocation Built() =>
        new("kimi", RoleCatalog.ArchitectureRole, new ProcessRequest("coai-mcp.exe", ["--ask-acp"], Path.GetTempPath()) { Timeout = TimeSpan.FromSeconds(30) }, string.Empty, Kimi(), Model: "kimi-code/k3");

    private const string NamesNoRow = "the agent answered with an error (-32603): Internal error";

    private static AcpOutcome Outcome(string reason, string stderr) =>
        new(AcpEnd.AgentFailed, string.Empty, reason, string.Empty, "s", AcpUsage.Unknown, [], [], [], stderr, 0);

    [Fact]
    public void AMonthlyQuotaSeenOnlyOnStderr_IsQuotedFromTheMatchedLine_NeverNamedAsTheFiveHourWindow()
    {
        var ended = KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, NamesNoRow, failure: KimiAcpProfile.QuotaSpent, failureLine: MonthlyLine);

        var verdict = Kimi().RateLimited(Built(), ended);

        verdict.Limited.Should().BeTrue();
        verdict.Reason.Should().NotContain("5-hour", "the vendor said monthly; the 5-hour row's phrase must never stand for it");
        verdict.Reason.Should().Contain("Your quota will be refreshed in the next cycle");
        RateLimit.Hopeless(verdict.Reason).Should().BeTrue(verdict.Reason);
        Kimi().WhyItFailed(Built(), ended).Should().Contain("\"Your quota will be refreshed in the next cycle.\"").And.NotContain(KimiQuotaWords.NoReset);
    }

    [Fact]
    public void AShimReportWithNoFailureLine_StillStopsTheLadder_WithoutNamingOneWindow()
    {
        var verdict = Kimi().RateLimited(Built(), KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, NamesNoRow, failure: KimiAcpProfile.QuotaSpent));

        RateLimit.Hopeless(verdict.Reason).Should().BeTrue("an older shim's report still stops the ladder: " + verdict.Reason);
        verdict.Reason.Should().Contain("5-hour").And.Contain("monthly", "the table's phrases for the name, neither passed off as the one the vendor said");
    }

    [Fact]
    public void TheShim_ReportsTheStderrLineItMatched_AndTheParentReadsItBack()
    {
        var outcome = Outcome(NamesNoRow, "some noise\n" + MonthlyLine + "\nmore noise");
        var diagnosis = AskAcpMode.Diagnose(new KimiAcpProfile(), outcome);

        diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent);
        var json = AskAcpMode.Report(outcome, diagnosis);
        JsonDocument.Parse(json).RootElement.GetProperty("failureLine").GetString().Should().Be(MonthlyLine);
        KimiReport.Parse(json).FailureLine.Should().Be(MonthlyLine);
    }

    [Fact]
    public void AMatchInTheReasonItself_WritesNoFailureLine_AndAnOlderReportReadsAsEmpty()
    {
        var outcome = Outcome("Authentication required: " + MonthlyLine, "noise");
        var json = AskAcpMode.Report(outcome, AskAcpMode.Diagnose(new KimiAcpProfile(), outcome));

        JsonDocument.Parse(json).RootElement.TryGetProperty("failureLine", out _).Should().BeFalse("the reason already carries the sentence");
        KimiReport.Parse("""{"end":"agent_failed","failure":"kimi_quota_spent","reason":"x"}""").FailureLine.Should().BeEmpty();
    }

    [Fact]
    public void ALongMatchedLine_IsCappedAtAWindowThatStillHoldsThePhrase()
    {
        var long_ = new string('x', 3000) + " " + MonthlyLine + " " + new string('y', 3000);
        var diagnosis = AcpDiagnosis.Of(NamesNoRow, long_, KimiAcpProfile.Failures);

        diagnosis.Line.Length.Should().BeLessThanOrEqualTo(AcpDiagnosis.LineCap);
        diagnosis.Line.Should().Contain("reached your monthly usage limit");
    }

    // ---------- code gate: G1 (redacted), G2 (cut at its own line end), G3 (every Spent phrase is hopeless) ----------

    private const string Phrase = "reached your monthly usage limit";

    [Fact]
    public void TheMatchedLine_IsRedacted_AndKeepsThePhrase()
    {
        var stderr = "Authorization: Bearer abcdefghijkl " + MonthlyLine + " token sk-abcdefghijklmnop";

        var line = AcpDiagnosis.Of(NamesNoRow, stderr, KimiAcpProfile.Failures).Line;

        line.Should().NotContain("abcdefghijkl").And.NotContain("sk-abcdefghijklmnop", "the quoted line goes to the summary, the ledger and the log: a secret in stderr is masked first");
        line.Should().Contain(Phrase).And.Contain("Your quota will be refreshed in the next cycle");
    }

    [Theory]
    [InlineData("progress 10%\rprogress 20%\r{0}\r more")]
    [InlineData("progress 10%\r\nprogress 20%\r\n{0}\r\n more")]
    [InlineData("progress 10%\nprogress 20%\n{0}\n more")]
    public void TheMatchedLine_IsOnlyItsOwnLine_NoMatterHowLinesAreSeparated(string layout)
    {
        var line = AcpDiagnosis.Of(NamesNoRow, string.Format(layout, MonthlyLine), KimiAcpProfile.Failures).Line;

        line.Should().Be(MonthlyLine);
        line.Should().NotContain("\r").And.NotContain("\n").And.NotContain("progress");
    }

    [Fact]
    public void AWindowEdge_NeverSplitsASurrogatePair_AtEitherEnd()
    {
        // The window starts 60 characters before the phrase and holds 400: put a pair across each of those two edges.
        const string pair = "\U0001F600";
        var beforeThePhrase = new string('x', 100) + pair + new string('x', 59);
        var phraseEnd = beforeThePhrase.Length + Phrase.Length;
        var line = beforeThePhrase + Phrase + new string('y', 500 - phraseEnd) + pair + "zzz";

        var kept = AcpDiagnosis.Of(NamesNoRow, line, KimiAcpProfile.Failures).Line;

        kept.Length.Should().BeLessThanOrEqualTo(AcpDiagnosis.LineCap);
        kept.Should().Contain(Phrase);
        for (var i = 0; i < kept.Length; i++)
        {
            (char.IsHighSurrogate(kept[i]) == (i + 1 < kept.Length && char.IsLowSurrogate(kept[i + 1]))).Should().BeTrue($"index {i} is half of a pair");
            (!char.IsLowSurrogate(kept[i]) || (i > 0 && char.IsHighSurrogate(kept[i - 1]))).Should().BeTrue($"index {i} is a lone low surrogate");
        }
    }

    [Fact]
    public void EverySpentPhraseOfTheTable_IsHopeless_ForTheRateLimitCheck()
    {
        // Derived from the table, not a hand list: a Spent row whose phrase RateLimit.Spent lacks would be retried on the ladder when the sentence rides in a reason.
        var missing = KimiAcpProfile.Failures.Where(failure => failure.Kind == AcpFailureKind.Spent).Select(failure => failure.Phrase).Where(phrase => !RateLimit.Hopeless(phrase)).ToList();

        missing.Should().BeEmpty("each Spent phrase of KimiAcpProfile.Failures must be in RateLimit.Spent: " + string.Join(", ", missing));
    }

    // ---------- feature gate round 1, F1: the WHOLE line is redacted, then the window is cut from the redacted text ----------

    private const string Label = " Authorization: Bearer ";

    /// <summary>A bare Bearer label: no "Authorization:" word in front, so redaction cannot GROW the window and let its own cap trim the fragment away.</summary>
    private const string EndLabel = " Bearer ";

    /// <summary>The measured sentence from its phrase on, so the phrase is the first thing after a test line's own prefix.</summary>
    private static readonly string FromThePhrase = MonthlyLine[MonthlyLine.IndexOf(Phrase, StringComparison.Ordinal)..];

    /// <summary>An opaque token of the given length: letters and digits only, no vendor prefix, nothing but the Bearer label makes Redaction recognise it.</summary>
    private static string Opaque(int length) => string.Concat(Enumerable.Range(0, length).Select(i => "Zq8Lm3XvK7pRt2WbN5HcY9dFg4Js6AeU1oIx0Q"[(i * 7 + 3) % 38]));

    [Fact]
    public void ACredentialWhoseLabelTheOldWindowWouldCut_IsRedactedBeforeTheWindow_AndTheTokenNeverSurvives()
    {
        // The OLD order cut the window first: it starts WindowLeadIn characters before the phrase. A token of WindowLeadIn - 1 characters, one space, then the phrase
        // puts the window's first character exactly ON the token's first character, so the label (Authorization: Bearer ) is cut away and the bare token reached SafeText.
        var token = Opaque(AcpDiagnosis.WindowLeadIn - 1);
        var head = new string('p', 500) + Label;
        var line = head + token + " " + FromThePhrase;
        var oldWindowStart = line.IndexOf(Phrase, StringComparison.OrdinalIgnoreCase) - AcpDiagnosis.WindowLeadIn;

        oldWindowStart.Should().Be(head.Length, "the arithmetic that gives this test its teeth: the old window starts at the token's first character, so a window-then-redact order leaks it");
        line.Length.Should().BeGreaterThan(AcpDiagnosis.LineCap, "a line that fits is not windowed at all");

        var kept = AcpDiagnosis.Of(NamesNoRow, line, KimiAcpProfile.Failures).Line;

        kept.Should().NotContain(token).And.NotContain(token[..8], "the token's first characters are the leak the old order let through");
        kept.Should().Contain(Phrase).And.Contain("Your quota will be refreshed in the next cycle");
        kept.Length.Should().BeLessThanOrEqualTo(AcpDiagnosis.LineCap);
    }

    [Fact]
    public void ACredentialCrossingTheWindowsEnd_IsRedactedBeforeTheWindow_AndNoFragmentOfItSurvives()
    {
        // The window ends LineCap characters after it starts. Put the token's first 5 characters INSIDE the old window with the the Bearer label whole before them: Bearer wants
        // 8 characters, so the old order quoted the 5-character fragment unredacted.
        const int inside = 5;
        var token = Opaque(59);
        var head = new string('p', 100);
        var windowEnd = head.Length - AcpDiagnosis.WindowLeadIn + AcpDiagnosis.LineCap;
        var beforeLabel = windowEnd - inside - EndLabel.Length - (head.Length + Phrase.Length);
        var line = head + Phrase + new string('f', beforeLabel) + EndLabel + token;
        var oldWindow = line[(head.Length - AcpDiagnosis.WindowLeadIn)..windowEnd];

        oldWindow.Should().EndWith(token[..inside], "the arithmetic that gives this test its teeth: the old window ends after the token's first characters, label intact, too short for Bearer");

        var kept = AcpDiagnosis.Of(NamesNoRow, line, KimiAcpProfile.Failures).Line;

        kept.Should().NotContain(token[..inside]).And.Contain(Phrase);
        kept.Should().EndWith("Bearer [reda", "the window now cuts the REDACTED text: it ends inside the marker that replaced the token, never inside the token");
        kept.Length.Should().BeLessThanOrEqualTo(AcpDiagnosis.LineCap, kept);
    }

    [Fact]
    public void RedactionNeverTouchesAnyPhraseOfTheTable()
    {
        // The window is found again in the REDACTED text: that is only sound while the redactor leaves every phrase the profile matches alone.
        var touched = KimiAcpProfile.Failures.Select(failure => failure.Phrase).Where(phrase => !Redaction.SafeText(phrase, int.MaxValue).Equals(phrase, StringComparison.Ordinal)).ToList();

        touched.Should().BeEmpty("a phrase the redactor rewrites could never be found in the redacted line: " + string.Join(", ", touched));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void AnInteriorTab_NeverReachesTheQuotedLine_ItIsOneLineOfPrintableText(int padding)
    {
        // SafeSource keeps tab, LF and CR (the layout of a SOURCE, so the whole-text pass sees a block whole); the quoted line is ONE line of a notice, and it must end the way
        // SafeText makes a notice: no control character at all. LF and CR are taken by the isolation; a tab inside the line is not (CodeRabbit on PR #917). 0 = the line whole, 500 = windowed.
        var line = new string('p', padding) + " worker\tfailed:\t" + MonthlyLine;

        var kept = AcpDiagnosis.Of(NamesNoRow, line, KimiAcpProfile.Failures).Line;

        kept.Should().Contain(Phrase);
        kept.Where(char.IsControl).Should().BeEmpty("the quoted line is one line of printable text: " + kept);
        kept.Length.Should().BeLessThanOrEqualTo(AcpDiagnosis.LineCap);
    }

    [Fact]
    public void WhenRedactionFailsClosed_NothingIsQuoted_NeverTheRawLine()
    {
        // A redaction that timed out answers Redaction.Redacted (Redaction.WhenRedactionTimesOut) for the WHOLE text: the marker holds no phrase, so nothing is quoted — and never the raw text.
        var line = AcpDiagnosis.LineWith("noise Authorization: Bearer abcdefghijkl " + MonthlyLine, Phrase, _ => Redaction.WhenRedactionTimesOut(() => throw new System.Text.RegularExpressions.RegexMatchTimeoutException()));

        line.Should().BeEmpty("the phrase is not in the marker: nothing of the raw text may be quoted in its place");
    }

    // ---------- feature gate round 1, F2: the matched line is quoted BEFORE the hopeless check ----------

    [Fact]
    public void AnUnrelatedHopelessWordInTheReason_DoesNotDiscardTheMatchedLine()
    {
        // "daily" is one of RateLimit.Spent: the reason alone is "hopeless", so the old order returned it bare and the monthly sentence the shim matched was dropped.
        const string reason = "Internal error: daily worker failed";
        RateLimit.Hopeless(reason).Should().BeTrue("the premise of the case: the word daily is in RateLimit.Spent");
        var ended = KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, reason, failure: KimiAcpProfile.QuotaSpent, failureLine: MonthlyLine);

        var verdict = Kimi().RateLimited(Built(), ended);

        verdict.Limited.Should().BeTrue();
        verdict.Reason.Should().Contain(MonthlyLine[..80]).And.Contain("Your quota will be refreshed in the next cycle");
        RateLimit.Hopeless(verdict.Reason).Should().BeTrue(verdict.Reason);
    }

    [Fact]
    public void AReasonThatIsHopelessOnItsOwnAndHasNoMatchedLine_IsReturnedAsItIs()
    {
        var ended = KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, "Internal error: daily worker failed", failure: KimiAcpProfile.QuotaSpent);

        Kimi().RateLimited(Built(), ended).Reason.Should().Be("kimi_quota_spent: Internal error: daily worker failed", "no line to quote and the reason already stops the ladder: no phrases appended");
    }

    // ---------- F2: K4 precedence, pinned ----------

    [Theory]
    [InlineData("Authentication required", "not_signed_in")]
    [InlineData("Authentication required: 429 Too Many Requests", "provider_error")]
    public void AReasonThatNamesARow_WinsOverAMonthlySentenceInStderr(string reason, string name)
    {
        var diagnosis = AcpDiagnosis.Of(reason, MonthlyLine, KimiAcpProfile.Failures);

        diagnosis.Name.Should().Be(name, "K4: the agent's reason names a row, so stderr is not asked");
        diagnosis.Line.Should().BeEmpty();
    }
}

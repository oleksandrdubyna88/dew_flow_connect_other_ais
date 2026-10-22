using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Core.Findings;
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

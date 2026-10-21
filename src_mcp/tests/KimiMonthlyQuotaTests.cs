using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
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
        var names = KimiAcpProfile.Failures.Select(failure => failure.Phrase).ToList();

        names.IndexOf("reached your monthly usage limit").Should().Be(names.IndexOf("reached your 5-hour usage limit") + 1);
        names.IndexOf("reached your monthly usage limit").Should().BeLessThan(names.IndexOf("Authentication required: 401"));
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
}

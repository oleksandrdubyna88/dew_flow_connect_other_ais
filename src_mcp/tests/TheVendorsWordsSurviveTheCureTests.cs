using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A diagnosed failure keeps the vendor's own sentence AFTER the cure — redacted first, cut second.
/// </summary>
/// <remarks>
/// <para>Story 2 of todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md (D4; S6, S6b). <c>Because</c> used to return the cure and
/// nothing else, so the ledger and the log lost the one line the capture step needs: what the vendor actually said.</para>
/// <para>Every id below is made up. No real account data is in this file.</para>
/// </remarks>
public sealed class TheVendorsWordsSurviveTheCureTests
{
    private const string Separator = " — the vendor said: ";

    private const string FakeOrg = "org-0a1b2c3d4e5f6a7b";

    private const string FakeKey = "ak-9z8y7x6w5v4u3t2s";

    private const string BalanceLine =
        "provider.api_error: 429 Your account " + FakeOrg + " <" + FakeKey + "> is suspended due to insufficient balance, "
        + "please recharge your account or check your plan and billing details";

    [Fact]
    public void ASpentBalance_KeepsTheVendorsSentence_AfterTheCure()
    {
        var said = ReviewerSummaryFactory.DescribeWhole(new ReviewerOutcome.RateLimited("balance_spent: " + BalanceLine));

        said.Should().StartWith("rate limited (after 1 attempt): the Kimi allowance is spent (D14)")
            .And.Contain("is suspended due to insufficient balance, please recharge your account");
    }

    [Fact]
    public void AShimLine_IsKeptWithoutItsTag()
    {
        var said = ReviewerSummaryFactory.DescribeWhole(new ReviewerOutcome.NonZeroExit(77,
            "[coai-mcp] not_signed_in: the agent answered with an error (-32000): Authentication required — run `kimi login` in a terminal and try again"));

        said.Should().Contain("Authentication required").And.NotContain("[coai-mcp]");
    }

    [Fact]
    public void OnlyTheLineThatCarriesTheMarker_IsKept()
    {
        const string stderr = """
            Code Assist for individuals. To continue using Gemini, please migrate to the Antigravity suite of products: https://antigravity.google
                at throwIneligibleOrProjectIdError (file:///C:/Users/x/gemini-cli/bundle/chunk.js:310101:11)
                at _doSetupUser (file:///C:/Users/x/gemini-cli/bundle/chunk.js:310090:5)
            Node.js v20.20.2
            """;

        var said = ReviewerSummaryFactory.DescribeWhole(new ReviewerOutcome.NonZeroExit(1, stderr));

        said.Should().Contain(Separator + "Code Assist for individuals.")
            .And.Contain("migrate to the Antigravity suite")
            .And.NotContain("_doSetupUser")
            .And.NotContain("Node.js v");
    }

    [Fact]
    public void TheSummaryCutsTheVendorsPart_AfterTheCure()
    {
        var line = ShimNotes.Prefix + "balance_spent: " + new string('w', 600);
        var cure = VendorDiagnosis.For(line)!;

        var said = ReviewerSummaryFactory.Describe(new ReviewerOutcome.RateLimited(line));

        said.Should().Contain(cure, "the whole cure is kept").And.Contain(Separator);
        var vendorPart = said[(said.IndexOf(Separator, StringComparison.Ordinal) + Separator.Length)..];
        vendorPart.Length.Should().BeLessThanOrEqualTo(160 + 1, "160 characters and the ellipsis");
        vendorPart.Should().EndWith("…");
    }

    [Fact]
    public void AWholeVendorSentence_IsBoundedByTheSafetyCap()
    {
        var line = "balance_spent: " + new string('w', 9_000);

        var said = ReviewerSummaryFactory.DescribeWhole(new ReviewerOutcome.RateLimited(line));

        said.Should().Contain(Separator).And.EndWith(Redaction.Truncated);
        said.Length.Should().BeLessThan(4_100);
    }

    [Fact]
    public void TheVendorsSentence_NeverCarriesAMoonshotId()
    {
        var outcome = new ReviewerOutcome.RateLimited("balance_spent: " + BalanceLine);

        foreach (var said in new[] { ReviewerSummaryFactory.Describe(outcome), ReviewerSummaryFactory.DescribeWhole(outcome) })
        {
            said.Should().Contain("is suspended due to").And.NotContain("0a1b2c3d4e5f6a7b").And.NotContain("9z8y7x6w5v4u3t2s");
        }
    }

    [Fact]
    public void AnIdAtTheCut_IsRedactedBeforeTheCut_NotLeftAsItsHead()
    {
        // The id starts a few characters before the summary's 160-character cut: cut first and its head
        // (`org-0a1b2`, too short for the pattern's eight-character body once cut) would survive.
        var line = "balance_spent: " + new string('w', 135) + " " + FakeOrg + " is suspended";

        var said = ReviewerSummaryFactory.Describe(new ReviewerOutcome.RateLimited(line));

        said.Should().NotContain("org-0", "the id is redacted whole, and only then is the line cut");
    }
}

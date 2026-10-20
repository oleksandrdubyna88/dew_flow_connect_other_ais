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
/// A spent Kimi plan quota is not a lost sign-in (story 1 of <c>todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md</c>): Kimi wraps its 429 quota answer
/// in the same <c>Authentication required:</c> prefix as a 401, and the first matching row of the profile's table used to file it as <c>not_signed_in</c> —
/// the person was told to run <c>kimi login</c> for a quota that no login clears. The table, the shim's exit, the one sentence and the reviewer's verdict.
/// </summary>
/// <remarks>
/// The envelope is CONSTRUCTED: <c>AcpClient</c> wraps a JSON-RPC error as <c>the agent answered with an error ({code}): {message}</c>, and the one measured
/// provider error carries the prefix <c>Authentication required: 401 …</c> (<c>research/RESULTS_kimi_confinement.md</c>). The quota line is the owner's own
/// kimi 2.2.0 CLI, 2026-10-19; its ellipsis is the owner's — the rest of the line was not copied.
/// </remarks>
public sealed class KimiQuotaIsNotASignInTests
{
    private const string OwnersQuotaLine = "You've reached your 5-hour usage limit. Your quota will reset …";

    private const string Inner = "Authentication required: 429 " + OwnersQuotaLine;

    private static string Envelope(string inner) => $"the agent answered with an error (-32000): {inner}";

    private static IReadOnlyList<AcpKnownFailure> Table => KimiAcpProfile.Failures;

    private static KimiRuntime Kimi() => new("kimi", HostKind.Windows, new KimiSeams(
        root => new KimiPlaces(root, "persons", "persons-bin", [], string.Empty, "kimi.exe"), () => ("coai-mcp.exe", [])));

    private static ReviewerInvocation Built() =>
        new("kimi", RoleCatalog.ArchitectureRole, new ProcessRequest("coai-mcp.exe", ["--ask-acp"], Path.GetTempPath()) { Timeout = TimeSpan.FromSeconds(30) }, string.Empty, Kimi(), Model: "kimi-code/k3");

    // ---------- S1 ----------

    [Fact]
    public void TheOwnersQuotaLine_BehindTheSignInPrefix_IsQuotaSpent()
    {
        var diagnosis = AcpDiagnosis.Of(Envelope(Inner), Table);

        diagnosis.Name.Should().Be(KimiAcpProfile.QuotaSpent, "the quota sentence comes before the sign-in prefix it hides behind");
        diagnosis.Kind.Should().Be(AcpFailureKind.Spent);
        diagnosis.Cure.Should().Be(KimiQuotaWords.Fixed);
    }

    [Fact]
    public void TheSixRowsStandInTheDecidedOrder_AndSpentNamesAreDerived()
    {
        Table.Select(failure => failure.Name).Should().Equal(
            "subscription_model", "kimi_quota_spent", "balance_spent", "not_signed_in", "provider_error", "not_signed_in");
        Table.Select(failure => failure.Phrase).Should().Equal(
            "Your current subscription does not have access to",
            "reached your 5-hour usage limit",
            "insufficient balance",
            "Authentication required: 401",
            "Authentication required:",
            "Authentication required");
        KimiAcpProfile.SpentNames.Should().BeEquivalentTo(Table.Where(failure => failure.Kind == AcpFailureKind.Spent).Select(failure => failure.Name));
        KimiAcpProfile.SpentNames.Should().BeEquivalentTo(["kimi_quota_spent", "balance_spent"]);
        Table[4].Cure.Should().BeEmpty("a provider error has no cure to give");
        Table[4].Kind.Should().Be(AcpFailureKind.Failed);
    }

    // ---------- S2 ----------

    [Theory]
    [InlineData("Authentication required", "not_signed_in", AcpFailureKind.Refused, true)]
    [InlineData("Authentication required: 401 Your current subscription does not have access to kimi-for-coding-highspeed. Upgrade to higher-tier Kimi Code plans.", "subscription_model", AcpFailureKind.Refused, false)]
    [InlineData("Authentication required: 401 token expired", "not_signed_in", AcpFailureKind.Refused, true)]
    [InlineData("Authentication required: 503 Service Unavailable", "provider_error", AcpFailureKind.Failed, false)]
    [InlineData("Authentication required: 429 Too Many Requests", "provider_error", AcpFailureKind.Failed, false)]
    [InlineData(
        "Authentication required: 429 Your account org-REDACTED <ak-REDACTED> is suspended due to insufficient balance, please recharge your account or check your plan and billing details",
        "balance_spent", AcpFailureKind.Spent, false)]
    public void TheGuards_EveryOtherSignInShapeKeepsItsOwnRow(string inner, string name, AcpFailureKind kind, bool loginCure)
    {
        var diagnosis = AcpDiagnosis.Of(Envelope(inner), Table);

        diagnosis.Name.Should().Be(name);
        diagnosis.Kind.Should().Be(kind);
        diagnosis.Cure.Contains("kimi login", StringComparison.Ordinal).Should().Be(loginCure);
        if (kind == AcpFailureKind.Failed)
        {
            diagnosis.Cure.Should().BeEmpty("a provider error carries no cure");
        }
    }

    [Fact]
    public void AnUnrelatedUsageLimitText_MatchesNoRow()
    {
        AcpDiagnosis.Of("You've hit your usage limit. Upgrade to Pro to purchase", Table).Should().Be(AcpDiagnosis.None);
        AcpDiagnosis.Of("rate limit reached; upgrade to the paid plan", Table).Should().Be(AcpDiagnosis.None);
    }

    [Fact]
    public void AnotherVendorsText_NeverReachesKimisTable()
    {
        var elsewhere = new ProcessResult(1, "not a report", OwnersQuotaLine, false);

        Kimi().RateLimited(Built(), elsewhere).Should().Be(RateLimitVerdict.No, "classification is from the report's field, never from text");
        Kimi().RateLimited(Built(), KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, Inner, failure: string.Empty))
            .Should().Be(RateLimitVerdict.No, "exit 75 with no name is no verdict");
    }

    // ---------- S3 ----------

    [Fact]
    public void TheQuotaMarker_BecomesTheQuotaSentence_WhichNeverSaysKimiLogin()
    {
        var diagnosis = VendorDiagnosis.Classify($"kimi_quota_spent: {Inner}", wsl: false);

        diagnosis.Should().NotBeNull();
        diagnosis!.Kind.Should().Be(DiagnosisKind.Refused);
        diagnosis.Cure.Should().Be(KimiQuotaWords.Fixed);
        diagnosis.Cure.Should().NotContain("kimi login").And.NotContain("waiting is the only cure");
    }

    // ---------- S3b ----------

    [Fact]
    public void WhyItFailed_OnAQuota_QuotesTheResetClause_BetweenTheCureAndTheShimsWords()
    {
        const string withReset = "Authentication required: 429 You've reached your 5-hour usage limit. Your quota will reset at 14:00 UTC.";
        var ended = KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, withReset, failure: KimiAcpProfile.QuotaSpent);

        Kimi().WhyItFailed(Built(), ended).Should().Be(
            "the Kimi plan quota is used up — \"Your quota will reset at 14:00 UTC.\"; signing in again will not help "
            + $"(the shim said: kimi_quota_spent: {withReset})");

        var silent = KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, "Authentication required: 429 You've reached your 5-hour usage limit.", failure: KimiAcpProfile.QuotaSpent);

        Kimi().WhyItFailed(Built(), silent).Should().Contain("the Kimi plan quota is used up — the vendor named no reset; signing in again will not help");
    }

    [Fact]
    public void TheClause_IsCutAtTheFirstPeriodAndSpace_NeverAtADecimalPoint()
    {
        KimiQuotaWords.ClauseOf("You've reached your 5-hour usage limit. Your quota will reset in 2.5 hours. Please wait.")
            .Should().Be("Your quota will reset in 2.5 hours.");
        KimiQuotaWords.ClauseOf("no such sentence").Should().BeEmpty();
        KimiQuotaWords.ClauseOf(OwnersQuotaLine).Should().Be("Your quota will reset …", "no period: the clause runs to the end, the owner's ellipsis whole");
    }

    // ---------- S4 ----------

    [Fact]
    public void AShimExit75NamedQuotaSpent_IsALimitedVerdict_Hopeless_WithoutABorrowedPhrase()
    {
        var ended = KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, Inner, failure: KimiAcpProfile.QuotaSpent);

        var verdict = Kimi().RateLimited(Built(), ended);

        verdict.Limited.Should().BeTrue("exit 75 and a Spent name is a limit");
        RateLimit.Hopeless(verdict.Reason).Should().BeTrue("the vendor's own words ride in the reason: " + verdict.Reason);
        verdict.Reason.Should().NotContain("(insufficient balance)").And.NotContain("kimi login").And.StartWith("kimi_quota_spent: ").And.Contain(KimiQuotaWords.ResetOpening);
        verdict.Reason.Should().NotContain(KimiQuotaWords.Opening, "the reason is the vendor's words; whoever writes the line puts the cure in front (S7), so it is never said twice");
        verdict.Usage.Should().Be(Usage.Unknown);
    }

    [Fact]
    public void AnExit75WithAnotherName_OrAnotherExitWithTheName_IsNoLimit()
    {
        Kimi().RateLimited(Built(), KimiShimScripts.Ended(AcpWords.SpentExit, AcpEnd.AgentFailed, Inner, failure: KimiAcpProfile.ProviderError))
            .Limited.Should().BeFalse("exit 75 with a name that is not Spent");
        Kimi().RateLimited(Built(), KimiShimScripts.Ended(70, AcpEnd.AgentFailed, Inner, failure: KimiAcpProfile.QuotaSpent))
            .Limited.Should().BeFalse("a Spent name on another exit");
        Kimi().RateLimited(Built(), KimiShimScripts.Ended(77, AcpEnd.AgentFailed, "Authentication required", failure: "not_signed_in"))
            .Limited.Should().BeFalse("a sign-in refusal is not a limit");
    }

    [Fact]
    public void ASpentBalance_KeepsItsOwnWords()
    {
        var ended = KimiShimScripts.Ended(
            AcpWords.SpentExit, AcpEnd.AgentFailed, "provider.api_error: 429 Your account is suspended due to insufficient balance", failure: KimiAcpProfile.BalanceSpent);

        var verdict = Kimi().RateLimited(Built(), ended);

        verdict.Limited.Should().BeTrue();
        RateLimit.Hopeless(verdict.Reason).Should().BeTrue(verdict.Reason);
        verdict.Reason.Should().StartWith("balance_spent: ").And.Contain("insufficient balance")
            .And.NotContain("the Kimi allowance is spent (D14)", "whoever writes the line puts the cure in front, once").And.NotContain("quota is used up");
    }

    // ---------- S9 ----------

    [Fact]
    public void ABareSignInRefusal_StaysASignInRefusal_AndIsNeverWidened()
    {
        var diagnosis = AcpDiagnosis.Of(Envelope("Authentication required"), Table);

        diagnosis.Name.Should().Be("not_signed_in");
        diagnosis.Cure.Should().Contain("kimi login");
        VendorDiagnosis.Classify("not_signed_in: Authentication required — run `kimi login` in a terminal and try again", wsl: false)!.Cure
            .Should().Contain("kimi login").And.NotContain("quota");
        KimiAcpProfile.SpentNames.Should().NotContain("not_signed_in");
    }
}

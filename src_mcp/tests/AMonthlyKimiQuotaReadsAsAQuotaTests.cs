using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S13 of <c>todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md</c> (story 5) — ONE round with a Kimi reviewer whose MONTHLY quota is spent, from the vendor's
/// answer to the three lines a person reads. The same product road as <see cref="ASpentKimiQuotaReadsAsAQuotaTests"/>: the real shim, runtime, executor, scheduler and ledger.
/// </summary>
public sealed class AMonthlyKimiQuotaReadsAsAQuotaTests : IDisposable
{
    /// <summary>The agent's answer — the measured monthly line behind the sign-in prefix (the ACP envelope is the plan's hypothesis, still unmeasured).</summary>
    private const string PromptError = "Authentication required: " + KimiMonthlyQuotaTests.MonthlyLine;

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-monthly-round-");

    public void Dispose() => _dir.Dispose();

    private static void ReadsAsAQuota(string where, string line)
    {
        line.Should().Contain(KimiQuotaWords.Opening, $"the {where} says the quota is used up");
        line.Should().Contain("\"Your quota will be refreshed in the next cycle.\"", $"the {where} quotes the refreshed clause Kimi gave");
        line.Should().NotContain("kimi login", $"the {where} never sends a person to sign in again");
        line.Should().NotContain("exit 77", $"the {where} is not a sign-in refusal");
        line.Should().NotContain(KimiQuotaWords.NoReset, $"the {where} — Kimi did name when the quota comes back");
        line.Should().Contain(KimiQuotaWords.Opening, Exactly.Once(), $"the {where} says the cure once");
    }

    [Fact]
    public async Task AMonthlyQuota_ReadsAsAQuota_InTheSummary_TheLedger_AndTheLog_AfterOneLaunch()
    {
        var round = await new AKimiRound(_dir).RunAsync(PromptError);

        using var scope = new AssertionScope();
        ReadsAsAQuota("round summary", round.Summary);
        ReadsAsAQuota("ledger line", round.Ledger);
        ReadsAsAQuota("log line", round.Log);
        var limited = round.Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>("a spent monthly quota is a rate limit").Subject;
        limited.Attempts.Should().Be(1);
        RateLimit.Hopeless(limited.Reason).Should().BeTrue("the vendor's own words say the allowance is spent: " + limited.Reason);
        round.Shims.Should().Be(1, "a spent quota is never climbed");
    }
}

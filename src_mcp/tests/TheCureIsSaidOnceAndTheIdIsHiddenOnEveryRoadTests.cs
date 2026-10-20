using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The two follow-ups story 3's S7 found, closed on EVERY road a vendor's line takes into the round summary, the ledger and the log
/// (<c>todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md</c>, story 3, deviations 2 and 3): the cure is said exactly ONCE, and a vendor's line is
/// redacted BEFORE any cut, so no head of an account id can show.
/// </summary>
/// <remarks>Every id below is made up. No real account data is in this file.</remarks>
public sealed class TheCureIsSaidOnceAndTheIdIsHiddenOnEveryRoadTests : IDisposable
{
    private const string FakeOrg = "org-0a1b2c3d4e5f6a7b";

    private const string FakeKey = "ak-9z8y7x6w5v4u3t2s";

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-once-and-hidden-");

    public void Dispose() => _dir.Dispose();

    private static void NoIdFragmentIn(TheRound round)
    {
        foreach (var (where, line) in round.Lines)
        {
            line.Should().NotContain("org-0", $"the {where} must not show the head of an account id")
                .And.NotContain("ak-9").And.NotContain("0a1b2c3d").And.NotContain("9z8y7x6w");
        }
    }

    [Fact]
    public async Task ASignInRefusal_SaysItsCureOnce_InTheSummary_TheLedger_AndTheLog()
    {
        var round = await new AKimiRound(_dir).RunAsync("Authentication required");

        using var scope = new AssertionScope();
        round.Outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Which.ExitCode.Should().Be(77);
        foreach (var (where, line) in round.Lines)
        {
            line.Should().Contain("kimi login", $"the {where} still tells a person what to do");
            line.Should().Contain("kimi login", Exactly.Once(), $"the {where} says the cure once — the shim's own tail was quoted behind the table's cure");
            line.Should().Contain("Authentication required", $"the {where} keeps the vendor's own words");
        }
    }

    [Fact]
    public async Task AProviderErrorWithAnId_ShowsNoIdFragment_InTheSummary_TheLedger_AndTheLog()
    {
        var round = await new AKimiRound(_dir).RunAsync($"Authentication required: 503 Service Unavailable for {FakeOrg} <{FakeKey}>");

        using var scope = new AssertionScope();
        round.Outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>().Which.ExitCode.Should().Be(70);
        NoIdFragmentIn(round);
        round.Summary.Should().Contain("503 Service Unavailable", "redaction is not a cut");
    }

    [Fact]
    public async Task AnUndiagnosedVendorLine_IsRedactedBeforeTheCut_NotLeftAsAnIdHead()
    {
        // The id starts eight characters before the summary's 160-character cut: cut first and `org-0a1b` (too short for the pattern's
        // eight-character body once its prefix is counted out) would show, and no pattern would ever match it.
        var line = "Error: " + new string('w', 145) + $" {FakeOrg} <{FakeKey}> was refused";
        var round = await new AKimiRound(_dir).RunAsync(new ProcessResult(1, "not a report", line, TimedOut: false));

        using var scope = new AssertionScope();
        NoIdFragmentIn(round);
        round.Summary.Should().Contain("Error: www", "the vendor's words are still quoted");
    }
}

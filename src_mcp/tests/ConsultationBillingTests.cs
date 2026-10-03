using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The caller is told what THIS turn cost — the same share the record and the ledger get — never the
/// conversation's running total.
/// </summary>
/// <remarks>
/// Not a scenario test, and the reason is the defect's own: the one cumulative vendor (antigravity) reports
/// no money, so through any shipped adapter the reply's cost is null either way and the bug could not be
/// seen. The decision lives in <see cref="ConsultationBilling"/> so a test can hand it the case a priced
/// cumulative vendor would. Observed RED before the fix: <c>Expected … to be 0.05 …, but found 0.45</c>.
/// </remarks>
public sealed class ConsultationBillingTests
{
    private static ConsultationRecord SecondTurnOf(double turnOneCost) =>
        new("c0ffee00", "caller", "claude", "no-session", "/repo", "main", "0123abc", "vendor", "model", "antigravity", "vendorRemembers", 5, "2026-10-02T00:00:00Z")
        {
            Turns = [new ConsultationTurn("2026-10-02T00:00:01Z", "turn one", "advice", 1, 100, 10, turnOneCost)],
        };

    private static ConsultantTurnResult Reported(Usage usage) =>
        new([new ReviewerLaunch(null, "advice", usage, "advice")], [], "conv-1", usage);

    [Fact]
    public void ASecondTurnOfACumulativeVendor_ReportsOnlyItsOwnShare()
    {
        var cost = ConsultationBilling.ReplyCost(cumulative: true, SecondTurnOf(turnOneCost: 0.40), Reported(new Usage(250, 25, 0.45)));

        cost.Should().BeApproximately(0.05, 0.0001, "the vendor's 0.45 is the conversation's total, and 0.40 of it was turn one");
    }

    [Fact]
    public void APerTurnVendor_ReportsWhatItSaid()
    {
        ConsultationBilling.ReplyCost(cumulative: false, SecondTurnOf(turnOneCost: 0.40), Reported(new Usage(150, 15, 0.45)))
            .Should().Be(0.45);
    }

    [Fact]
    public void TheReplyAndTheRecord_AreBilledByOneRule()
    {
        var record = SecondTurnOf(turnOneCost: 0.40);
        var turned = Reported(new Usage(250, 25, 0.45));

        ConsultationBilling.ReplyCost(true, record, turned)
            .Should().Be(ConsultationBilling.ThisTurnsShare(true, record, turned.TurnUsage).CostUsd);
    }
}

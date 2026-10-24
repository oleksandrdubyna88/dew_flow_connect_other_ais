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

    /// <summary>
    /// The review's B1, its migration: a record written before <c>LastReported</c> existed measures its next report against <c>Billed</c> — what
    /// the old rule measured against, so that turn bills what it always would have, never its whole history — and from then on against the
    /// vendor's own counter, written with the record.
    /// </summary>
    [Fact]
    public void ARecordWrittenBeforeTheCounter_UsesWhatWasBilled_ThenTheVendorsOwnCounter()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(SecondTurnOf(turnOneCost: 0.40) with { Billed = new Core.Consultation.ConsultationBilled(100, 10, 0.40) }, ConsultationJsonContext.Default.ConsultationRecord);
        json.Should().Contain("\"lastReported\"", "the counter is a field of the record file");
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove("lastReported");
        var old = System.Text.Json.JsonSerializer.Deserialize(node.ToJsonString(), ConsultationJsonContext.Default.ConsultationRecord)!;

        var second = ConsultationBilling.ThisTurnsBill(cumulative: true, old, new Usage(250, 25, null), mayRestart: true);
        var billed = ConsultationBilling.Billing(old, second);
        var read = System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(billed, ConsultationJsonContext.Default.ConsultationRecord), ConsultationJsonContext.Default.ConsultationRecord)!;
        var third = ConsultationBilling.ThisTurnsBill(cumulative: true, read, new Usage(400, 40, null), mayRestart: true);

        (second.Share.TokensIn, second.Share.TokensOut).Should().Be((150L, 15L), "the first turn after the upgrade is measured against what was billed");
        read.LastReported.Should().Be(new Core.Consultation.ConsultationBilled(250, 25), "the vendor's counter is written with the record");
        (third.Share.TokensIn, third.Share.TokensOut).Should().Be((150L, 15L), "and the next turn is measured against it — billed once");
        read.Billed.TokensIn.Should().Be(250, "billed stays the sum of the shares");
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

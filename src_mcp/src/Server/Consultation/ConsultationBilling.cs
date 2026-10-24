using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;

namespace CoaiMcp.Server;

/// <summary>
/// What a consultation turn is billed, and what the caller is told it cost — one rule, out of the service.
/// </summary>
/// <remarks>
/// <para>Moved out of <c>ConsultationService</c> (past the 800-line ceiling, wiring only) when epic 1's code
/// round found the reply telling the caller the TURN's usage while the record and the ledger got its SHARE:
/// for a vendor that reports the conversation's running total, a second turn's reply carried turn 1's cost
/// as well. The defect could not be seen through any shipped vendor — antigravity, the one cumulative vendor,
/// reports no money — so the decision was moved here, where a test can hand it the case.</para>
/// <para>Epic 2 closed the two gaps it still had (PLAN_the_consultant_works_on_every_vendor.md, E2.3): the
/// share was rebuilt as <c>new Usage(in, out, cost)</c>, dropping the cached and reasoning counts and the
/// "no price set" / "not captured" flags the ledger keeps; and it subtracted only the ANSWERED turns, so a
/// failed or interrupted turn on a cumulative vendor was billed again inside the next answer. It now
/// subtracts <see cref="ConsultationRecord.Billed"/> — the running total every billing outcome advances —
/// and carries every field through.</para>
/// </remarks>
internal static class ConsultationBilling
{
    /// <summary>
    /// What THIS turn consumed, and the vendor's counter after it: for a vendor that reports the whole conversation's total
    /// every time, what it said less its counter as it last reported it (<see cref="ConsultationRecord.LastReported"/>); for
    /// any other, what it said, the counter untouched.
    /// </summary>
    /// <remarks>
    /// The subtraction is <see cref="ConsultationUsage.Next"/>, in the core — every field floored at zero, money only when
    /// this report names some, a restart billed whole only for a vendor that declares one possible (<paramref name="mayRestart"/>)
    /// — so the test exercises the rule rather than a copy of it.
    /// </remarks>
    public static CumulativeShare ThisTurnsBill(bool cumulative, ConsultationRecord record, Usage reported, bool mayRestart = false) =>
        cumulative ? ConsultationUsage.Next(reported, record.LastReported, mayRestart) : new CumulativeShare(reported, record.LastReported);

    /// <summary>The share of <see cref="ThisTurnsBill"/>.</summary>
    public static Usage ThisTurnsShare(bool cumulative, ConsultationRecord record, Usage reported, bool mayRestart = false) =>
        ThisTurnsBill(cumulative, record, reported, mayRestart).Share;

    /// <summary>The record once this turn is billed — the sum of the shares advanced, and the vendor's counter moved to what it reported.</summary>
    public static ConsultationRecord Billing(ConsultationRecord record, CumulativeShare bill) =>
        record with { Billed = record.Billed.Plus(bill.Share), LastReported = bill.Counter };

    /// <summary>The record once a share that reported no counter (nothing launched) is billed — the counter where it was.</summary>
    public static ConsultationRecord Billing(ConsultationRecord record, Usage share) =>
        Billing(record, new CumulativeShare(share, record.LastReported));

    /// <summary>What the caller's answer says this turn cost: its SHARE, the figure the record and the ledger get.</summary>
    public static double? ReplyCost(bool cumulative, ConsultationRecord record, ConsultantTurnResult turned, bool mayRestart = false) =>
        ThisTurnsShare(cumulative, record, turned.TurnUsage, mayRestart).CostUsd;
}

using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Consultation;

/// <summary>What one turn of a conversation consumed, when the vendor reports the whole of it.</summary>
/// <remarks>
/// <para>Antigravity does, measured on 2026-09-12: turn 1 reported 14 138 input tokens and turn 2
/// reported 30 843 — turn 1 plus turn 2. A consultation is the first thing in this product to run one
/// conversation twice, so it is the first place that shows. Left alone the ledger counts turn 1 again
/// on turn 2 and again on turn 3: a spending record that grows while the spending does not.</para>
/// <para>Pure, and in the core, because it is a RULE — and because the alternative was the service
/// holding it and a test reimplementing it beside, which proves only that the test can add.</para>
/// </remarks>
public static class ConsultationUsage
{
    /// <summary>
    /// This turn's share of a cumulative report: what the vendor said, less what the conversation was
    /// already billed — every field, floored at zero.
    /// </summary>
    /// <remarks>
    /// <para><b>The one subtraction.</b> It was two: a tokens-and-money rule here over a list of earlier
    /// turns, and the cached and reasoning counts floored by hand beside it in the server (epic 2's review).
    /// Now every field of <see cref="Usage"/> goes through this, against the running total the record keeps
    /// (<see cref="ConsultationBilled"/>).</para>
    /// <para><b>Floored at zero</b>, the first turn included: a vendor reporting LESS than was already billed,
    /// or a negative count, is reporting something this arithmetic does not understand, and a negative
    /// spending row would be worse than a flat one.</para>
    /// <para><b>Money is subtracted only when this report names some.</b> An unpriced report stays unpriced —
    /// a null here is "the vendor said nothing about money", which a zero would turn into "it was free". The
    /// two flags are facts about THIS report and travel unchanged.</para>
    /// </remarks>
    public static Usage Less(Usage reported, ConsultationBilled billed) =>
        reported with
        {
            TokensIn = Floor(reported.TokensIn - billed.TokensIn),
            TokensOut = Floor(reported.TokensOut - billed.TokensOut),
            CostUsd = reported.CostUsd is { } usd ? Math.Max(usd - billed.CostUsd, 0) : null,
            TokensCached = Floor(reported.TokensCached - billed.TokensCached),
            TokensReasoning = Floor(reported.TokensReasoning - billed.TokensReasoning),
        };

    private static long Floor(long value) => Math.Max(value, 0);

    /// <summary>
    /// What ONE turn that made two launches — the first, and its follow-up in the same conversation —
    /// consumed, billed once and never under-billed.
    /// </summary>
    /// <param name="cumulative">The vendor reports the conversation's running total on every launch.</param>
    /// <remarks>
    /// <para><b>Cumulative: the field-wise MAXIMUM.</b> The follow-up's report already contains the
    /// first's, so adding them bills the first launch twice. And it is the maximum rather than simply the
    /// second, because a second launch killed on its deadline reports <see cref="Usage.None"/>
    /// (<c>ReviewerExecutor.LaunchAsync</c>) — "the second said nothing" must not erase what the first
    /// said it spent. Measured 2026-10-02, the follow-up's input grew every time (13 574 → 20 748), which
    /// is consistent with a running total but does not prove one; under a per-launch reading the maximum
    /// under-bills by the first launch's share and never double-bills
    /// (research/RESULTS_agy_consult_follow_up.md §3).</para>
    /// <para><b>Otherwise: the sum</b> — two launches each reporting their own consumption.</para>
    /// <para>The flags are OR-ed either way: one launch whose usage was not captured, or whose row had no
    /// price, makes the turn's figure a floor, and the figure must say so.</para>
    /// </remarks>
    public static Usage OfTwoLaunches(bool cumulative, Usage first, Usage second) =>
        cumulative ? Larger(first, second) : first.Add(second);

    private static Usage Larger(Usage one, Usage other) => new(
        Math.Max(one.TokensIn, other.TokensIn),
        Math.Max(one.TokensOut, other.TokensOut),
        LargerCost(one.CostUsd, other.CostUsd),
        Math.Max(one.TokensCached, other.TokensCached),
        one.NoPriceSet || other.NoPriceSet,
        Math.Max(one.TokensReasoning, other.TokensReasoning),
        one.NotCaptured || other.NotCaptured);

    /// <summary>The larger of two reported costs; unreported only when neither launch reported one.</summary>
    private static double? LargerCost(double? one, double? other) =>
        one is null && other is null ? null : Math.Max(one ?? 0, other ?? 0);
}

/// <summary>What a conversation has been billed so far — the running total a cumulative vendor's next report is measured against.</summary>
/// <remarks>
/// <para>Every field a share can carry, so subtracting it (<see cref="ConsultationUsage.Less"/>) loses nothing
/// the ledger keeps. Money is a plain number here: a running total of what was billed, in which an unpriced
/// turn added nothing.</para>
/// <para>In the core, beside the subtraction, so the rule and the total it subtracts are one place; the server's
/// record keeps the value (<c>ConsultationRecord.Billed</c>).</para>
/// </remarks>
public sealed record ConsultationBilled(
    long TokensIn = 0,
    long TokensOut = 0,
    double CostUsd = 0,
    long TokensCached = 0,
    long TokensReasoning = 0)
{
    /// <summary>The total as a <see cref="Usage"/> — the shape <see cref="Usage.Add"/> sums.</summary>
    public Usage AsUsage() => new(TokensIn, TokensOut, CostUsd, TokensCached, TokensReasoning: TokensReasoning);

    /// <summary>The running total of a usage — an unpriced one adds no money.</summary>
    public static ConsultationBilled Of(Usage usage) =>
        new(usage.TokensIn, usage.TokensOut, usage.CostUsd ?? 0, usage.TokensCached, usage.TokensReasoning);

    /// <summary>This total with one more turn's share added — through <see cref="Usage.Add"/>, the one sum.</summary>
    public ConsultationBilled Plus(Usage share) => Of(AsUsage().Add(share));
}

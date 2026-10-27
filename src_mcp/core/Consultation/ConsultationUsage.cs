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
    /// This turn's share of a cumulative report: what the vendor said, less its counter as it last reported it
    /// (<paramref name="counter"/>) — every field, floored at zero.
    /// </summary>
    /// <remarks>
    /// <para><b>The one subtraction.</b> It was two: a tokens-and-money rule here over a list of earlier
    /// turns, and the cached and reasoning counts floored by hand beside it in the server (epic 2's review).
    /// Now every field of <see cref="Usage"/> goes through this.</para>
    /// <para><b>Against the vendor's counter, not the sum of the shares</b> (the E5.4 review's B1): the two agree while the
    /// counter only grows, and part the moment a restarted counter is billed whole — the sum stays inflated and every later
    /// report of the restarted session would read as a restart again. The record keeps both
    /// (<c>ConsultationRecord.LastReported</c> and <c>Billed</c>); <see cref="Next"/> advances the counter.</para>
    /// <para><b>Floored at zero</b>, the first turn included: a vendor reporting LESS than before, or a negative count, is
    /// reporting something this arithmetic does not understand, and a negative spending row would be worse than a flat one.</para>
    /// <para><b>Money is subtracted only when this report names some.</b> An unpriced report stays unpriced —
    /// a null here is "the vendor said nothing about money", which a zero would turn into "it was free". The
    /// two flags are facts about THIS report and travel unchanged.</para>
    /// <para><b>A counter that restarted is billed whole</b> — only for a vendor that DECLARES its counters can restart
    /// (<paramref name="mayRestart"/>, <see cref="IsAReset"/>; decision 12 of E5.4 in <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>):
    /// the floor alone read such a report as "0 this turn". antigravity does not declare it: only its input is measured cumulative.</para>
    /// </remarks>
    public static Usage Less(Usage reported, ConsultationBilled counter, bool mayRestart = false) =>
        mayRestart && IsAReset(reported, counter) ? reported : Subtracted(reported, counter);

    /// <summary>
    /// This turn's share, and the counter the NEXT report is measured against: this report when it carries a counter
    /// (<see cref="CarriesACounter"/>), the old counter when it does not.
    /// </summary>
    public static CumulativeShare Next(Usage reported, ConsultationBilled counter, bool mayRestart) =>
        new(Less(reported, counter, mayRestart), CarriesACounter(reported) ? ConsultationBilled.Of(reported) : counter);

    /// <summary>
    /// Whether a report IS the vendor's counter: captured, and not all zeros. An uncaptured report is zeros that are not a counter; a
    /// captured one of nothing is <see cref="Usage.None"/> — a turn that reached no model (nothing launched, a refused or not-started launch,
    /// a timed-out one), which consumed nothing and says nothing about the counter. Read as one, it would move the counter to 0/0 and bill
    /// the session's whole total again on the next real report (the E5.4 review of 2026-10-27, R1). A vendor's real report after a turn is
    /// never zero in both.
    /// </summary>
    public static bool CarriesACounter(Usage reported) =>
        !reported.NotCaptured && (reported.TokensIn > 0 || reported.TokensOut > 0);

    /// <summary>
    /// The vendor's counter RESTARTED: a report that carries a counter (<see cref="CarriesACounter"/>) whose input or output total is
    /// below the counter's last report — a session that was not the one loaded, a vendor-side restart. Both totals are judged together,
    /// never per field: a per-field mix would subtract one counter and restart the other. A report that carries none is never a reset
    /// (an uncaptured turn is billed unknown, and the next captured share carries its spend; a turn of nothing is billed nothing).
    /// </summary>
    public static bool IsAReset(Usage reported, ConsultationBilled counter) =>
        CarriesACounter(reported) && (reported.TokensIn < counter.TokensIn || reported.TokensOut < counter.TokensOut);

    private static Usage Subtracted(Usage reported, ConsultationBilled billed) =>
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

/// <summary>
/// What a conversation has been billed so far — the sum of its turns' shares — and, in the same shape, a cumulative vendor's counter as it last
/// reported it (<see cref="CumulativeShare.Counter"/>), which its next report is measured against (the E5.4 review's B1: the two part after a reset).
/// </summary>
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

/// <summary>What one cumulative report bills (<paramref name="Share"/>), and the vendor's counter after it (<paramref name="Counter"/>) — the baseline the next report is measured against.</summary>
public sealed record CumulativeShare(Usage Share, ConsultationBilled Counter);

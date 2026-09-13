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
    /// This turn's share of a cumulative report: what the vendor said, less what the earlier turns
    /// already recorded.
    /// </summary>
    /// <param name="alreadyRecorded">The earlier turns' own figures, in the order they happened.</param>
    /// <param name="reported">What the vendor said this time.</param>
    /// <remarks>
    /// Floored at zero. A vendor reporting LESS than it did before is reporting something this
    /// arithmetic does not understand, and a negative spending row would be worse than a flat one.
    /// </remarks>
    public static (long TokensIn, long TokensOut, double? CostUsd) ThisTurnsShare(
        IReadOnlyList<(long TokensIn, long TokensOut, double? CostUsd)> alreadyRecorded,
        (long TokensIn, long TokensOut, double? CostUsd) reported)
    {
        var soFar = Total(alreadyRecorded);

        // The clamp applies to the FIRST turn as well: a vendor reporting a negative count is
        // reporting something this arithmetic does not understand, and it must not reach the ledger
        // merely because there was nothing to subtract from it yet.
        return (
            Math.Max(reported.TokensIn - soFar.TokensIn, 0),
            Math.Max(reported.TokensOut - soFar.TokensOut, 0),
            reported.CostUsd is { } usd
                ? Math.Max(usd - soFar.CostUsd, 0)
                : reported.CostUsd);
    }

    /// <summary>What the earlier turns recorded between them, in one pass.</summary>
    /// <remarks>
    /// Its own method so the subtraction rule above reads as the one thing it is. An unpriced turn
    /// contributes nothing rather than making the total unknown: the caller only ever subtracts it
    /// from a cost the vendor DID report, and a null there would throw away a number somebody is
    /// paying for. (CodeRabbit, on the pull request — the complexity ceiling is this repository's
    /// own rule.)
    /// </remarks>
    private static (long TokensIn, long TokensOut, double CostUsd) Total(
        IReadOnlyList<(long TokensIn, long TokensOut, double? CostUsd)> turns)
    {
        var total = (TokensIn: 0L, TokensOut: 0L, CostUsd: 0d);
        foreach (var turn in turns)
        {
            total = (total.TokensIn + turn.TokensIn, total.TokensOut + turn.TokensOut, total.CostUsd + (turn.CostUsd ?? 0));
        }

        return total;
    }
}

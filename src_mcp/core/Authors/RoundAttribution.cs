namespace CoaiMcp.Core.Authors;

/// <summary>
/// Turns ONE caller session's observation and the gate session's journal into what one round did, per window (E4.S2): tiles the
/// journal into half-open windows from the previous recorded round's START, places every message, launch and write of the caller
/// that falls into THIS round's windows exactly once, and gives each the first rung of the association ladder that holds —
/// <c>wrote here</c>, <c>child of</c>, <c>same-session</c>, <c>ambiguous</c>, <c>wrote elsewhere</c> or <c>claimed by peer</c>.
/// Pure: no clock, no disk, no process — every instant is an argument.
/// </summary>
public static class RoundAttribution
{
    /// <summary>
    /// Bumped by ANY change to a rule of §S2.3–§S2.4 or of S3 and S4. E5 stores it beside the reader's version; E7's "equal reader →
    /// keep" compares both.
    /// </summary>
    public const int Version = 1;

    /// <exception cref="ArgumentException">The input contradicts its contract; the message names the first violation.</exception>
    public static WindowAssignment Assign(AttributionInput input) => Assign(input, usePriorEnd: true);

    /// <summary>
    /// <paramref name="usePriorEnd"/> false hands the tiling no prior end: the before-open window then reaches back for ever. A test
    /// seam that shows the bound is a read bound — the owned set is the same without it.
    /// </summary>
    internal static WindowAssignment Assign(AttributionInput input, bool usePriorEnd)
    {
        var violations = input.Validate();
        if (violations.Length > 0)
        {
            throw new ArgumentException(violations[0], nameof(input));
        }

        return new AssignRun(input, usePriorEnd).Build();
    }

    /// <summary>
    /// The earliest instant a line can matter to <see cref="Assign(AttributionInput)"/>: the first of this round's windows and of their
    /// claimants' windows. Computable from the timelines alone, so E5 calls it BEFORE it reads the transcript; null when any window it
    /// reads is unbounded.
    /// </summary>
    public static DateTimeOffset? ReadFrom(AttributionInput input) => ReadFrom(input, usePriorEnd: true);

    /// <summary>
    /// Whether a round of a named peer still runs and overlaps this round's windows: E4 §S2.3's window overlap over the peers'
    /// timelines — a window of THIS caller, not blind, not settled, sharing an instant with one of this round's non-blind windows. It is
    /// exactly the test that gives a window <c>partial: peer running</c>, so a backfill that asks "may this row be recomputed yet?" and the
    /// state a recompute would write cannot disagree. False when the peers are not known.
    /// </summary>
    public static bool PeerStillRuns(AttributionInput input) => new AssignRun(input, usePriorEnd: true).HasRunningPeer();

    internal static DateTimeOffset? ReadFrom(AttributionInput input, bool usePriorEnd) => new AssignRun(input, usePriorEnd).ReadFrom();
}

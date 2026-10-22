using System.Collections.Immutable;
using Stage = CoaiMcp.Core.Rounds.Stage;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// The tiling of a gate session's journal into windows, and the rank that decides who owns contested activity (E4.S2, §S2.3).
/// A window runs from the PREVIOUS recorded round's start to its own start — never its completion — so work done while a round was
/// being reviewed lands in the next window and is lost to none.
/// </summary>
public static class Windows
{
    public const string Done = "done";
    public const string Skipped = "skipped";
    public const string Interrupted = "interrupted";

    /// <summary>
    /// A round that will carry an authors snapshot: a plan or a code round that ran (not a skip) and was not interrupted. Every other
    /// round of the journal opens no window — its stretch lands in the next recorded round.
    /// </summary>
    public static bool IsRecorded(GateRound round) =>
        round.Stage is nameof(Stage.PlanReview) or nameof(Stage.CodeReview) && round.Verdict != Skipped && round.Status is not (Interrupted or Rounds.RoundStatus.Cancelled);

    /// <summary>
    /// Recorded and <c>done</c>: the CLAIM condition. A recorded round that is not settled is <c>running</c>; it tiles its own gate but
    /// ranks last, claims nothing and bounds no prior end (§S2.2b G1, G2).
    /// </summary>
    public static bool IsSettled(GateRound round) => IsRecorded(round) && round.Status == Done;

    /// <summary>
    /// The windows of one gate session. <paramref name="priorEnd"/> is where the first recorded round's <c>before-open</c> window
    /// begins (null = unbounded). A recorded round with no start is BLIND, and so is the next recorded round: a window with one unknown
    /// edge would over-credit its neighbour (decision 1). A blind window has no edge and takes part in nothing.
    /// </summary>
    public static ImmutableArray<Window> Of(GateTimeline gate, DateTimeOffset? priorEnd)
    {
        var recorded = gate.Rounds.Where(IsRecorded).ToList();

        return [.. recorded.SelectMany((round, index) => index == 0 ? First(gate, round, priorEnd) : [Next(gate, round, recorded[index - 1])])];
    }

    /// <summary>
    /// The end of the other gate sessions' SETTLED PREFIX on this gate's repository: for each peer of the same <c>repo_path</c>, walk
    /// its recorded rounds in journal order while the round is settled, not blind, of this caller, started at or before this gate's
    /// open and COMPLETED BEFORE this gate's first round (so that it claims what lies before the bound), and take the last start. The
    /// walk STOPS at the first round that fails — it never skips — because an unsettled, blind or other-caller window claims nothing,
    /// and a message in it would be owned by nobody with the bound and by this gate without it. A READ bound, not an ownership rule.
    /// </summary>
    internal static DateTimeOffset? PriorEnd(GateTimeline gate, IEnumerable<GateTimeline> others, string caller)
    {
        var first = gate.Rounds.FirstOrDefault(IsRecorded);
        if (first is null || gate.OpenedUtc is not { } open)
        {
            return null;
        }

        var own = Of(gate, null).First(window => window.Kind == WindowKind.BeforeOpen);

        return others.Where(other => Repositories.SameRoot(other.RepoPath, gate.RepoPath)).Select(other => SettledPrefixEnd(other, open, caller, own)).Max();
    }

    /// <summary>The ordinal order of rank: completion (not settled = last), then gate session id, then <c>before-open</c> before <c>round</c>.</summary>
    public static int Compare(Window left, Window right)
    {
        var byCompletion = CompletionOf(left).CompareTo(CompletionOf(right));

        return byCompletion != 0 ? byCompletion : ByGateThenKind(left, right);
    }

    private static DateTimeOffset CompletionOf(Window window) => window.CompletedUtc ?? DateTimeOffset.MaxValue;

    private static int ByGateThenKind(Window left, Window right)
    {
        var byGate = string.CompareOrdinal(left.Gate, right.Gate);

        return byGate != 0 ? byGate : KindOrder(left).CompareTo(KindOrder(right));
    }

    public static bool Precedes(Window left, Window right) => Compare(left, right) < 0;

    /// <summary>Whether two non-blind windows share an instant; an unbounded <c>From</c> reaches back for ever.</summary>
    public static bool Overlaps(Window left, Window right)
    {
        if (left.Blind || right.Blind)
        {
            return false;
        }

        var low = Later(left.From, right.From);

        return low is null || low < Earlier(left.To, right.To);
    }

    /// <summary>The reason a blind window is blind: the first round's missing open time, else a missing round start.</summary>
    internal static string BlindReason(GateTimeline gate, Window window) =>
        OpenTimeIsTheGap(gate, window) ? AttributionReasons.NoOpenTime : AttributionReasons.NoRoundStart;

    private static bool OpenTimeIsTheGap(GateTimeline gate, Window window)
    {
        var first = gate.Rounds.FirstOrDefault(IsRecorded);

        return first is not null && IsRoundOf(first, window) && first.StartedUtc is not null && gate.OpenedUtc is null;
    }

    /// <summary>Whether <paramref name="window"/> is one of the windows of <paramref name="round"/>.</summary>
    internal static bool IsRoundOf(GateRound round, Window window) => round.Stage == window.Stage && round.Number == window.Number;

    private static int KindOrder(Window window) => window.Kind == WindowKind.BeforeOpen ? 0 : 1;

    private static DateTimeOffset? Later(DateTimeOffset? left, DateTimeOffset? right) =>
        left is { } a && right is { } b ? Larger(a, b) : left ?? right;

    private static DateTimeOffset? Earlier(DateTimeOffset? left, DateTimeOffset? right) =>
        left is { } a && right is { } b ? Smaller(a, b) : left ?? right;

    private static DateTimeOffset Larger(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static DateTimeOffset Smaller(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private static DateTimeOffset? SettledPrefixEnd(GateTimeline peer, DateTimeOffset open, string caller, Window own)
    {
        DateTimeOffset? last = null;
        foreach (var window in Of(peer, null).Where(candidate => candidate.Kind == WindowKind.Round))
        {
            if (!ClaimsBefore(window, open, caller, own))
            {
                break;
            }

            last = window.To;
        }

        return last;
    }

    private static bool ClaimsBefore(Window window, DateTimeOffset open, string caller, Window own) =>
        IsSettledFor(window, caller) && window.To <= open && Precedes(window, own);

    private static bool IsSettledFor(Window window, string caller) => !window.Blind && window.CompletedUtc is not null && window.CallerSessionId == caller;

    private static IEnumerable<Window> First(GateTimeline gate, GateRound round, DateTimeOffset? priorEnd)
    {
        var blind = round.StartedUtc is null || gate.OpenedUtc is null;
        var edge = UnlessBlind(blind, Earlier(gate.OpenedUtc, round.StartedUtc));

        return
        [
            Make(gate, WindowKind.BeforeOpen, round, UnlessBlind(blind, priorEnd), edge, blind),
            Make(gate, WindowKind.Round, round, edge, UnlessBlind(blind, round.StartedUtc), blind),
        ];
    }

    private static Window Next(GateTimeline gate, GateRound round, GateRound previous)
    {
        var blind = round.StartedUtc is null || previous.StartedUtc is null;

        return Make(gate, WindowKind.Round, round, UnlessBlind(blind, previous.StartedUtc), UnlessBlind(blind, round.StartedUtc), blind);
    }

    /// <summary>A blind window has no edge: whatever instant was about to bound it is dropped.</summary>
    private static DateTimeOffset? UnlessBlind(bool blind, DateTimeOffset? edge) => blind ? null : edge;

    private static Window Make(GateTimeline gate, string kind, GateRound round, DateTimeOffset? from, DateTimeOffset? to, bool blind) =>
        new(gate.GateSessionId, kind, round.Stage, round.Number, from, to, round.CallerSessionId, round.Status == Done ? round.CompletedUtc : null, blind);
}

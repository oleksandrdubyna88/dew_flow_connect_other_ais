using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// The state of one window of the round (E4 §S2.3 <i>States</i>): the WORST kind wins (<c>observed &lt; partial &lt; unobserved</c>) and its
/// reasons are those of that kind, distinct, in first-seen order. Every reason is one of <see cref="AttributionReasons"/> or E3's
/// constant, copied verbatim — never composed, never carrying a count.
/// </summary>
internal static class WindowStates
{
    private readonly record struct Reason(string Kind, string Why);

    public static WindowState Of(Window window, AttributionInput input, bool hasRunningPeer, GateTimeline gate)
    {
        var reasons = Gather(window, input, hasRunningPeer, gate).ToList();
        var worst = reasons.Select(reason => reason.Kind).DefaultIfEmpty(ObservationKinds.Observed).MaxBy(Badness) ?? ObservationKinds.Observed;

        return new WindowState(window, worst, [.. reasons.Where(reason => reason.Kind == worst).Select(reason => reason.Why).Distinct(StringComparer.Ordinal)]);
    }

    private static int Badness(string kind) => kind switch
    {
        ObservationKinds.Unobserved => 2,
        ObservationKinds.Partial => 1,
        _ => 0,
    };

    private static IEnumerable<Reason> Gather(Window window, AttributionInput input, bool hasRunningPeer, GateTimeline gate) =>
    [
        .. CallerUnobserved(input),
        .. Blind(window, gate),
        .. PeersUnknown(input),
        .. UnobservedLaunches(window, input),
        .. RunningPeer(hasRunningPeer),
        .. CallerReasons(window, input),
        .. CallerChanged(window, gate),
    ];

    private static IEnumerable<Reason> CallerUnobserved(AttributionInput input) =>
        input.Caller.State.Kind == ObservationKinds.Unobserved
            ? input.Caller.State.Reasons.Select(reason => new Reason(ObservationKinds.Unobserved, reason.Why))
            : [];

    private static IEnumerable<Reason> Blind(Window window, GateTimeline gate) =>
        window.Blind ? [new Reason(ObservationKinds.Unobserved, Windows.BlindReason(gate, window))] : [];

    private static IEnumerable<Reason> PeersUnknown(AttributionInput input) =>
        input.PeersKnown ? [] : [new Reason(ObservationKinds.Partial, AttributionReasons.PriorWindowsUnknown)];

    private static IEnumerable<Reason> UnobservedLaunches(Window window, AttributionInput input) =>
        input.Caller.Unobserved.Where(item => IsIn(item.At, window)).OrderBy(item => item.At ?? DateTimeOffset.MinValue).ThenBy(item => item.ToolUseId, StringComparer.Ordinal)
            .Select(item => new Reason(ObservationKinds.Partial, item.Why));

    private static IEnumerable<Reason> RunningPeer(bool hasRunningPeer) =>
        hasRunningPeer ? [new Reason(ObservationKinds.Partial, AttributionReasons.PeerRunning)] : [];

    private static IEnumerable<Reason> CallerReasons(Window window, AttributionInput input) =>
        input.Caller.State.Kind == ObservationKinds.Partial
            ? input.Caller.State.Reasons.Where(reason => IsIn(reason.At, window)).Select(reason => new Reason(ObservationKinds.Partial, reason.Why))
            : [];

    /// <summary>An item with no instant is about the whole session, so it is in every window.</summary>
    private static bool IsIn(DateTimeOffset? at, Window window) => at is not { } instant || window.Contains(instant);

    private static IEnumerable<Reason> CallerChanged(Window window, GateTimeline gate) =>
        CallerBefore(window, gate).Where(before => before != window.CallerSessionId).Select(ChangeOf);

    private static Reason ChangeOf(string before) =>
        new(ObservationKinds.Partial, before.Length == 0 ? AttributionReasons.PriorCallerUnknown : AttributionReasons.CallerChanged);

    /// <summary>The caller of the recorded round before this window's own, for a sighted round window that has one; nothing otherwise.</summary>
    private static IEnumerable<string> CallerBefore(Window window, GateTimeline gate) =>
        window.Kind == WindowKind.Round && !window.Blind ? CallerOfRoundBefore(window, gate.Rounds.Where(Windows.IsRecorded).ToList()) : [];

    private static IEnumerable<string> CallerOfRoundBefore(Window window, List<GateRound> recorded)
    {
        var index = recorded.FindIndex(round => Windows.IsRoundOf(round, window));

        return index >= 1 ? [recorded[index - 1].CallerSessionId] : [];
    }
}

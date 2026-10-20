using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>Who owns one item (a message or a launch) and under what label (E4 §S2.3 <i>Ownership</i>).</summary>
internal readonly record struct Owned(string Association, string Rung, string OwnerGate);

/// <summary>
/// Ownership of contested activity: a window of THIS gate owns an item unless its own ladder says the agent wrote elsewhere (which is
/// this gate's own evidence — claimants are not consulted) or a SETTLED window of another gate session of the same caller, ranked
/// before it, contains the instant and its ladder is owning there (E4 decision 3, completion rank).
/// </summary>
internal static class Ownership
{
    /// <summary>
    /// The peer windows that may claim from <paramref name="window"/>: same caller, not blind, settled, ranked before it and
    /// overlapping it — in rank order. An unsettled or other-caller window never claims (§S2.2b G1, G2).
    /// </summary>
    public static ImmutableArray<Window> ClaimantsOf(Window window, IEnumerable<Window> peerWindows, string caller) =>
    [
        .. peerWindows.Where(peer => peer.CallerSessionId == caller && !peer.Blind && peer.CompletedUtc is not null)
            .Where(peer => Windows.Precedes(peer, window) && Windows.Overlaps(peer, window))
            .Order(Comparer<Window>.Create(Windows.Compare)),
    ];

    /// <summary>
    /// Whether an unsettled peer window of this caller overlaps <paramref name="window"/>: ownership decided now is PROVISIONAL until
    /// that round is final (§S2.2b G3), and E7's backfill recomputes exactly those rows.
    /// </summary>
    public static bool HasRunningPeer(Window window, IEnumerable<Window> peerWindows, string caller) =>
        peerWindows.Any(peer => peer.CallerSessionId == caller && !peer.Blind && peer.CompletedUtc is null && Windows.Overlaps(peer, window));

    /// <summary>The owner of one item of <paramref name="agent"/> at <paramref name="at"/> in <paramref name="window"/>.</summary>
    public static Owned Of(AgentKey agent, DateTimeOffset at, Window window, IEnumerable<Window> claimants, AssociationLadder ladder)
    {
        var own = ladder.Of(agent, window);
        if (own.Association == Association.WroteElsewhere)
        {
            return new Owned(Association.WroteElsewhere, own.Rung, string.Empty);
        }

        var claimant = claimants.FirstOrDefault(peer => peer.Contains(at) && Association.IsOwning(ladder.Of(agent, peer).Association));

        return claimant is null ? new Owned(own.Association, own.Rung, window.Gate) : new Owned(Association.ClaimedByPeer, own.Rung, claimant.Gate);
    }
}

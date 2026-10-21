using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// The authors of one stored round, computed by THE live step (E7.S2): <see cref="RoundAuthorsStep"/> is handed the session as it stood before
/// the round, the round itself, the stored caller's id through its injected environment and the one peer set — so a backfilled snapshot is the
/// snapshot the live path would have written, by construction rather than by a second implementation that could drift.
/// </summary>
/// <remarks>
/// <para><b>What differs from live, and only this:</b> the budget (64 MiB of the slice and 60 s per caller, never the live 2 s — no answer waits
/// on a backfill, and a wall clock would make it depend on machine load); the peer lookup (E5.S4's <see cref="AuthorPeers.Find"/> with
/// <c>deadline: null</c>, run ONCE per round and shared by the decision and the step); the reader (one read per caller, <see cref="CallerReads"/>);
/// and the <c>authors_id_source</c> — the live server's choice cannot be re-asked after the fact, so a recompute KEEPS the one the row has and
/// <c>stored</c> is written only where the row had none.</para>
/// <para>The step never throws for a failing reader (<c>unobserved: reader failed</c>) and is handed settled rounds only.</para>
/// </remarks>
internal sealed class BackfillAttribution(
    PanelSettings settings,
    Serilog.ILogger log,
    SessionStore store,
    CallerReads reads,
    TimeProvider clock,
    Func<AttributionInput, WindowAssignment>? assign = null,
    Func<string, string, PersistedSession?>? load = null)
{
    /// <summary>What a backfill may spend reading ONE caller session: the live byte cap and 60 seconds (E7 §10 item 7).</summary>
    internal static AuthorsBudget Budget { get; } = new(AuthorsBudget.Default.MaxBytes, TimeSpan.FromSeconds(60));

    /// <summary>The id source written when the row has none — the backfill read the stored id.</summary>
    internal const string StoredSource = CallerSessionId.Stored;

    /// <summary>The one peer lookup of a round, over the mode's read-only connection, unbounded.</summary>
    public AuthorPeerSet PeersOf(Microsoft.Data.Sqlite.SqliteConnection db, string callerId, string gateSessionId) =>
        callerId.Length == 0
            ? AuthorPeerSet.Unknown("no caller session id")
            : AuthorPeers.Find(db, new PeerLookup(callerId, gateSessionId), load ?? ((repo, branch) => store.Load(repo, branch)), null, clock);

    /// <summary>Whether a round of a peer the row NAMES, overlapping this round's windows, still runs — E4's own predicate over the same timelines.</summary>
    public PeerStatus StatusOf(PersistedSession before, RoundRecord record, string callerId, AuthorPeerSet set, SnapshotPresence presence)
    {
        if (!set.PeersKnown)
        {
            return new PeerStatus(false, [], false);
        }

        var gate = RoundAuthorsStep.GateOf(before, record, callerId, set);
        var named = Named(set.Peers, presence);
        var input = RoundAuthorsStep.InputOf(record, RoundAuthorsStep.EmptyObservation(callerId), gate, named, peersKnown: true);

        return new PeerStatus(true, [.. set.Peers.Select(peer => peer.GateSessionId)], input.Validate().IsEmpty && RoundAttribution.PeerStillRuns(input));
    }

    /// <summary>The peers a snapshot was built against — all of them when it names none that can be trusted (healed, or no snapshot).</summary>
    private static ImmutableArray<GateTimeline> Named(ImmutableArray<GateTimeline> peers, SnapshotPresence presence) =>
        presence.Has && presence.IdSource != "healed" && !presence.PeerGates.IsEmpty
            ? [.. peers.Where(peer => presence.PeerGates.Contains(peer.GateSessionId))]
            : peers;

    /// <summary>
    /// The snapshot of <paramref name="record"/>. With <paramref name="planning"/> the reader is the probe: the bound is recorded and the snapshot
    /// thrown away, so a whole run can learn each caller's lowest bound before one transcript is read.
    /// </summary>
    public AuthorsSnapshot Compute(PersistedSession before, RoundRecord record, string callerId, AuthorPeerSet set, string liveIdSource, bool planning) =>
        WithIdSource(StepFor(callerId, set, planning).For(before, record), liveIdSource);

    /// <summary>The live step, handed the stored caller's id through the environment it reads and nothing else of this machine.</summary>
    private RoundAuthorsStep StepFor(string callerId, AuthorPeerSet set, bool planning) =>
        new(
            settings,
            log,
            store,
            environment: EnvironmentOf(callerId),
            clock: clock,
            budget: Budget,
            peers: (_, _) => set,
            read: planning ? reads.Probe : reads.Read,
            assign: assign,
            load: load);

    private static Func<string, string?> EnvironmentOf(string callerId) =>
        name => name == "CLAUDE_CODE_SESSION_ID" ? callerId : null;

    /// <summary>The id source the row has, else <c>stored</c>: the live server's choice cannot be re-asked after the fact.</summary>
    private static AuthorsSnapshot WithIdSource(AuthorsSnapshot? snapshot, string liveIdSource) =>
        (snapshot ?? throw new InvalidOperationException("The step answers a snapshot for every settled round.")) with { IdSource = liveIdSource.Length > 0 ? liveIdSource : StoredSource };
}

using CoaiMcp.Core.Rounds;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>Everything the decision and the compute need about ONE stored round, gathered once.</summary>
/// <param name="Gate">The gate session; its journal is the overlay the run keeps current.</param>
/// <param name="Index">The round's place in the gate's journal, or -1 when the journal does not list it.</param>
/// <param name="CallerId">The caller's session id: <c>rounds.authors_session</c>, else a UUID <c>rounds.caller</c>, else empty (E5 §3's order).</param>
/// <param name="Fingerprint">The round's <see cref="RoundFingerprint"/> when it was selected: what the write validates against, inside its lock.</param>
/// <param name="Peers">THE one peer lookup of the round — shared by the decision and the compute, so a round asks its store once.</param>
internal sealed record StoredRoundFacts(
    StoredRound Round,
    StoredGate Gate,
    int Index,
    string CallerId,
    CallerTranscriptLocation Location,
    AuthorPeerSet Peers,
    SnapshotPresence Presence,
    PeerStatus PeerStatus,
    bool ProjectionAgrees,
    string Fingerprint)
{
    /// <summary>The transcript was found (<c>TranscriptLocated</c> — apart from E3's per-agent <c>TranscriptPresent</c>).</summary>
    public bool TranscriptLocated => Location is CallerTranscriptLocation.Found;

    /// <summary>The round's record as the journal holds it NOW — a run that gave an earlier round its snapshot has already replaced that round's entry.</summary>
    public RoundRecord Record => Gate.RecordOf(Round, Index);

    /// <summary>The session as the engine held it before the round: the rounds that ran earlier (with what the run gave them), the real open time.</summary>
    public PersistedSession Before => Gate.Before(Round, Index);

    public BackfillInput Input(DateTime? since) => new(Round, Presence, TranscriptLocated, PeerStatus, ProjectionAgrees, since);
}

/// <summary>
/// The facts of a stored round (E7.S2): which file, which snapshot, which transcript, which peers — every question the decision asks, each answered
/// by the unit that already owns it (<see cref="CallerTranscriptLocator"/>, <see cref="AuthorPeers"/>, <see cref="RoundAttribution"/>), never by a
/// second implementation.
/// </summary>
/// <remarks>
/// <para>The transcript is located BEFORE the decision, because its absence is what turns a recompute into a re-projection or a heal. The peer
/// lookup runs only for a round with a transcript — without one nothing is attributed and no peer matters — and once, with no deadline.</para>
/// <para>A journal that does not list the round (a file that lost it) cannot hold its snapshot: the presence says so, and the round is built from
/// its row and written to the database alone.</para>
/// </remarks>
internal sealed class StoredFacts(SqliteConnection db, PanelSettings settings, BackfillAttribution attribution)
{
    public StoredRoundFacts Of(StoredGate gate, StoredRound round)
    {
        var index = gate.IndexOf(round);
        var record = gate.RecordOf(round, index);
        var caller = CallerOf(round);
        var location = CallerTranscriptLocator.LocateStored(round.CallerVendor, caller, settings.TranscriptHome);
        var presence = PresenceOf(gate, index, record);
        var (peers, status) = location is CallerTranscriptLocation.Found
            ? PeersOf(gate, round, index, record, caller, presence)
            : (AuthorPeerSet.Unknown("no transcript, no peer lookup"), PeerStatus.Nobody);

        return new StoredRoundFacts(round, gate, index, caller, location, peers, presence, status, Agrees(presence, record, db, round), RoundFingerprint.Read(db.CreateCommand, round.RoundId));
    }

    private (AuthorPeerSet Peers, PeerStatus Status) PeersOf(StoredGate gate, StoredRound round, int index, RoundRecord record, string caller, SnapshotPresence presence)
    {
        var peers = attribution.PeersOf(db, caller, gate.SessionId);

        return (peers, attribution.StatusOf(gate.Before(round, index), record, caller, peers, presence));
    }

    /// <summary>E5 §3's earlier-caller order, for a stored round: the id the projection recorded, else a UUID <c>caller</c>, else none.</summary>
    internal static string CallerOf(StoredRound round) =>
        round.AuthorsSession.Length > 0 ? round.AuthorsSession : AuthorPeers.UuidOrEmpty(round.Caller);

    private static SnapshotPresence PresenceOf(StoredGate gate, int index, RoundRecord record) =>
        gate.FileState == SessionFileState.Present && index >= 0
            ? Holding(record)
            : new SnapshotPresence(gate.FileState, false, false, string.Empty, 0, 0, [], string.Empty, false);

    /// <summary>The file is there and lists the round: what it holds for it.</summary>
    private static SnapshotPresence Holding(RoundRecord record) =>
        record.Authors is { } snapshot
            ? new SnapshotPresence(SessionFileState.Present, true, true, snapshot.Render(), snapshot.Reader, snapshot.Attribution, snapshot.PeerGates, snapshot.IdSource, record.Size is not null)
            : new SnapshotPresence(SessionFileState.Present, true, false, string.Empty, 0, 0, [], string.Empty, record.Size is not null);

    private static bool Agrees(SnapshotPresence presence, RoundRecord record, SqliteConnection db, StoredRound round) =>
        !presence.Has || AuthorsProjection.Agree(record.Authors!, db, round.RoundId);
}

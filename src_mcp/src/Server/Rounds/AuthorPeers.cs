using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>Whose peers to look for: the caller's session id, and the gate session that is being attributed (never its own peer).</summary>
internal readonly record struct PeerLookup(string CallerSessionId, string GateSessionId);

/// <summary>
/// What the peer lookup found. <see cref="PeersKnown"/> is E4's input flag: <b>false</b> when the lookup did not complete — the store could
/// not answer, a peer's session file did not load or named another session, or the deadline cut the walk. Then <see cref="Peers"/> is empty
/// and E4 says <c>partial: prior windows unknown</c> instead of treating "no peers" as a fact.
/// </summary>
/// <param name="Peers">Every gate session other than this one that holds a round of this caller, built from its SESSION FILE.</param>
/// <param name="EarlierCallers">The caller of each round of THIS gate session as the store recorded it, by (stage, number) — empty when unknown.</param>
/// <param name="Why">Why the lookup did not complete, for a Debug line; empty when it did. Never a transcript fragment.</param>
internal sealed record AuthorPeerSet(
    bool PeersKnown,
    ImmutableArray<GateTimeline> Peers,
    IReadOnlyDictionary<(string Stage, int Number), string> EarlierCallers,
    string Why = "")
{
    public static AuthorPeerSet Unknown(string why) =>
        new(false, [], new Dictionary<(string Stage, int Number), string>(), why);
}

/// <summary>The peer lookup as the step takes it: a seam whose default is <see cref="AuthorPeers"/> over the real store.</summary>
internal delegate AuthorPeerSet AuthorPeerSource(PeerLookup lookup, DateTimeOffset? deadline);

/// <summary>
/// The peer gate sessions of one caller, and the callers of this gate session's earlier rounds — ONE read-only lookup and one build,
/// shared by the live path (E5.S4) and E7's backfill, so the two cannot disagree about what a round's history was (E5 §10 items 8 and 14).
/// </summary>
/// <remarks>
/// <para><b>The lookup</b> asks the projection which OTHER gate sessions hold a settled-or-skipped plan or code round whose
/// <c>authors_session</c> — else, when that is empty, a UUID <c>caller</c> — is this caller. <b>Each peer is then built from its SESSION
/// FILE</b> (<see cref="SessionStore.Load"/>, reached through <c>load</c> so a test can spy on it): its <c>OpenedUtc</c> from the file, never
/// <c>sessions.opened_utc</c>; its journal from the file; a round's completion only when it is <c>done</c> — a running row carries a stamp
/// too, its last progress tick (E4 G1) — through <see cref="AttributionMapping.Timeline"/>, the one mapping. A round's caller is its own
/// snapshot's session when the file has one, else the store's <c>authors_session</c>, else a UUID <c>caller</c>, else empty.</para>
/// <para><b>The limit, stated</b> (E5 §3, M3): <c>RecordRound</c> runs at completion and on a skip, never for a running round, so a peer
/// whose only same-caller round is still running is NOT found; E7's grown-peer-set rule recomputes this round once it is. And a RUNNING
/// round of a found peer has no caller on disk — no row, no snapshot — so it is built with an empty caller and, in production, E4's
/// <c>peer running</c> cannot name it until it is projected.</para>
/// <para>The deadline (null = unbounded, which is what E7 passes) is checked before EACH peer file is loaded; a load past it ends the walk
/// with <see cref="AuthorPeerSet.PeersKnown"/> false. The connection is read-only and short (<see cref="OpenReadOnly"/>): a lookup is never
/// worth a wait.</para>
/// </remarks>
internal static class AuthorPeers
{
    private const string PeerRounds = """
        SELECT DISTINCT s.id, s.repo_path, s.branch
        FROM rounds r JOIN sessions s ON s.id = r.session_id
        WHERE s.id <> $gate AND r.stage IN ('PlanReview', 'CodeReview')
          AND (r.authors_session = $caller OR (r.authors_session = '' AND lower(r.caller) = $caller))
        ORDER BY s.id
        """;

    private const string CallersOfRounds = """
        SELECT session_id, stage, number, authors_session, caller
        FROM rounds
        WHERE session_id IN (SELECT value FROM json_each($ids))
        """;

    private sealed record PeerRow(string Id, string RepoPath, string Branch);

    /// <summary>A read-only connection that waits at most a second, or null when the store is absent or will not open.</summary>
    public static SqliteConnection? OpenReadOnly(string dataDir)
    {
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return null;
        }

        var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False;Default Timeout=1");
        try
        {
            connection.Open();

            return connection;
        }
        catch (SqliteException)
        {
            connection.Dispose();

            return null;
        }
    }

    /// <summary>
    /// The peers of <paramref name="lookup"/>'s caller and the callers this gate session's rounds had. Never throws for a store that cannot
    /// answer — that is <see cref="AuthorPeerSet.PeersKnown"/> false.
    /// </summary>
    public static AuthorPeerSet Find(
        SqliteConnection? connection,
        PeerLookup lookup,
        Func<string, string, PersistedSession?> load,
        DateTimeOffset? deadline,
        TimeProvider clock)
    {
        if (connection is null)
        {
            return AuthorPeerSet.Unknown("no rounds database");
        }

        try
        {
            return Walk(connection, lookup, load, deadline, clock);
        }
        catch (Exception e) when (e is SqliteException or InvalidOperationException)
        {
            return AuthorPeerSet.Unknown(e.GetType().Name + ": " + e.Message);
        }
    }

    private static AuthorPeerSet Walk(SqliteConnection connection, PeerLookup lookup, Func<string, string, PersistedSession?> load, DateTimeOffset? deadline, TimeProvider clock)
    {
        var peers = PeersOf(connection, lookup);
        var callers = CallersOf(connection, [lookup.GateSessionId, .. peers.Select(peer => peer.Id)]);
        var timelines = new List<GateTimeline>();
        foreach (var peer in peers)
        {
            if (Expired(deadline, clock))
            {
                return AuthorPeerSet.Unknown("the deadline passed before every peer file was loaded");
            }

            if (Timeline(peer, load, callers) is not { } timeline)
            {
                return AuthorPeerSet.Unknown("a peer's session file did not load, or names another session");
            }

            timelines.Add(timeline);
        }

        return new AuthorPeerSet(true, [.. timelines], OfGate(callers, lookup.GateSessionId));
    }

    /// <summary>Whether a deadline is set and has passed; null is unbounded (E7's backfill).</summary>
    private static bool Expired(DateTimeOffset? deadline, TimeProvider clock) => deadline is { } at && clock.GetUtcNow() >= at;

    private static List<PeerRow> PeersOf(SqliteConnection connection, PeerLookup lookup)
    {
        using var read = connection.CreateCommand();
        read.CommandText = PeerRounds;
        read.Parameters.AddWithValue("$gate", lookup.GateSessionId);
        read.Parameters.AddWithValue("$caller", lookup.CallerSessionId);
        using var rows = read.ExecuteReader();
        var peers = new List<PeerRow>();
        while (rows.Read())
        {
            peers.Add(new PeerRow(Columns.Text(rows, "id"), Columns.Text(rows, "repo_path"), Columns.Text(rows, "branch")));
        }

        return peers;
    }

    /// <summary>The stored caller of every round of the given sessions: <c>authors_session</c>, else a UUID <c>caller</c>, else empty.</summary>
    private static Dictionary<(string Session, string Stage, int Number), string> CallersOf(SqliteConnection connection, IEnumerable<string> sessionIds)
    {
        using var read = connection.CreateCommand();
        read.CommandText = CallersOfRounds;
        read.Parameters.AddWithValue("$ids", System.Text.Json.JsonSerializer.Serialize(sessionIds.ToList(), ServerJsonContext.Default.ListString));
        using var rows = read.ExecuteReader();
        var callers = new Dictionary<(string Session, string Stage, int Number), string>();
        while (rows.Read())
        {
            var session = Columns.Text(rows, "authors_session");
            callers[(Columns.Text(rows, "session_id"), Columns.Text(rows, "stage"), Columns.Number(rows, "number"))] =
                session.Length > 0 ? session : UuidOrEmpty(Columns.Text(rows, "caller"));
        }

        return callers;
    }

    private static Dictionary<(string Stage, int Number), string> OfGate(Dictionary<(string Session, string Stage, int Number), string> callers, string gate) =>
        callers.Where(pair => pair.Key.Session == gate).ToDictionary(pair => (pair.Key.Stage, pair.Key.Number), pair => pair.Value);

    private static GateTimeline? Timeline(PeerRow peer, Func<string, string, PersistedSession?> load, Dictionary<(string Session, string Stage, int Number), string> callers)
    {
        var file = load(peer.RepoPath, peer.Branch);
        if (file is null || file.State.SessionId != peer.Id)
        {
            return null;
        }

        return AttributionMapping.Timeline(peer.Id, peer.RepoPath, file.OpenedUtc, file.Rounds, round => CallerOf(round, peer.Id, callers));
    }

    /// <summary>A round's own snapshot's session when the file has one (a round after E5), else the store's, else empty.</summary>
    private static string CallerOf(RoundRecord round, string session, Dictionary<(string Session, string Stage, int Number), string> callers) =>
        round.Authors is { Session.Length: > 0 } snapshot
            ? snapshot.Session
            : callers.GetValueOrDefault((session, round.Stage, round.Number)) ?? string.Empty;

    /// <summary>The id as a lower-case GUID, or empty: <c>repo:…</c>, a stated string and anything else is not a session id.</summary>
    internal static string UuidOrEmpty(string raw) =>
        Guid.TryParseExact(raw.Trim(), "D", out var guid) ? guid.ToString("D") : string.Empty;
}

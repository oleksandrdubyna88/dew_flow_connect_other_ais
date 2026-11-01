using System.Collections.Immutable;

namespace CoaiMcp.Server;

/// <summary>What the disk says about the gate session's FILE (E7.S1): there, gone, a different session's, or there and not loading.</summary>
internal enum SessionFileState
{
    /// <summary>The file exists and its <c>State.SessionId</c> is the round's session.</summary>
    Present,

    /// <summary><c>SessionStore.Exists</c> is false: there is no file to read or to write.</summary>
    Gone,

    /// <summary>The file exists and names another session (a branch reopened under a new id): treated as no file.</summary>
    Replaced,

    /// <summary>The file exists and <c>SessionStore.Load</c> answered null — torn or busy. Never a gone file: retried, never recorded on the round.</summary>
    Unreadable,
}

/// <summary>One settled plan or code round as the database holds it — the columns the decision reads, nothing more.</summary>
/// <param name="AuthorsReader">E5's integer: <c>0</c> = the reader did not run.</param>
/// <param name="AuthorsAttribution">E5's integer: <c>0</c> = the attribution did not run.</param>
/// <param name="CallerVendor"><c>rounds.caller_vendor</c> — <c>claude</c>, <c>codex</c>, … the reader exists for claude only.</param>
internal sealed record StoredRound(
    long RoundId,
    string SessionId,
    string Stage,
    int Number,
    string Status,
    string Verdict,
    string Caller,
    string CallerVendor,
    string AuthorsSession,
    DateTime StartedUtc,
    string AuthorsState,
    string AuthorsIdSource,
    int AuthorsReader,
    int AuthorsAttribution,
    string SizeState,
    string BaseRef,
    string HeadSha);

/// <summary>What the session FILE holds for the round: whether it can hold a snapshot at all, and what is in it.</summary>
/// <param name="FileHasRound">The round is in the file's journal. A file that lacks it cannot hold its snapshot, whatever else it says.</param>
/// <param name="HasAuthors">The round's <c>Authors</c> member is present in the file (an older server strips it).</param>
/// <param name="PeerGates">The gate ids the snapshot was built against — UNKNOWN (not empty) when <see cref="IdSource"/> is <c>healed</c> or there is no snapshot.</param>
internal sealed record SnapshotPresence(
    SessionFileState FileState,
    bool FileHasRound,
    bool HasAuthors,
    string AuthorsState,
    int Reader,
    int Attribution,
    ImmutableArray<string> PeerGates,
    string IdSource,
    bool HasSize)
{
    /// <summary>The file exists, names this session and lists the round: the one case a snapshot can be saved into.</summary>
    public bool Holds => FileState == SessionFileState.Present && FileHasRound;

    /// <summary>A snapshot is there to read. A cell that says <see cref="HasAuthors"/> without <see cref="Holds"/> is read as no snapshot.</summary>
    public bool Has => Holds && HasAuthors;

    /// <summary>The file could hold a snapshot and has none — what an older server's save leaves behind.</summary>
    public bool Lost => Holds && !HasAuthors;

    public static SnapshotPresence NoFile { get; } = new(SessionFileState.Gone, false, false, string.Empty, 0, 0, [], string.Empty, false);
}

/// <summary>What the one peer lookup (<c>AuthorPeers.Find</c>) said, in the three facts the decision needs.</summary>
/// <param name="PeersKnown">The lookup completed. False when a peer's file did not load or the store could not answer — then NOTHING about the peers is proved.</param>
/// <param name="PeerIds">The gate ids found.</param>
/// <param name="ANamedPeerRoundStillRuns">A round of a gate the row names, overlapping this round's windows, is still <c>running</c>.</param>
internal sealed record PeerStatus(bool PeersKnown, ImmutableArray<string> PeerIds, bool ANamedPeerRoundStillRuns)
{
    public static PeerStatus Nobody { get; } = new(true, [], false);
}

/// <summary>Everything <see cref="BackfillDecision.Decide"/> reads about ONE round.</summary>
/// <param name="TranscriptLocated"><c>CallerTranscriptLocator.LocateStored</c> answered <c>Found</c> — named apart from E3's per-agent <c>TranscriptPresent</c>, which is a different fact.</param>
/// <param name="ProjectionAgrees">The snapshot's projection (the <c>authors_*</c> columns and the <c>round_authors</c> rows it would write) equals what the database holds. Read only where there is a snapshot.</param>
/// <param name="Since"><c>--since</c>; null selects every round.</param>
internal sealed record BackfillInput(
    StoredRound Round,
    SnapshotPresence Snapshot,
    bool TranscriptLocated,
    PeerStatus Peers,
    bool ProjectionAgrees,
    DateTime? Since);

/// <summary>What the backfill does to one round's authors.</summary>
internal enum BackfillVerb
{
    /// <summary>Not a settled plan or code round, or before <c>--since</c>: no snapshot, no state, no reason.</summary>
    NotSelected,

    /// <summary>Not now: counted, never recorded on the round, retried on the next run.</summary>
    Defer,

    /// <summary>Rows FROM the file's snapshot; the transcript is not read.</summary>
    Reproject,

    /// <summary>Nothing is written.</summary>
    Keep,

    /// <summary>The FILE gets a snapshot rebuilt from the database; the database is not touched.</summary>
    Heal,

    /// <summary>Read the transcript and attribute.</summary>
    Compute,
}

/// <summary>Why — the count a summary keeps; never a state on the round.</summary>
internal enum BackfillWhy
{
    None,
    FileUnreadable,
    PeersUnknown,
    TranscriptGone,
    NoFile,
    PeerStillRunning,
    EqualOrNewer,
    Computed,
    RecomputedProvisional,
    RecomputedSnapshotLost,

    // what a settled result or a write adds: the counts a summary keeps, never a state on the round
    StrongerExisting,
    Unchanged,
    Busy,
    ChangedUnderUs,
}

/// <summary>A verb with its reason.</summary>
internal readonly record struct BackfillAction(BackfillVerb Verb, BackfillWhy Why)
{
    public static BackfillAction Of(BackfillVerb verb, BackfillWhy why = BackfillWhy.None) => new(verb, why);
}

/// <summary>What is known once a computed or re-projected result exists: the second half of the decision.</summary>
/// <param name="DatabaseState">The state the database holds now.</param>
/// <param name="ResultState">The state the result would write.</param>
/// <param name="ProjectionEqualsDatabase">The result's projection is what the database already holds.</param>
/// <param name="FileLacksSnapshot">The file could hold a snapshot and has none (<see cref="SnapshotPresence.Lost"/>).</param>
/// <param name="ResultReader">The reader version of the result; absent means "not older than anything" — a result no version speaks against.</param>
/// <param name="ResultAttribution">The attribution version of the result; absent as <paramref name="ResultReader"/>.</param>
internal readonly record struct SettleInput(
    string DatabaseState,
    int DatabaseReader,
    int DatabaseAttribution,
    string ResultState,
    bool ProjectionEqualsDatabase,
    bool FileLacksSnapshot,
    int ResultReader = int.MaxValue,
    int ResultAttribution = int.MaxValue);

/// <summary>What happens to a computed or re-projected result.</summary>
internal enum Settlement
{
    /// <summary>The database projection and the file snapshot are written.</summary>
    Write,

    /// <summary>The same, and the row sat below <see cref="BackfillDecision.CorrectedBelow"/>: replaced whatever the strengths.</summary>
    WriteCorrected,

    /// <summary>The database already holds this; only the FILE lacks its snapshot, so only the file is written.</summary>
    WriteFileOnly,

    /// <summary>The result IS what the database holds and the file holds its snapshot: nothing is written.</summary>
    Unchanged,

    /// <summary>The result is weaker than what the database holds: the database is kept.</summary>
    KeepStronger,

    /// <summary>The same, and the file lacks its snapshot: it is healed from the database instead of staying empty.</summary>
    KeepStrongerAndHeal,
}

/// <summary>What the backfill does to one code round's size.</summary>
internal enum SizeVerb
{
    NotCode,
    Keep,
    Compute,
    NoBaseRecorded,
    NoHeadRecorded,
}

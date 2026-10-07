namespace CoaiMcp.Server;

/// <summary>
/// Every question id the sessions on disk hold — on a hold (<c>SessionState.HoldQuestions</c>) or as a feature session's
/// request (<c>SessionState.RequestQuestions</c>) — and whether that set is COMPLETE.
/// </summary>
/// <param name="Ids">The held ids, ordinal.</param>
/// <param name="Complete">
/// False when a session file could not be read (torn, busy under another writer's turn, not a session): its holds are
/// unknown, so a caller deciding to DELETE something must treat every id as possibly held.
/// </param>
public sealed record HeldQuestions(IReadOnlySet<string> Ids, bool Complete)
{
    public static HeldQuestions None { get; } = new(new HashSet<string>(StringComparer.Ordinal), true);

    /// <summary>Whether this id may be held: held, or unknown because a session could not be read.</summary>
    public bool MayHold(string id) => !Complete || Ids.Contains(id);
}

/// <summary>
/// Reads <see cref="HeldQuestions"/> from the sessions directory, parsing again only the session files that changed.
/// </summary>
/// <remarks>
/// <para><b>Why it exists</b> (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D1, 2026-10-06). The
/// escalation retention asked "does any session hold this id" once per escalation file on every one-minute beat, and
/// each answer deserialised every session file: 54 × 435 files (14 MB) a minute, per server, about 40 % of a core for
/// as long as an editor session lived. One read per sweep fixes the multiplication; this cache fixes the rest, because
/// a question held past its seven days is due on EVERY beat for as long as the hold stands.</para>
/// <para><b>Per file</b> (the code round, gemini, 2026-10-07): each session is cached under its own write time and
/// length — a stat per file, no read — so one active session, written on every round, costs one parse rather than all
/// of them. Two writes inside one timestamp tick that keep the exact size are not seen; the same trade
/// <see cref="PanelServiceHost"/> makes for the settings file, and here the cost of missing one is a hold recognised one
/// change late, never a deletion: a NEW hold can only bind an id whose file is not yet due.</para>
/// <para><b>Fails closed, without paying for it every beat.</b> A session that cannot be read makes the set
/// <c>Complete = false</c>; it is cached as unreadable under its stamp, so a torn file that STAYS torn is not parsed
/// again until it changes (own review of the branch, 2026-10-07). A file gone between the listing and its stat holds
/// nothing.</para>
/// </remarks>
internal sealed class SessionHolds(string sessionsDirectory, Func<string, PersistedSession?> read)
{
    private readonly Lock _gate = new();
    private Dictionary<string, Holds> _files = new(StringComparer.Ordinal);

    /// <summary>One session file as last parsed: its stamp, what it holds, and whether it could be read at all.</summary>
    private sealed record Holds(DateTime Written, long Length, IReadOnlyList<string> Ids, bool Readable);

    /// <summary>How many session files were parsed — what the idle-budget tests count.</summary>
    internal int Parses { get; private set; }

    public HeldQuestions Read()
    {
        lock (_gate)
        {
            if (!Directory.Exists(sessionsDirectory))
            {
                return HeldQuestions.None;
            }

            var next = new Dictionary<string, Holds>(StringComparer.Ordinal);
            foreach (var file in new DirectoryInfo(sessionsDirectory).EnumerateFiles("session-*.json"))
            {
                Kept(file, next);
            }

            _files = next;

            return new HeldQuestions(
                next.Values.SelectMany(holds => holds.Ids).ToHashSet(StringComparer.Ordinal),
                next.Values.All(holds => holds.Readable));
        }
    }

    /// <summary>The file's holds into <paramref name="next"/> — the cached ones when its stamp did not move.</summary>
    private void Kept(FileInfo file, Dictionary<string, Holds> next)
    {
        if (!TryStamp(file, out var written, out var length))
        {
            return; // gone since the listing: it holds nothing any more
        }

        next[file.FullName] = _files.TryGetValue(file.FullName, out var cached) && cached.Written == written && cached.Length == length
            ? cached
            : Parsed(file.FullName, written, length);
    }

    private Holds Parsed(string path, DateTime written, long length)
    {
        Parses++;

        return read(path) is { } session
            ? new(written, length, [.. session.State.HoldQuestions, .. session.State.RequestQuestions], Readable: true)
            : new(written, length, [], Readable: false);
    }

    private static bool TryStamp(FileInfo file, out DateTime written, out long length)
    {
        try
        {
            file.Refresh();
            (written, length) = (file.LastWriteTimeUtc, file.Length);

            return file.Exists;
        }
        catch (IOException)
        {
            (written, length) = (default, 0);

            return false;
        }
    }
}

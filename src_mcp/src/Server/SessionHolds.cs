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
/// again until it changes (own review of the branch, 2026-10-07). A file CONFIRMED gone between the listing and its stat
/// holds nothing; a stat that fails for any other reason keeps the set incomplete.</para>
/// </remarks>
internal sealed class SessionHolds(string sessionsDirectory, Func<string, PersistedSession?> read)
{
    private readonly Lock _gate = new();
    private Dictionary<string, Holds> _files = new(StringComparer.Ordinal);

    /// <summary>One session file as last parsed: its stamp, what it holds, and whether it could be read at all.</summary>
    private sealed record Holds(Stamp Stamp, IReadOnlyList<string> Ids, bool Readable);

    /// <summary>What a stat of one session file said: when it was written and how long it is, gone, or unknown.</summary>
    private abstract record Stamp
    {
        public sealed record Seen(DateTime Written, long Length) : Stamp;

        public sealed record Gone : Stamp;

        /// <summary>The stat failed for a reason other than absence — the file may hold anything (fail closed).</summary>
        public sealed record Unknown : Stamp;
    }

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
        var stamp = StampOf(file);
        if (stamp is Stamp.Gone)
        {
            return; // confirmed gone since the listing: it holds nothing any more
        }

        next[file.FullName] = Reusable(file.FullName, stamp) ?? Parsed(file.FullName, stamp);
    }

    /// <summary>The cached holds when the file's stamp is the one they were parsed under — or null, parse it.</summary>
    private Holds? Reusable(string path, Stamp stamp) =>
        stamp is Stamp.Seen seen && _files.TryGetValue(path, out var cached) && cached.Stamp == seen ? cached : null;

    private Holds Parsed(string path, Stamp stamp)
    {
        if (stamp is Stamp.Unknown)
        {
            return new(stamp, [], Readable: false); // its holds cannot be known, so the set is incomplete
        }

        Parses++;

        return read(path) is { } session
            ? new(stamp, [.. session.State.HoldQuestions, .. session.State.RequestQuestions], Readable: true)
            : new(stamp, [], Readable: false);
    }

    /// <summary>
    /// A stat of one file. Only a CONFIRMED absence is <see cref="Stamp.Gone"/>; any other failure is
    /// <see cref="Stamp.Unknown"/>, which keeps the set incomplete rather than reading as "holds nothing" (CodeRabbit on
    /// #690: a held card in a session that could not be stat'ed must not be deleted).
    /// </summary>
    private static Stamp StampOf(FileInfo file)
    {
        try
        {
            file.Refresh();

            return file.Exists ? new Stamp.Seen(file.LastWriteTimeUtc, file.Length) : new Stamp.Gone();
        }
        catch (FileNotFoundException)
        {
            return new Stamp.Gone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Stamp.Unknown();
        }
    }
}

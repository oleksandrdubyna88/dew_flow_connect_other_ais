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
/// Reads <see cref="HeldQuestions"/> from the sessions directory, and reads it AGAIN only when that directory changed.
/// </summary>
/// <remarks>
/// <para><b>Why it exists</b> (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D1, 2026-10-06). The
/// escalation retention asked "does any session hold this id" once per escalation file on every one-minute beat, and
/// each answer deserialised every session file: 54 × 435 files (14 MB) a minute, per server, about 40 % of a core for
/// as long as an editor session lived. One read per sweep fixes the multiplication; this cache fixes the rest, because
/// a question held past its seven days is due on EVERY beat for as long as the hold stands.</para>
/// <para><b>The stamp</b> is the directory listing's names, sizes and write times — a stat per file, no read. A session
/// written, added or removed moves it. Two writes inside one timestamp tick that keep the exact size are not seen; the
/// same trade <see cref="PanelServiceHost"/> makes for the settings file, and here the cost of missing one is a hold
/// recognised one change late, never a deletion: a NEW hold can only bind an id whose file is not yet due.</para>
/// <para><b>Fails closed.</b> A set read with an unreadable session is not cached and says <c>Complete = false</c>.</para>
/// </remarks>
internal sealed class SessionHolds(string sessionsDirectory, Func<string, PersistedSession?> read)
{
    private readonly Lock _gate = new();
    private (int Count, long Bytes, long Newest, long Mix) _stamp = (-1, 0, 0, 0);
    private HeldQuestions _held = HeldQuestions.None;

    /// <summary>How many times the session files were actually read — what the idle-budget tests count.</summary>
    internal int Reads { get; private set; }

    public HeldQuestions Read()
    {
        lock (_gate)
        {
            if (!Directory.Exists(sessionsDirectory))
            {
                return HeldQuestions.None;
            }

            var files = new DirectoryInfo(sessionsDirectory).EnumerateFiles("session-*.json").ToList();
            var stamp = Stamp(files);
            if (stamp == _stamp)
            {
                return _held;
            }

            var held = ReadAll(files);
            _stamp = held.Complete ? stamp : (-1, 0, 0, 0);
            _held = held;

            return held;
        }
    }

    private HeldQuestions ReadAll(IReadOnlyList<FileInfo> files)
    {
        Reads++;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var complete = true;
        foreach (var file in files)
        {
            if (read(file.FullName) is not { } session)
            {
                complete = false;
                continue;
            }

            ids.UnionWith(session.State.HoldQuestions);
            ids.UnionWith(session.State.RequestQuestions);
        }

        return new HeldQuestions(ids, complete);
    }

    private static (int, long, long, long) Stamp(IReadOnlyList<FileInfo> files)
    {
        long bytes = 0, newest = 0, mix = 0;
        foreach (var file in files)
        {
            var ticks = file.LastWriteTimeUtc.Ticks;
            bytes += file.Length;
            newest = Math.Max(newest, ticks);
            mix = unchecked(mix + (StringComparer.Ordinal.GetHashCode(file.Name) ^ ticks ^ file.Length));
        }

        return (files.Count, bytes, newest, mix);
    }
}

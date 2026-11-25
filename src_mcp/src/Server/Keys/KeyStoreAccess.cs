using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server.Keys;

/// <summary>
/// How the pool's callers reach one device's key store: the folder, the clock, where notices go, and the one place an operation takes the lock and turns a disk fault into a refusal.
/// </summary>
internal sealed class KeyStoreAccess(string dataDir, TimeProvider clock, Action<ServerNotice> notice, KeyFolderSeams? seams = null, bool sharedSetting = false)
{
    public KeyFolder Folder { get; } = new(dataDir, seams ?? KeyFolderSeams.Real, sharedSetting: sharedSetting);

    public DateTime NowUtc => clock.GetUtcNow().UtcDateTime;

    public void Notify(ServerNotice what) => notice(what);

    /// <summary>
    /// Runs <paramref name="work"/> with the pool held. A disk fault (the folder refuses, a file is held, a name is not allowed) is a refusal with exit code 74 and the system's own sentence —
    /// which names a path and never holds a key text; any other exception is a defect and flies on.
    /// </summary>
    public KeyResult<T> Run<T>(bool create, Func<KeySession, KeyResult<T>> work, TimeSpan? wait = null)
    {
        try
        {
            var opened = KeySession.Open(Folder, create, wait);
            if (opened.Value is not { } session)
            {
                return KeyResult<T>.Refused(opened.Refusal, opened.Code);
            }

            using (session)
            {
                return work(session);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return KeyResult<T>.Refused($"the key store could not be used: {e.Message}", KeyExit.Io);
        }
    }

    /// <summary>The same row a list gives, for one entry.</summary>
    public static KeyRow RowOf(KeyEntry entry, DateTime nowUtc) => new(
        entry.Record.Id,
        entry.Record.Kind,
        entry.Record.Label,
        entry.Record.State,
        entry.Record.DeadReason,
        entry.Record.Account,
        entry.Record.Source,
        entry.Record.AddedUtc,
        entry.Record.LastUsedUtc,
        entry.Record.CooldownUntilUtc,
        entry.Record.ConsecutiveCooldowns,
        entry.Record.Launches,
        entry.Record.LimitStops,
        entry.Record.AuthStops,
        entry.Record.Note,
        entry.Problem,
        entry.Usable(nowUtc));
}

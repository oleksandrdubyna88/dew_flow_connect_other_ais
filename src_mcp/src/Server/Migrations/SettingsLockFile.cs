namespace CoaiMcp.Server.Migrations;

/// <summary>
/// The extension's <c>settings.lock</c>, taken from the server's side — the lock every window holds around a read, a comparison and a write of
/// <c>settings.json</c> (<c>extension.ts</c>: <c>underSettingsLock</c>, <c>takeLock</c>, <c>breakIfStale</c>, <c>lockHolds</c>; <c>settingsLock.ts</c>).
/// </summary>
/// <remarks>
/// <para><b>The protocol, ported and not re-invented.</b> The lock is an exclusive CREATE of <c>&lt;data folder&gt;/settings.lock</c> whose content is
/// a token <c>pid:unixMs:guid</c>. A create that finds the file there means somebody holds it. ONE attempt is made to break a stale lock, never a
/// loop: a lock whose mtime is more than ten seconds old and not in the future (a clock that stepped back is NOT stale — this machine cannot tell
/// how old the lock is), whose token is the same when read again after the decision. The release deletes the file only while the token on disk is
/// still ours, so a lock broken under us is never deleted from under its successor. Before a rename over <c>settings.json</c> the writer asks
/// <see cref="Holds"/> once more, which narrows the window of a process suspended past the stale age to the instant between the check and the rename.</para>
/// <para>Nothing here waits. A lock that cannot be taken is <c>deferred</c> — the next start retries.</para>
/// </remarks>
internal sealed class SettingsLockFile : IDisposable
{
    /// <summary>The extension's <c>LOCK_STALE_AFTER_MS</c>: a read, a compare and a write of a few hundred bytes — ten seconds is four orders of slack.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    private readonly string _path;
    private readonly string _token;

    private SettingsLockFile(string path, string token)
    {
        _path = path;
        _token = token;
    }

    public static string FileFor(string dataDir) => Path.Combine(dataDir, "settings.lock");

    /// <summary>The extension's <c>lockIsStale</c>: older than the window, and never negative.</summary>
    public static bool IsStale(DateTime writtenUtc, DateTime nowUtc) => nowUtc - writtenUtc is { } age && age >= TimeSpan.Zero && age > StaleAfter;

    /// <summary>The lock, or null when somebody holds it (or the folder will not take it) — one attempt to break a stale one, no wait.</summary>
    public static SettingsLockFile? TryTake(string dataDir, TimeProvider clock)
    {
        var path = FileFor(dataDir);
        var token = $"{Environment.ProcessId}:{clock.GetUtcNow().ToUnixTimeMilliseconds()}:{Guid.NewGuid():N}";
        if (TryCreate(path, token))
        {
            return new SettingsLockFile(path, token);
        }

        return BreakIfStale(path, clock.GetUtcNow().UtcDateTime) && TryCreate(path, token) ? new SettingsLockFile(path, token) : null;
    }

    /// <summary>Whether the lock on disk is still the one this window took.</summary>
    public bool Holds()
    {
        try
        {
            return File.ReadAllText(_path) == _token;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Holds())
            {
                File.Delete(_path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a lock left behind goes stale in ten seconds, which is what the age is for
        }
    }

    private static bool TryCreate(string path, string token)
    {
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            file.Write(System.Text.Encoding.UTF8.GetBytes(token));

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // there already, or not ours to create: either way not taken
        }
    }

    private static bool BreakIfStale(string path, DateTime nowUtc)
    {
        try
        {
            var before = File.ReadAllText(path);
            if (!IsStale(File.GetLastWriteTimeUtc(path), nowUtc) || File.ReadAllText(path) != before)
            {
                return false;
            }

            File.Delete(path);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

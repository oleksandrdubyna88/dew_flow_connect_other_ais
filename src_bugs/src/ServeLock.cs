namespace CoaiBugs;

/// <summary>One server per data directory, enforced rather than assumed.</summary>
/// <remarks>
/// <para>The rate limiter is in memory and per process. Two servers on one data directory would
/// each see half the traffic and each admit the whole limit — twice the limit in total, silently.
/// The plan's "one process, one Kestrel" is therefore a lock file held for the server's lifetime:
/// a second server on the same directory refuses to start, with a sentence naming the first. The
/// one-shot modes do not take it — they are not servers, and a <c>--revoke</c> while the service runs
/// is exactly what the database's busy timeout exists for.</para>
/// <para><b>The atomic operation</b> is the operating system's exclusive open — <c>FileShare.None</c>,
/// which .NET maps to an exclusive <c>flock</c> on Linux. <b>The residual:</b> on Linux that lock is
/// advisory between processes that ask, so it stops a second <c>coai-bugs</c>, not a stranger with
/// <c>sqlite3</c>. A crash releases it with the handle, so there is no stale lock to sweep and no
/// heartbeat to keep.</para>
/// </remarks>
internal sealed class ServeLock : IDisposable
{
    /// <summary>The file, beside the database, whose exclusive open is the lock.</summary>
    public const string FileName = "coai-bugs.serving";

    private readonly FileStream _held;

    private ServeLock(FileStream held) => _held = held;

    /// <summary>Takes the lock, or says why it cannot.</summary>
    public static Taken Take(string dataDir)
    {
        var path = Path.Combine(dataDir, FileName);
        try
        {
            Directory.CreateDirectory(dataDir);

            return new Taken.Held(new ServeLock(
                new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)));
        }
        catch (IOException e)
        {
            // The one expected failure: another server holds it. Anything else — a directory that
            // cannot be created, a permission refused — is the same sentence with its own reason,
            // because the answer to all of them is "this server does not start".
            return new Taken.Refused(
                $"{path} is held by another coai-bugs, or cannot be taken ({e.Message}); one server "
                + "per data directory, because the rate limiter lives in that server's memory");
        }
        catch (UnauthorizedAccessException e)
        {
            return new Taken.Refused($"{path} cannot be taken ({e.Message})");
        }
    }

    public void Dispose() => _held.Dispose();

    /// <summary>What taking the lock came to.</summary>
    public abstract record Taken
    {
        private Taken()
        {
        }

        /// <summary>This process is the server. Dispose to let another be.</summary>
        public sealed record Held(ServeLock Lock) : Taken;

        /// <summary>Somebody else is, or the file could not be opened, and the sentence to print.</summary>
        public sealed record Refused(string Why) : Taken;
    }
}

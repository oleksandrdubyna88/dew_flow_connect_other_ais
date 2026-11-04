using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// Windows only: ONE sweep at a time over a temp root, by a named mutex held across the whole pass (E5 entry 5.1, consult e9d7aec0; the conventions' follow-ups plan F29).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> <c>Directory.Move</c> has no single winner on Windows: two threads racing one source both succeed in 39951 of 40000 races (hosted windows-latest, ci run
/// 41873954643), leaving one destination. So <see cref="LeftoversSweep"/>'s exclusive rename is not exclusive there, two sweeps at once double-report a folder and meet one that
/// is gone, and — by reading — a sweep that sees its claim deleted by the other restores the folder under its old name, unclaimed. POSIX's <c>rename</c> has one winner: unchanged.</para>
/// <para><b>What.</b> The name is <c>Global\coai-leftovers-sweep-&lt;sha256 of the real root, lower-cased&gt;</c>. The wait is 30 s; an <see cref="AbandonedMutexException"/> counts as
/// acquired (a sweep was killed holding it). A wait that times out, or a mutex that cannot be opened (<see cref="UnauthorizedAccessException"/>, <see cref="IOException"/>, …), means
/// NO sweep: the caller lists one line and returns — a sweep never throws and never fails the run. The mutex is released on the thread that took it, by <see cref="Dispose"/>.</para>
/// </remarks>
internal sealed class SweepLock : IDisposable
{
    /// <summary>How long a sweep waits for another one on the same root.</summary>
    internal static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    private Mutex? _mutex;

    private SweepLock(Mutex? mutex) => _mutex = mutex;

    /// <summary>The lock, or null when the sweep must not run (<paramref name="note"/> was given the one line to list). On POSIX: a lock that holds nothing.</summary>
    internal static SweepLock? Take(string tmp, TimeSpan? wait, Action<string> note)
    {
        if (!OperatingSystem.IsWindows()) return new SweepLock(null);

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, NameFor(tmp));
            try
            {
                if (!mutex.WaitOne(wait ?? DefaultWait))
                {
                    mutex.Dispose();
                    note("another sweep holds this temp root: nothing swept");
                    return null;
                }
            }
            catch (AbandonedMutexException)
            {
                // A sweep was killed while it held the mutex: this thread owns it now.
            }

            return new SweepLock(mutex);
        }
        catch (Exception ex)
        {
            mutex?.Dispose();
            note($"the sweep lock for this temp root could not be taken ({ex.GetType().Name}): nothing swept");
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    internal static string NameFor(string tmp)
    {
        var real = Native.FinalPathOfFollowing(tmp) ?? Path.GetFullPath(tmp);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(real.TrimEnd('\\', '/').ToLowerInvariant()));
        return @"Global\coai-leftovers-sweep-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null) return;
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread any more: nothing to release.
        }

        mutex.Dispose();
    }
}

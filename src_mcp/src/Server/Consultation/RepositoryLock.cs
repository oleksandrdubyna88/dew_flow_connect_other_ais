using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Server;

/// <summary>
/// One consultation per repository at a time — held from the first snapshot to the second.
/// </summary>
/// <remarks>
/// Two consultations on one working tree would see each other's output files, and a stale cache the
/// other's CLI touched, as breaches of the filesystem invariant — and fail each other closed for
/// nothing (gemini, Blocking, the plan round). A held <see cref="FileShare.None"/> handle in the data
/// directory is the <c>SessionTurn</c> shape: one process holds it, every other process is refused at
/// the door, and a holder that dies releases it with its handle. The wait is short and then a NAMED
/// refusal; a stuck agent that is told "another consultation is running here" can try again.
/// </remarks>
internal sealed class RepositoryLock : IDisposable
{
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    private readonly FileStream _held;

    private RepositoryLock(FileStream held) => _held = held;

    public static string PathFor(string dataDir, string repoPath) => Path.Combine(
        dataDir, "consultations", "locks",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(repoPath))))[..16] + ".lock");

    /// <summary>
    /// The lock, or null when nobody let go within the wait.
    /// </summary>
    /// <remarks>
    /// A wait of zero is a legitimate question rather than a degenerate case — "is anybody working in
    /// this repository right now" — and is what the startup sweep asks before deciding anything about
    /// a record.
    /// </remarks>
    public static async Task<RepositoryLock?> TryTakeAsync(string dataDir, string repoPath, TimeSpan wait, CancellationToken ct = default)
    {
        var path = PathFor(dataDir, repoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            if (TryOpen(path) is { } held)
            {
                return new RepositoryLock(held);
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(Poll, ct);
        }
    }

    private static FileStream? TryOpen(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The same repository spelled two ways must be one lock: case and trailing separators differ
    // between what a client sends and what git prints, and both reach here.
    //
    // Case is folded only where the FILESYSTEM folds it. On Linux `/work/Foo` and `/work/foo` are two
    // repositories, and lowercasing gave them one lock — so consulting about the second waited thirty
    // seconds and was refused by name while nothing was wrong. (CodeRabbit, on the pull request.)
    private static string Normalise(string repoPath)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoPath));

        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? full.ToLowerInvariant() : full;
    }

    public void Dispose() => _held.Dispose();
}

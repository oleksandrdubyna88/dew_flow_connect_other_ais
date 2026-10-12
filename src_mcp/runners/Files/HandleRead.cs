using System.Runtime.InteropServices;
using CoaiMcp.Runners.Worktrees;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

/// <summary>What reading a file by its handle came to: the bytes, or why none.</summary>
public abstract record FileRead
{
    private FileRead()
    {
    }

    public sealed record Served(byte[] Bytes) : FileRead;

    public sealed record Refused(string Reason) : FileRead;
}

/// <summary>
/// A root as it was when a reader was made: the path it is reached by, and the place the kernel says that path was
/// then. Made once, by <see cref="HandleRead.Trust"/>, and never asked again — a root renamed aside and replaced by a
/// link later is then a different place from this one, not a new truth.
/// </summary>
public sealed record ReadRoot(string Path, string Canonical);

/// <summary>
/// Reads a file FROM ITS OPENED HANDLE, after asking the handle what it really names — the one read both confined
/// readers go through (the snapshot's <c>ReviewSnapshot.Read</c> and the ACP boundary's <c>FolderReadBoundary.Read</c>).
/// </summary>
/// <remarks>
/// <para><b>The race this closes</b> (finding F of the risk consultations, 2026-10-12). Both readers walked the path
/// for links and then re-opened BY PATH: a link swapped in between the walk and the open was followed, and a file that
/// grew after its size was looked at was read whole. So the file is opened first; the OPENED handle is asked for its
/// final path and that path must be the expected entry under the root's real path, component by component; and the
/// bytes are read from that handle against the cap — at most <c>cap + 1</c> bytes are ever read, and one byte over is
/// a refusal, so there is no size check a growing file can slip past.</para>
/// <para><b>Per platform.</b> Windows: <c>GetFinalPathNameByHandleW</c>, and the root's own canonical path by the
/// same call on a directory handle, so an 8.3 spelling of the temp directory (<c>RUNNER~1</c> on a CI runner) compares
/// equal to its long form. Linux: the kernel's own answer, <c>readlink /proc/self/fd/N</c>, through
/// <see cref="FileSystemInfo.LinkTarget"/>. macOS: <c>F_GETPATH</c> is a variadic <c>fcntl</c>, which P/Invoke cannot call
/// safely on arm64, and <c>/dev/fd/N</c> is not a readable link — so the handle cannot be named there, and the
/// fallback is the path walked for links again AFTER the open, the root included, and then the opened file's identity
/// (device and inode, <see cref="MacFileIdentity"/>) compared with the path's: a link swapped in for the open and
/// removed again before the walk names a different inode, so it is refused too.</para>
/// <para><b>The root is fixed when the reader is made</b> (<see cref="ReadRoot"/>; the cadence consultation of epics
/// 4-5, 2026-10-12). Its canonical path used to be asked for AFTER the open: a root renamed aside and replaced by a
/// link in between then named the outside folder too, the outside file's handle matched it, and its bytes were
/// served.</para>
/// <para>Anything the platform cannot answer is a refusal: an unnamed handle is not known to be the file.</para>
/// </remarks>
public static partial class HandleRead
{
    /// <summary>4 MiB — a review's files are kilobytes; past this a file is a binary or a log, not a thing to send down a pipe.</summary>
    public const long DefaultCap = 4 * 1024 * 1024;

    private const int Chunk = 64 * 1024;

    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    /// The bytes of <paramref name="relative"/> (forward or native slashes) under <paramref name="root"/>, read from the
    /// handle that was opened and verified — or a refusal.
    /// </summary>
    /// <param name="cap">The most bytes served; one more is a refusal.</param>
    /// <param name="rule">How path components compare on this filesystem.</param>
    /// <summary>The root a reader serves from, fixed now: its path, and the place that path is at this moment.</summary>
    public static ReadRoot Trust(string root) => new(root, CanonicalDirectory(root));

    public static FileRead Read(ReadRoot root, string relative, long cap, StringComparison rule) =>
        Read(root, relative, cap, rule, static () => { });

    /// <param name="afterOpen">A seam for the tests, run between the open and the check of what was opened.</param>
    internal static FileRead Read(ReadRoot root, string relative, long cap, StringComparison rule, Action afterOpen)
    {
        var path = Path.Combine(root.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.None);
            afterOpen();
            var verified = Verified(stream.SafeFileHandle, root, relative, path, rule);

            return verified.Length > 0 ? new FileRead.Refused(verified) : Capped(stream, cap);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Named by type only: the sentence reaches an agent, and a system message can carry a path.
            return new FileRead.Refused($"the file could not be read ({e.GetType().Name})");
        }
    }

    /// <summary>Why the opened handle is not the expected entry under the root, or empty when it is.</summary>
    private static string Verified(SafeFileHandle handle, ReadRoot root, string relative, string path, StringComparison rule)
    {
        if (OperatingSystem.IsMacOS())
        {
            return WalkedAgain(handle, root.Path, relative, path);
        }

        var final = FinalPathOf(handle);
        if (final.Length == 0)
        {
            return "what was opened cannot be named by its handle on this platform, so nothing is served from it";
        }

        var expected = Path.Combine(root.Canonical, relative.Replace('/', Path.DirectorySeparatorChar));

        return SameByComponents(final, expected, rule)
            ? string.Empty
            : "what was opened is not the file that was asked for: a link appeared under the path, and it is not followed";
    }

    /// <summary>The macOS check: no link from the root down after the open, and the opened file IS the file the path names.</summary>
    private static string WalkedAgain(SafeFileHandle handle, string root, string relative, string path)
    {
        if (LinkFromTheRoot(root, relative))
        {
            return "a link is on the path at the moment of the read, and it is not followed";
        }

        return MacFileIdentity.SameEntry(handle, path)
            ? string.Empty
            : "what was opened is not the file the path names now: it was swapped, and it is not served";
    }

    private static bool LinkFromTheRoot(string root, string relative)
    {
        var at = Path.TrimEndingDirectorySeparator(root);
        if (IsLink(at))
        {
            return true;
        }

        foreach (var part in relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            at = Path.Combine(at, part);
            if (IsLink(at))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool SameByComponents(string final, string expected, StringComparison rule)
    {
        var a = final.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var b = expected.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        return a.Length == b.Length && a.Select((component, i) => string.Equals(component, b[i], rule)).All(same => same);
    }

    /// <summary>At most <paramref name="cap"/> bytes from the handle; one more is a refusal, and nothing past it is read.</summary>
    private static FileRead Capped(FileStream stream, long cap)
    {
        using var kept = new MemoryStream();
        var chunk = new byte[Chunk];
        long total = 0;
        while (true)
        {
            var wanted = (int)Math.Min(chunk.Length, cap + 1 - total);
            var read = stream.Read(chunk, 0, wanted);
            if (read == 0)
            {
                return new FileRead.Served(kept.ToArray());
            }

            total += read;
            if (total > cap)
            {
                return new FileRead.Refused($"over the {cap / (1024 * 1024)} MiB ceiling for one read");
            }

            kept.Write(chunk, 0, read);
        }
    }

    // ---------------------------------------------------------------------------------------
    // The handle's own name, per platform
    // ---------------------------------------------------------------------------------------

    /// <summary>The final path the kernel holds for <paramref name="handle"/>, or empty when this platform cannot say.</summary>
    private static string FinalPathOf(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsFinalPath(handle);
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxFinalPath(handle);
        }

        return string.Empty;
    }

    /// <summary>The root as the kernel spells it — the long, link-resolved form on Windows, <c>realpath</c>'s on Unix.</summary>
    private static string CanonicalDirectory(string root)
    {
        if (!OperatingSystem.IsWindows())
        {
            return WorktreePaths.Real(root);
        }

        // A directory handle needs FILE_FLAG_BACKUP_SEMANTICS, which no managed open sets; asked once per read, so
        // the temp directory's short spelling (RUNNER~1) and the handle's long one compare as the same place.
        using var directory = CreateFileW(root, FileReadAttributes, ShareAll, 0, OpenExisting, BackupSemantics, 0);
        var canonical = directory.IsInvalid ? string.Empty : WindowsFinalPath(directory);

        return canonical.Length > 0 ? canonical : WorktreePaths.Real(root);
    }

    private static string LinuxFinalPath(SafeFileHandle handle)
    {
        try
        {
            return new FileInfo($"/proc/self/fd/{handle.DangerousGetHandle().ToInt64()}").LinkTarget ?? string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string WindowsFinalPath(SafeFileHandle handle)
    {
        Span<char> buffer = stackalloc char[1024];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length)
        {
            // Zero is a failure; a length past the buffer is a path longer than any snapshot makes, and is refused rather than guessed.
            return string.Empty;
        }

        return WithoutDevicePrefix(new string(buffer[..(int)length]));
    }

    /// <summary><c>\\?\C:\x</c> is <c>C:\x</c>, and <c>\\?\UNC\server\share</c> is <c>\\server\share</c>.</summary>
    private static string WithoutDevicePrefix(string final) =>
        final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + final[@"\\?\UNC\".Length..]
        : final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[@"\\?\".Length..]
        : final;

    private const uint FileReadAttributes = 0x0080;
    private const uint ShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetFinalPathNameByHandleW(SafeFileHandle handle, Span<char> path, uint length, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}

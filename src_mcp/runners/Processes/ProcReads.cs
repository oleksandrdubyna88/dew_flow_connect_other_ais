using System.Runtime.InteropServices;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Processes;

/// <summary>The raw reads of <c>/proc</c> behind one seam: a test's fake is a dictionary, the product's is <see cref="ProcReads.Real"/>.</summary>
/// <remarks>The exception contracts are part of the interface — they are how "gone" and "could not be read" stay different answers.</remarks>
public interface IProcReads
{
    /// <summary>The numeric entries of <c>/proc</c>.</summary>
    IEnumerable<int> Pids();

    /// <summary>The text of <c>/proc/&lt;pid&gt;/stat</c>. Throws <see cref="FileNotFoundException"/> / <see cref="DirectoryNotFoundException"/> when the process is not there (gone),
    /// <see cref="UnauthorizedAccessException"/> / <see cref="IOException"/> for any other failure (unreadable).</summary>
    string StatText(int pid);

    /// <summary>The thread ids of <c>/proc/&lt;pid&gt;/task</c>; <c>[-1]</c> when the listing cannot be read — a thread nobody can rule out, so the entry is not called exited.</summary>
    IReadOnlyCollection<int> Threads(int pid);

    /// <summary>The uid that owns <c>/proc/&lt;pid&gt;</c> (<c>statx</c>, <c>STATX_UID</c>). Throws <see cref="IOException"/> on failure.</summary>
    uint OwnerUid(int pid);

    /// <summary><c>readlink</c> of <c>/proc/&lt;pid&gt;/exe</c>: the raw target (it ends in <c> (deleted)</c> once the file is unlinked), <c>""</c> for <c>ENOENT</c> (no executable: a kernel
    /// thread), and <see cref="UnauthorizedAccessException"/> for <c>EACCES</c> (another account's process), <see cref="IOException"/> for any other errno.</summary>
    string ExeTarget(int pid);

    /// <summary>
    /// The text of <c>/proc/&lt;pid&gt;/cmdline</c>: the arguments, each ended by NUL (empty for a kernel thread). Read when a same-account <c>exe</c> is denied — DL10's fall-back to <c>argv[0]</c>. Throws
    /// <see cref="FileNotFoundException"/> / <see cref="DirectoryNotFoundException"/> when the process is not there (gone), <see cref="UnauthorizedAccessException"/> / <see cref="IOException"/> for any other failure.
    /// </summary>
    string Cmdline(int pid);

    /// <summary>This process's effective uid (<c>geteuid</c>).</summary>
    uint OwnUid();
}

/// <summary>The real <c>/proc</c> — Linux only; every method but the three that call libc is plain file reading.</summary>
public sealed partial class ProcReads : IProcReads
{
    private const int NoEntry = 2;
    private const int NoSuchProcess = 3;
    private const int NoAccess = 13;
    private const int PathBuffer = 4096;

    private ProcReads()
    {
    }

    /// <summary>The one instance.</summary>
    public static ProcReads Real { get; } = new();

    public IEnumerable<int> Pids() =>
        Directory.EnumerateDirectories("/proc").Select(directory => (ok: int.TryParse(Path.GetFileName(directory), out var pid), pid)).Where(entry => entry.ok).Select(entry => entry.pid);

    public string StatText(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/stat");
        }
        catch (IOException) when (!Directory.Exists($"/proc/{pid}"))
        {
            // A process that exits while its stat is being read answers ESRCH from read(): it LEFT, which is not "could not be read".
            throw new DirectoryNotFoundException($"/proc/{pid} left while it was being read");
        }
    }

    public string Cmdline(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/cmdline");
        }
        catch (IOException) when (!Directory.Exists($"/proc/{pid}"))
        {
            throw new DirectoryNotFoundException($"/proc/{pid} left while it was being read");
        }
    }

    public IReadOnlyCollection<int> Threads(int pid)
    {
        try
        {
            return [.. Directory.EnumerateDirectories($"/proc/{pid}/task").Select(path => int.TryParse(Path.GetFileName(path), out var thread) ? thread : -1)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [-1];
        }
    }

    public uint OwnerUid(int pid)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("/proc is Linux's");
        }

        return LinuxStatx.OfPath($"/proc/{pid}", 0, LinuxStatx.StatxUid) is StatxRead.Read read
            ? read.Status.OwnerUid
            : throw new IOException($"the owner of /proc/{pid} could not be read");
    }

    public string ExeTarget(int pid)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("/proc is Linux's");
        }

        var buffer = new byte[PathBuffer];
        var length = Readlink($"/proc/{pid}/exe", buffer, buffer.Length);

        return length >= 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, (int)length) : Failed(Marshal.GetLastPInvokeError(), pid);
    }

    public uint OwnUid() => OperatingSystem.IsLinux() ? GetEffectiveUid() : throw new PlatformNotSupportedException("geteuid is Linux's");

    private static string Failed(int errno, int pid) => errno switch
    {
        NoEntry or NoSuchProcess => string.Empty,
        NoAccess => throw new UnauthorizedAccessException($"/proc/{pid}/exe: errno {errno} (EACCES)"),
        _ => throw new IOException($"/proc/{pid}/exe: errno {errno}"),
    };

    [LibraryImport("libc", EntryPoint = "readlink", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Readlink(string path, [Out] byte[] buffer, nint size);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUid();
}

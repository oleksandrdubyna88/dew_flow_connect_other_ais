using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// The INHERITABLE take of a lock file on Linux, native: <c>open</c> without <c>O_CLOEXEC</c>, then <c>flock(LOCK_EX | LOCK_NB)</c> — behind the unchanged <see cref="Hold"/> union (DL8 of
/// <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, A2 §3.4).
/// </summary>
/// <remarks>
/// <para><b>Why not .NET's.</b> On Unix .NET turns a share mode into an advisory <c>flock</c> and chooses <c>LOCK_EX</c> only when the share mode is exactly <see cref="FileShare.None"/>;
/// <c>FileShare.None | FileShare.Inheritable</c> is a SHARED lock there. L-N1 measured it on the ubuntu job (CI run 38923199105, <c>HeldFileInheritableOnLinuxTests</c>: a second inheritable take
/// answered "taken", not "busy") — two refreshers of a rotating token would both have been let in. So the inheritable take is asked of the kernel directly, and the descriptor is left to be inherited
/// by every process started while it is open, which is the property the tree hold wants (see <see cref="HeldFile"/>).</para>
/// <para><b>It interoperates with .NET's own takes.</b> A <see cref="FileStream"/> built over an existing handle takes no second lock, so .NET's non-inheritable probes (<c>LOCK_EX | LOCK_NB</c> through <c>flock</c>) refuse
/// against a native holder, and this take answers <c>EWOULDBLOCK</c> — <see cref="Hold.Busy"/> — against a .NET one. Both lock the same open-file description's file.</para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal static partial class LinuxHeldFile
{
    // The libc names, so a reader checks them against <fcntl.h>, <sys/stat.h>, <sys/file.h> and <errno.h> and not against a comment. No O_CLOEXEC: the child must inherit the descriptor.
    private const int O_RDWR = 0x0002;
    private const int O_CREAT = 0x0040;
    private const int O_NOCTTY = 0x0100;
    private const int S_IRUSR = 0x0100;
    private const int S_IWUSR = 0x0080;
    private const int LOCK_EX = 2;
    private const int LOCK_NB = 4;
    private const int ENOENT = 2;
    private const int EACCES = 13;
    private const int EWOULDBLOCK = 11;
    private const int EISDIR = 21;
    private const int ENAMETOOLONG = 36;

    /// <summary><c>open(path, O_RDWR|O_CREAT|O_NOCTTY, 0600)</c> and <c>flock(LOCK_EX|LOCK_NB)</c>: taken over a stream on the descriptor, <c>Busy</c> on <c>EWOULDBLOCK</c>, otherwise <c>Unusable</c> naming the errno.</summary>
    public static Hold TakeInheritable(string path) => TakeInheritable(path, handle => new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0));

    /// <summary>The same, with the stream over the descriptor behind a seam — a test makes its construction fail.</summary>
    internal static Hold TakeInheritable(string path, Func<SafeFileHandle, FileStream> stream)
    {
        var descriptor = Open(path, O_RDWR | O_CREAT | O_NOCTTY, S_IRUSR | S_IWUSR);
        if (descriptor < 0)
        {
            return new Hold.Unusable(Failure(path, Marshal.GetLastPInvokeError()));
        }

        return Flock(descriptor, LOCK_EX | LOCK_NB) == 0 ? Held(descriptor, stream) : Refused(descriptor, path, Marshal.GetLastPInvokeError());
    }

    /// <summary>The stream over the descriptor; when the stream cannot be made, the handle that owns the descriptor is disposed — the descriptor is closed exactly once, and the flock goes with it and the failure is the take's.</summary>
    private static Hold Held(int descriptor, Func<SafeFileHandle, FileStream> stream)
    {
        var handle = new SafeFileHandle(descriptor, ownsHandle: true);
        try
        {
            return new Hold.Taken(stream(handle));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            handle.Dispose();

            return new Hold.Unusable(e);
        }
    }

    private static Hold Refused(int descriptor, string path, int errno)
    {
        _ = Close(descriptor);

        return errno == EWOULDBLOCK ? new Hold.Busy() : new Hold.Unusable(Failure(path, errno));
    }

    /// <summary>The exception <see cref="HeldFile.Take"/> would have answered for the same refusal, where .NET names one; an <see cref="IOException"/> naming the errno otherwise.</summary>
    private static Exception Failure(string path, int errno) => errno switch
    {
        ENOENT => new DirectoryNotFoundException($"{path}: errno {errno} (ENOENT)"),
        EACCES or EISDIR => new UnauthorizedAccessException($"{path}: errno {errno}"),
        ENAMETOOLONG => new PathTooLongException($"{path}: errno {errno} (ENAMETOOLONG)"),
        _ => new IOException($"the lock file {path} cannot be taken: errno {errno}"),
    };

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags, int mode);

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(int descriptor, int operation);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);
}

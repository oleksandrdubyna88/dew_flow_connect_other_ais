using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// The INHERITABLE take of a lock file on Linux, native: <c>open</c> (with <c>O_CLOEXEC</c>, cleared only once the take has SUCCEEDED), then <c>flock(LOCK_EX | LOCK_NB)</c> — behind the unchanged
/// <see cref="Hold"/> union (DL8 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, A2 §3.4; the order is C26 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E5.md</c>, §3.5).
/// </summary>
/// <remarks>
/// <para><b>The order, and why (C26).</b> <c>open(O_CLOEXEC)</c> → <c>flock</c> → build the stream → <c>fcntl(F_SETFD, 0)</c> (CHECKED) on success; <c>flock(LOCK_UN)</c> → close on the ABORTED path; NO unlock when a taken
/// hold is later disposed. The descriptor is inheritable only from the moment the take has succeeded, so a child another thread forks AND execs while the take is still open (or fails) closes its copy at exec. A child
/// that was forked but has not exec'd yet still holds a copy of the open file description — and the flock belongs to the DESCRIPTION, not the descriptor — so the aborted path unlocks explicitly before it closes.
/// A successful hold is never unlocked here: the lock is what the inheriting child (Kimi) keeps. The production taker is the <c>--ask-acp</c> shim alone, which starts one child right after the take, so
/// the window in which an unrelated fork can inherit a SUCCESSFUL hold does not exist there.</para>
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
    // The libc names, so a reader checks them against <fcntl.h>, <sys/stat.h>, <sys/file.h> and <errno.h> and not against a comment. O_CLOEXEC is set at the open and cleared (F_SETFD, 0) once the take has succeeded:
    // the child must inherit a TAKEN hold, and nothing else. (The values are Linux's generic ones: x86-64 and arm64.)
    private const int O_RDWR = 0x0002;
    private const int O_CREAT = 0x0040;
    private const int O_NOCTTY = 0x0100;
    private const int O_CLOEXEC = 0x80000;
    private const int F_SETFD = 2;
    private const int LOCK_UN = 8;
    private const int S_IRUSR = 0x0100;
    private const int S_IWUSR = 0x0080;
    private const int LOCK_EX = 2;
    private const int LOCK_NB = 4;
    private const int ENOENT = 2;
    private const int EACCES = 13;
    private const int EWOULDBLOCK = 11;
    private const int EISDIR = 21;
    private const int ENAMETOOLONG = 36;
    private const int EINTR = 4;

    /// <summary>How many times the unlock of an aborted take is asked when the kernel keeps interrupting it (<c>EINTR</c>) — bounded, so a stand-in that always answers it cannot loop for ever.</summary>
    internal const int UnlockAttempts = 5;

    /// <summary><c>open(path, O_RDWR|O_CREAT|O_NOCTTY|O_CLOEXEC, 0600)</c> and <c>flock(LOCK_EX|LOCK_NB)</c>: taken over a stream on the descriptor and made inheritable, <c>Busy</c> on <c>EWOULDBLOCK</c>, otherwise <c>Unusable</c> naming the errno.</summary>
    public static Hold TakeInheritable(string path) => TakeInheritable(path, handle => new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0));

    /// <summary>The same, with the stream over the descriptor behind a seam — a test makes its construction fail.</summary>
    internal static Hold TakeInheritable(string path, Func<SafeFileHandle, FileStream> stream) => TakeInheritable(path, stream, ClearCloseOnExec);

    /// <summary>The same, with the clearing of <c>FD_CLOEXEC</c> behind a second seam: the errno it answers (0 when it worked) — a test makes it fail.</summary>
    internal static Hold TakeInheritable(string path, Func<SafeFileHandle, FileStream> stream, Func<int, int> clearCloseOnExec) => TakeInheritable(path, stream, clearCloseOnExec, Unlock);

    /// <summary>The same, with the explicit unlock of the ABORTED path behind a third seam: the errno it answers (0 when it worked) — a test makes it fail, or fail once with <c>EINTR</c>.</summary>
    internal static Hold TakeInheritable(string path, Func<SafeFileHandle, FileStream> stream, Func<int, int> clearCloseOnExec, Func<int, int> unlock)
    {
        var descriptor = Open(path, O_RDWR | O_CREAT | O_NOCTTY | O_CLOEXEC, S_IRUSR | S_IWUSR);
        if (descriptor < 0)
        {
            return new Hold.Unusable(Failure(path, Marshal.GetLastPInvokeError()));
        }

        return Flock(descriptor, LOCK_EX | LOCK_NB) == 0 ? Held(descriptor, path, stream, clearCloseOnExec, unlock) : Refused(descriptor, path, Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// The stream over the descriptor, then the descriptor made inheritable. When either cannot be done the take is ABORTED: the lock is released explicitly (<c>LOCK_UN</c> acts on the open file description, so a copy of
    /// the descriptor in a child that has forked but not yet exec'd no longer holds it), THEN the descriptor is closed — exactly once, by the handle that owns it. A successful hold is never unlocked.
    /// </summary>
    private static Hold Held(int descriptor, string path, Func<SafeFileHandle, FileStream> stream, Func<int, int> clearCloseOnExec, Func<int, int> unlock)
    {
        var handle = new SafeFileHandle(descriptor, ownsHandle: true);
        FileStream made;
        try
        {
            made = stream(handle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Aborted(descriptor, handle, new Hold.Unusable(e), unlock);
        }

        var errno = clearCloseOnExec(descriptor);

        return errno == 0
            ? new Hold.Taken(made)
            : Aborted(descriptor, made, new Hold.Unusable(new IOException($"the lock file {path} cannot be made inheritable: fcntl(F_SETFD) failed with errno {errno}")), unlock);
    }

    /// <summary>Unlock the open file description, then close the descriptor (the owner disposes): the file is free the moment this returns, whoever else holds a copy of the descriptor.</summary>
    private static Hold Aborted(int descriptor, IDisposable owner, Hold failure, Func<int, int> unlock)
    {
        _ = unlock(descriptor);
        owner.Dispose();

        return failure;
    }

    /// <summary><c>fcntl(descriptor, F_SETFD, 0)</c>: the errno when it failed, 0 when it worked — read at once, before anything else can change it.</summary>
    private static int ClearCloseOnExec(int descriptor) => Fcntl(descriptor, F_SETFD, 0) == 0 ? 0 : Marshal.GetLastPInvokeError();

    /// <summary><c>flock(descriptor, LOCK_UN)</c>: the errno when it failed, 0 when it worked — read at once, before anything else can change it.</summary>
    internal static int Unlock(int descriptor) => Flock(descriptor, LOCK_UN) == 0 ? 0 : Marshal.GetLastPInvokeError();

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

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int descriptor, int command, int argument);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);
}

// Ported from wsl_care (MIT, same copyright holder): src_daemon/src/WslCare.Core/Files/RegularFiles.cs @ 0a5c72d3 — the Linux leg
// only (`ReadOwned` → `ReadLinuxOwned` → `ReadOpened` → `Judged` → `Capped`, `OwnershipProblem`, `OwnedErrno`, `Describe`, the
// native block), measured there and re-homed here with its tests (src_mcp/tests/RegularFilesTests.cs). DL2 of
// research/PLAN_kimi_and_vibe_on_linux.md; research/module_runners.md says what was left.
//
// NOT ported: Read, ReadNoFollow, ReadOther*, ReadHead/Head, StatNoFollow, MayExecute, LinkErrno, MakeReadable, HomeFileOwner and
// BeneathFiles. Nothing here walks a folder chain: the chain above ~/.vibe/.env is the person's own home, which coai admits for
// Kimi's sign-in too (Junctions.Resolved); only the FINAL component is coai's to judge.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// Reading a file a person NAMED — their sign-in — only when it is a REGULAR file their own account alone may write, and never past a
/// hard byte cap, whatever its length claims. A symbolic link, a directory, a FIFO, a socket or a device is refused with the reason, and
/// is never waited on: a FIFO with no writer would hold a plain open for ever, and a device could stream for ever.
/// </summary>
/// <remarks>
/// <para>The file is opened with <c>O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC</c> (a final link answers <c>ELOOP</c> and is never followed; the
/// open cannot block on a FIFO), its type, owner and mode are asked of the OPEN descriptor in one <c>statx(fd, "", AT_EMPTY_PATH)</c>
/// — <c>struct statx</c> has one layout on every architecture — and the bytes are read from the same descriptor. Asking the descriptor,
/// never the path, leaves no window for a swap between the judgement and the read (I2 of the A1 spec).</para>
/// <para><b>The residual, stated where the code reads it:</b> coai's judgement and Vibe's read are two OPENS. The mode rule leaves only
/// the person's own account (or root) able to swap the file between them; coai cannot close that window and does not claim to.</para>
/// </remarks>
public static partial class RegularFiles
{
    public const string NotRegular = "not a regular file";

    /// <summary>What a non-Linux host answers: the judgement is a Linux descriptor's, and nothing here pretends otherwise.</summary>
    public const string LinuxOnly = "judged through a descriptor on Linux only";

    /// <summary>
    /// The file at <paramref name="path"/>: only when it is regular, owned by <paramref name="owner"/> and not group- or other-writable,
    /// at most <paramref name="maxBytes"/> bytes (one more is refused), all asked of one descriptor. Never throws for a file's state.
    /// </summary>
    public static FileReadResult ReadOwned(string path, int maxBytes, uint owner)
    {
        return Guarded(() => OperatingSystem.IsLinux() ? ReadLinuxOwned(path, maxBytes, owner) : new FileReadResult.Unreadable(LinuxOnly));
    }

    /// <summary>Runs one read and turns what a file's state or this libc can throw into a result — a seam, so a host with no <c>statx</c> is a test.</summary>
    internal static FileReadResult Guarded(Func<FileReadResult> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return new FileReadResult.Unreadable(NoStatx);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return e is FileNotFoundException or DirectoryNotFoundException ? new FileReadResult.Missing() : new FileReadResult.Unreadable(e.Message);
        }
    }

    /// <summary>glibc before 2.28 has no <c>statx</c>: the judgement cannot be made, so there is no link — and the reason says so.</summary>
    public const string NoStatx = "statx is not available in this libc — Vibe on this distribution is unmeasured";

    /// <summary>This process's effective uid on Linux — whose sign-in coai links; 0 elsewhere.</summary>
    public static uint EffectiveUid() => OperatingSystem.IsLinux() ? Native.GetEffectiveUid() : 0;

    /// <summary>Why a file with this owner and mode may not be trusted; empty when it may (pure, so it is a unit test on any host).</summary>
    public static string OwnershipProblem(uint fileOwner, int mode, uint owner) =>
        fileOwner != owner ? $"owned by uid {fileOwner}, not uid {owner}"
        : (mode & Native.GroupOrOtherWrite) != 0 ? $"writable by group or others (mode {Convert.ToString(mode & 0xFFF, 8)})"
        : string.Empty;

    [SupportedOSPlatform("linux")]
    private static FileReadResult ReadLinuxOwned(string path, int maxBytes, uint owner)
    {
        var fd = Native.Open(path, Native.OpenReadOnlyNonBlocking | Native.NoFollow, 0);

        return fd < 0 ? OwnedErrno(Marshal.GetLastPInvokeError()) : ReadOpened(fd, maxBytes, owner);
    }

    /// <summary>A descriptor opened without following a link, judged (type, owner, mode) and read up to the cap; the descriptor is closed here.</summary>
    [SupportedOSPlatform("linux")]
    private static FileReadResult ReadOpened(int fd, int maxBytes, uint owner)
    {
        using var handle = new SafeFileHandle(fd, ownsHandle: true);
        if (Judged(fd, owner) is { Length: > 0 } problem)
        {
            return new FileReadResult.Unreadable(problem);
        }

        using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 0);

        return Capped(stream, maxBytes);
    }

    /// <summary>The descriptor's type, owner and mode judged from one <c>statx</c>; empty when the file may be read.</summary>
    [SupportedOSPlatform("linux")]
    private static string Judged(int fd, uint owner)
    {
        var buffer = new byte[FileStatus.StatxSize];
        if (Native.Statx(fd, string.Empty, Native.AtEmptyPath, Native.StatxType | Native.StatxMode | Native.StatxUid, buffer) != 0)
        {
            return $"its type and owner could not be read (errno {Marshal.GetLastPInvokeError()})";
        }

        var status = FileStatus.FromStatx(buffer);

        return status.IsRegular ? OwnershipProblem(status.OwnerUid, status.Permissions, owner) : $"{NotRegular} ({Describe(status.Type)})";
    }

    /// <summary>
    /// What a failed <c>open</c> means. <c>ELOOP</c> is a final symbolic link, never followed; <c>ENOENT</c> and <c>ENOTDIR</c> are a file
    /// that is not there (the person has not signed in); <c>ENXIO</c> is a socket. The errno NUMBER, never <c>strerror</c> text.
    /// </summary>
    internal static FileReadResult OwnedErrno(int errno) => errno switch
    {
        Native.TooManyLinks => new FileReadResult.Unreadable($"a symbolic link, never followed (errno {errno})"),
        Native.AccessDenied or Native.NotPermitted => new FileReadResult.Unreadable($"no read permission (errno {errno})"),
        Native.NameTooLong => new FileReadResult.Unreadable($"path too long (errno {errno})"),
        Native.NoEntry or Native.NotADirectory => new FileReadResult.Missing(),
        Native.NoSuchDevice => new FileReadResult.Unreadable($"{NotRegular} (a socket)"),
        _ => new FileReadResult.Unreadable($"could not be opened (errno {errno})"),
    };

    /// <summary>The bytes of an open stream, at most <paramref name="maxBytes"/> — or the refusal when there is more. Counts bytes READ, never the length a stream claims.</summary>
    internal static FileReadResult Capped(Stream stream, int maxBytes)
    {
        var buffer = new byte[maxBytes + 1];
        try
        {
            var total = 0;
            int read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += read;
            }

            return total > maxBytes ? new FileReadResult.Unreadable(FileReadResult.TooLarge(maxBytes)) : new FileReadResult.Content(buffer[..total]);
        }
        finally
        {
            // The array the stream filled may hold a credential: the copy handed out is the caller's to zero, this one is ours.
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static string Describe(int type) => type switch
    {
        0x4000 => "a directory",
        0x1000 => "a FIFO",
        0xC000 => "a socket",
        0x2000 or 0x6000 => "a device",
        0xA000 => "a symbolic link",
        _ => "an unknown kind of file",
    };

    /// <summary>
    /// glibc, by coai's one spelling (<c>"libc"</c>, as <c>AppendOnlyFile</c>'s). The constants are the same on x86-64 and arm64 except
    /// <c>O_NOFOLLOW</c>: <c>O_NONBLOCK</c> 0x800, <c>O_CLOEXEC</c> 0x80000, <c>O_NOCTTY</c> 0x100 (<c>O_RDONLY</c> is 0),
    /// <c>AT_EMPTY_PATH</c> 0x1000, <c>STATX_TYPE</c> 1, <c>STATX_MODE</c> 2, <c>STATX_UID</c> 8, <c>EPERM</c> 1, <c>ENOENT</c> 2, <c>ENXIO</c> 6, <c>EACCES</c> 13,
    /// <c>ENOTDIR</c> 20, <c>ENAMETOOLONG</c> 36, <c>ELOOP</c> 40.
    /// </summary>
    private static partial class Native
    {
        public const int OpenReadOnlyNonBlocking = 0x800 | 0x80000 | 0x100;
        public const int AtEmptyPath = 0x1000;
        public const uint StatxType = 1;
        public const uint StatxMode = 2;
        public const uint StatxUid = 8;

        /// <summary><c>S_IWGRP | S_IWOTH</c>.</summary>
        public const int GroupOrOtherWrite = 0x12;

        public const int NotPermitted = 1;
        public const int NoEntry = 2;
        public const int AccessDenied = 13;
        public const int NameTooLong = 36;
        public const int NoSuchDevice = 6;
        public const int NotADirectory = 20;
        public const int TooManyLinks = 40;

        private const string Libc = "libc";

        /// <summary><c>O_NOFOLLOW</c> differs by architecture: 0x20000 on x86-64, 0x8000 on arm64 (its own uapi fcntl.h).</summary>
        public static int NoFollow => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0x8000 : 0x20000;

        [LibraryImport(Libc, EntryPoint = "geteuid")]
        internal static partial uint GetEffectiveUid();

        [LibraryImport(Libc, EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Open(string path, int flags, int mode);

        [LibraryImport(Libc, EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Statx(int dirfd, string path, int flags, uint mask, byte[] buffer);
    }
}

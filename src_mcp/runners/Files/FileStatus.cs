using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Runners.Files;

// Ported from wsl_care (src_daemon/src/WslCare.Core/Files/RegularFiles.cs, the `FileStatus` record and `SameFileAs` at :12-21, the
// `Status` offsets at :238-244, the `statx` import at :381-382): same owner, MIT. Only what DL7 of todo/PLAN_kimi_and_vibe_on_linux.md
// needs for a folder's identity key comes over — the record, the read and its offsets. wsl_care's regular-file READER (the O_NONBLOCK
// open, the capped read) is DL2's port, in its own file, and is not here.

/// <summary>What <c>statx</c> says about one file: its type and permission bits (from <c>stx_mode</c>), its owner, its inode
/// and the device it lives on.</summary>
public sealed record FileStatus(int Type, int Permissions, uint OwnerUid, ulong Inode, uint DeviceMajor, uint DeviceMinor)
{
    public bool IsRegular => Type == 0x8000;

    public bool IsDirectory => Type == 0x4000;

    public bool IsSymbolicLink => Type == 0xA000;

    /// <summary>The same file: one device, one inode.</summary>
    public bool SameFileAs(FileStatus other) => Inode == other.Inode && DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor;

    /// <summary>The 256-byte <c>struct statx</c>, which has ONE layout on every Linux architecture (unlike <c>struct stat</c>):
    /// <c>stx_uid</c> (u32 at 20), <c>stx_mode</c> (u16 at 28: type and permission bits), <c>stx_ino</c> (u64 at 32),
    /// <c>stx_dev_major</c> / <c>stx_dev_minor</c> (u32 at 136 / 140). The fields are the machine's own byte order, as the kernel wrote them.</summary>
    public static FileStatus FromStatx(ReadOnlySpan<byte> statx)
    {
        var mode = BitConverter.ToUInt16(statx[StatxFile.ModeOffset..]);

        return new FileStatus(
            mode & StatxFile.TypeMask,
            mode & 0xFFF,
            BitConverter.ToUInt32(statx[StatxFile.UidOffset..]),
            BitConverter.ToUInt64(statx[StatxFile.InodeOffset..]),
            BitConverter.ToUInt32(statx[StatxFile.DeviceMajorOffset..]),
            BitConverter.ToUInt32(statx[StatxFile.DeviceMinorOffset..]));
    }
}

/// <summary>What asking the kernel about one path produced — a closed union, so "no such file" and "could not be asked" are never one answer.</summary>
public abstract record StatResult
{
    private StatResult()
    {
    }

    /// <summary>The kernel answered, and filled in the inode: a file identity.</summary>
    public sealed record Found(FileStatus Status) : StatResult;

    /// <summary>The call failed with this <c>errno</c> (<see cref="StatxFile.NoEntry"/> is a path that is not there), or answered without an inode (errno 0).</summary>
    public sealed record Failed(int Errno) : StatResult;
}

/// <summary>
/// <c>statx(AT_FDCWD, path, …)</c> — the only call here that names a folder by what it IS rather than how it is spelled. Linux only:
/// the kernel's <c>(st_dev, st_ino)</c> is one value for a symlinked spelling, a bind-mounted one and a <c>/proc/self/cwd</c> one.
/// </summary>
/// <remarks>
/// The constants are the same on x86-64 and arm64: <c>AT_FDCWD</c> -100, <c>AT_SYMLINK_NOFOLLOW</c> 0x100, <c>STATX_INO</c> 0x100,
/// <c>STATX_BASIC_STATS</c> 0x7ff, <c>ENOENT</c> 2. The mask is asked broadly and the ANSWER's <c>stx_mask</c> is read: a filesystem that
/// did not fill the inode is a failure, never an identity of zero.
/// </remarks>
public static partial class StatxFile
{
    internal const int StatxSize = 256;
    internal const int UidOffset = 20;
    internal const int ModeOffset = 28;
    internal const int InodeOffset = 32;
    internal const int DeviceMajorOffset = 136;
    internal const int DeviceMinorOffset = 140;
    internal const int TypeMask = 0xF000;

    /// <summary><c>ENOENT</c>: the path names nothing.</summary>
    public const int NoEntry = 2;

    private const int AtFdCwd = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxBasic = 0x7FF;
    private const uint StatxIno = 0x100;

    /// <summary>The file <paramref name="path"/> names, a final symbolic link FOLLOWED — so every spelling of one folder is one identity.</summary>
    [SupportedOSPlatform("linux")]
    public static StatResult Of(string path) => Ask(path, flags: 0);

    /// <summary>The status of <paramref name="path"/> itself: a final symbolic link is described, never followed.</summary>
    [SupportedOSPlatform("linux")]
    public static StatResult OfLink(string path) => Ask(path, AtSymlinkNoFollow);

    [SupportedOSPlatform("linux")]
    private static StatResult Ask(string path, int flags)
    {
        Span<byte> buffer = stackalloc byte[StatxSize];

        return Statx(AtFdCwd, path, flags, StatxBasic, buffer) == 0
            ? Answered(buffer)
            : new StatResult.Failed(Marshal.GetLastPInvokeError());
    }

    /// <summary>The answer, when the kernel filled the inode (the first u32 of the struct is the mask of what it filled); else a failure of its own.</summary>
    internal static StatResult Answered(ReadOnlySpan<byte> statx) =>
        (BitConverter.ToUInt32(statx) & StatxIno) == 0 ? new StatResult.Failed(0) : new StatResult.Found(FileStatus.FromStatx(statx));

    [LibraryImport(LibcLibrary.Name, EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int dirfd, string path, int flags, uint mask, Span<byte> buffer);
}

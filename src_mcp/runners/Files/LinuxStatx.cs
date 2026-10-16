using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

// Ported from wsl_care (MIT, same owner) RegularFiles.cs:238-244 (`Status`) and :327-383 (the `Native` constants and the `statx` import, only what `statx`
// needs). glibc by its short name "libc" — the spelling of src_mcp/service_defaults/AppendOnlyFile.cs:374-383 — not wsl_care's "libc.so.6".

/// <summary>What asking the kernel with <c>statx</c> produced — a closed union, so "the call failed" is never a status.</summary>
public abstract record StatxRead
{
    private StatxRead()
    {
    }

    /// <summary>The kernel answered, and filled in every field the request asked for that this reader trusts.</summary>
    public sealed record Read(FileStatus Status) : StatxRead;

    /// <summary>The call failed with this <c>errno</c> (<see cref="LinuxStatx.NoEntry"/>: the path names nothing), or answered without a field it was asked for (errno 0).</summary>
    public sealed record Failed(int Errno) : StatxRead;
}

/// <summary>
/// The ONE <c>statx</c> import and decoder for the runners. <c>struct statx</c> has one layout on every Linux architecture (unlike <c>struct stat</c>), so
/// the same offsets read it on x86-64 and arm64. The constants are the same on both: <c>AT_FDCWD</c> -100, <c>AT_EMPTY_PATH</c> 0x1000,
/// <c>AT_SYMLINK_NOFOLLOW</c> 0x100, <c>STATX_TYPE</c> 1, <c>STATX_MODE</c> 2, <c>STATX_UID</c> 8, <c>STATX_INO</c> 0x100, <c>STATX_BASIC_STATS</c> 0x7ff, <c>ENOENT</c> 2.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class LinuxStatx
{
    public const int AtFdCwd = -100;
    public const int AtEmptyPath = 0x1000;
    public const int AtSymlinkNoFollow = 0x100;
    public const uint StatxType = 1;
    public const uint StatxMode = 2;
    public const uint StatxUid = 8;
    public const uint StatxIno = 0x100;
    public const uint StatxBasic = 0x7FF;
    public const int StatxSize = 256;

    /// <summary><c>ENOENT</c>.</summary>
    public const int NoEntry = 2;

    private const int MaskOffset = 0;
    private const int UidOffset = 20;
    private const int ModeOffset = 28;
    private const int InodeOffset = 32;
    private const int DeviceMajorOffset = 136;
    private const int DeviceMinorOffset = 140;
    private const int TypeMask = 0xF000;

    /// <summary><c>statx</c> of a PATH (a final link followed unless <paramref name="flags"/> say otherwise): the status, or the errno.</summary>
    public static StatxRead OfPath(string path, int flags, uint mask)
    {
        var buffer = new byte[StatxSize];

        return Statx(AtFdCwd, path, flags, mask, buffer) == 0 ? Answered(buffer, mask) : new StatxRead.Failed(Marshal.GetLastPInvokeError());
    }

    /// <summary><c>statx</c> of an OPEN descriptor (<c>AT_EMPTY_PATH</c>): the status, or the errno. A reference is held across the call so the number cannot be closed and reused under it.</summary>
    public static StatxRead OfHandle(SafeFileHandle handle, uint mask)
    {
        var held = false;
        try
        {
            handle.DangerousAddRef(ref held);
            var buffer = new byte[StatxSize];
#pragma warning disable S3869 // the descriptor's NUMBER is what statx takes; the reference above keeps it ours for the call
            var answered = Statx((int)handle.DangerousGetHandle(), string.Empty, AtEmptyPath, mask, buffer);
#pragma warning restore S3869

            return answered == 0 ? Answered(buffer, mask) : new StatxRead.Failed(Marshal.GetLastPInvokeError());
        }
        finally
        {
            if (held)
            {
                handle.DangerousRelease();
            }
        }
    }

    /// <summary>The answer, unless the kernel did not fill the inode it was asked for (the first u32 of the struct is the mask of what it filled): an inode of zero is not an identity.</summary>
    internal static StatxRead Answered(byte[] statx, uint asked) =>
        (asked & StatxIno) != 0 && (BitConverter.ToUInt32(statx, MaskOffset) & StatxIno) == 0 ? new StatxRead.Failed(0) : new StatxRead.Read(Status(statx));

    /// <summary><c>stx_mode</c> (u16 at 28: type and permission bits), <c>stx_uid</c> (u32 at 20), <c>stx_ino</c> (u64 at 32), <c>stx_dev_major</c> / <c>stx_dev_minor</c> (u32 at 136 / 140),
    /// in the machine's own byte order, as the kernel wrote them.</summary>
    internal static FileStatus Status(byte[] statx)
    {
        var mode = BitConverter.ToUInt16(statx, ModeOffset);

        return new FileStatus(mode & TypeMask, mode & 0xFFF, BitConverter.ToUInt32(statx, UidOffset), BitConverter.ToUInt64(statx, InodeOffset), BitConverter.ToUInt32(statx, DeviceMajorOffset), BitConverter.ToUInt32(statx, DeviceMinorOffset));
    }

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int dirfd, string path, int flags, uint mask, [Out] byte[] buffer);
}

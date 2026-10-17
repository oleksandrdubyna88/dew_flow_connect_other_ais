// Ported from wsl_care (MIT, same copyright holder): src_daemon/src/WslCare.Core/Files/RegularFiles.cs @ 0a5c72d3 — `FileStatus`
// (lines 10-22) and the offsets of `Status` (lines 238-244). Shipped with the sign-in reader because DL7 (the Kimi lock key,
// E5) reuses it: one device and one inode name a folder. The sign-in judgement uses only Type, Permissions and OwnerUid.
using System.Buffers.Binary;

namespace CoaiMcp.Runners.Files;

/// <summary>What <c>statx</c> says about one file: its type and permission bits (from <c>stx_mode</c>), its owner, its inode and the device it lives on.</summary>
public sealed record FileStatus(int Type, int Permissions, uint OwnerUid, ulong Inode, uint DeviceMajor, uint DeviceMinor)
{
    public bool IsRegular => Type == 0x8000;

    public bool IsDirectory => Type == 0x4000;

    public bool IsSymbolicLink => Type == 0xA000;

    /// <summary>The same file: one device, one inode.</summary>
    public bool SameFileAs(FileStatus other) => Inode == other.Inode && DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor;

    /// <summary>The bytes of a <c>struct statx</c> in the 256-byte shape the kernel writes: the same on every Linux architecture (little-endian x86-64 and arm64).</summary>
    internal const int StatxSize = 256;

    /// <summary>
    /// The fields of a 256-byte <c>struct statx</c> buffer: <c>stx_mode</c> (u16 at 28: type and permission bits), <c>stx_uid</c> (u32 at 20),
    /// <c>stx_ino</c> (u64 at 32), <c>stx_dev_major</c> / <c>stx_dev_minor</c> (u32 at 136 / 140). Pure, so a Windows host tests it.
    /// </summary>
    internal static FileStatus FromStatx(ReadOnlySpan<byte> statx)
    {
        var mode = BinaryPrimitives.ReadUInt16LittleEndian(statx[28..]);

        return new FileStatus(
            mode & 0xF000,
            mode & 0xFFF,
            BinaryPrimitives.ReadUInt32LittleEndian(statx[20..]),
            BinaryPrimitives.ReadUInt64LittleEndian(statx[32..]),
            BinaryPrimitives.ReadUInt32LittleEndian(statx[136..]),
            BinaryPrimitives.ReadUInt32LittleEndian(statx[140..]));
    }
}

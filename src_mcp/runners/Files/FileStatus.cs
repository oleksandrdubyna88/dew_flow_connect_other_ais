namespace CoaiMcp.Runners.Files;

// Ported from wsl_care (MIT, same owner) RegularFiles.cs:12-22 — FileStatus and SameFileAs. The read that fills it is LinuxStatx.

/// <summary>What <c>statx</c> says about one file: its type and permission bits (from <c>stx_mode</c>), its owner, its inode
/// and the device it lives on.</summary>
public sealed record FileStatus(int Type, int Permissions, uint OwnerUid, ulong Inode, uint DeviceMajor, uint DeviceMinor)
{
    public bool IsRegular => Type == 0x8000;

    public bool IsDirectory => Type == 0x4000;

    public bool IsSymbolicLink => Type == 0xA000;

    /// <summary>The same file: one device, one inode.</summary>
    public bool SameFileAs(FileStatus other) => Inode == other.Inode && DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor;
}

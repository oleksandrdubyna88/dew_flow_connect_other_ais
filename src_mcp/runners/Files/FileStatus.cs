namespace CoaiMcp.Runners.Files;

// Ported from wsl_care (MIT, same owner) RegularFiles.cs:12-22 — FileStatus and SameFileAs. The read that fills it is LinuxStatx.

/// <summary>What <c>statx</c> says about one file: its type and permission bits (from <c>stx_mode</c>), its owner, its inode
/// and the device it lives on.</summary>
public sealed record FileStatus(int Type, int Permissions, uint OwnerUid, ulong Inode, uint DeviceMajor, uint DeviceMinor)
{
    /// <summary>The id of the mount the file lies on (<c>stx_mnt_id</c>, u64 at 144 — the first field of its <c>/proc/self/mountinfo</c> line); 0 when it was not asked for or the kernel (before 5.8) did not
    /// fill it. An init-only property, so every existing positional construction keeps its shape.</summary>
    public ulong MountId { get; init; }

    public bool IsRegular => Type == 0x8000;

    public bool IsDirectory => Type == 0x4000;

    public bool IsSymbolicLink => Type == 0xA000;

    /// <summary>The same file: one device, one inode.</summary>
    public bool SameFileAs(FileStatus other) => Inode == other.Inode && DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor;
}

using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// The text a folder's IDENTITY is hashed from — per platform, never a spelling; EMPTY when the platform cannot say (DL7 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>).
/// </summary>
/// <remarks>
/// Windows: the upper-cased final path of the opened folder handle (<see cref="HandleRead.FinalDirectory"/>) — path-stable, which is why Windows lock files never pile up.
/// Linux: <c>dev_major:dev_minor:ino</c> from <c>statx(AT_FDCWD, path, 0, STATX_TYPE|STATX_INO)</c>, and only when the status says a DIRECTORY: a symbolic link in the path is followed (the key names the
/// folder the link leads to), so a symlinked parent, a bind mount and a <c>/proc/self/cwd</c> spelling are one folder; a file, a missing path or a failed call is empty — and an empty identity has no key, so
/// the lock is refused, never keyed by the spelling.
/// </remarks>
public static class FolderIdentity
{
    /// <summary>The identity text of the folder at <paramref name="path"/>, or empty.</summary>
    public static string Of(string path) =>
        OperatingSystem.IsWindows() ? Windows(path) : OperatingSystem.IsLinux() ? Linux(path) : string.Empty;

    private static string Windows(string path) => HandleRead.FinalDirectory(path).ToUpperInvariant();

    [SupportedOSPlatform("linux")]
    private static string Linux(string path) =>
        LinuxStatx.OfPath(path, 0, LinuxStatx.StatxType | LinuxStatx.StatxIno) is StatxRead.Read { Status: { IsDirectory: true } status }
            ? $"{status.DeviceMajor}:{status.DeviceMinor}:{status.Inode}"
            : string.Empty;
}

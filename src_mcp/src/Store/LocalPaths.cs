namespace CoaiMcp.Store;

/// <summary>
/// Which write paths <see cref="CheckoutRoots"/> may hand to the file system at all. A write path comes from a transcript, and a
/// transcript can be steered by a prompt: a UNC path (<c>\\host\share\a.cs</c>, <c>//host/share</c>, <c>\\?\UNC\host\share</c>) or a
/// device path (<c>\\.\…</c>, <c>\\?\…</c>) handed to <c>Directory.Exists</c> makes Windows contact that host over SMB and offer the
/// user's NTLM credentials to it. So the test runs on the TEXT, before any file-system call.
/// </summary>
/// <remarks>
/// <para><b>Allowed:</b> on Windows a drive-rooted path (<c>D:\…</c> or <c>D:/…</c>); elsewhere a path rooted at one <c>/</c> whose
/// first segment is not a host-automount root (<c>/net</c>, <c>/Network</c> — case-folded on macOS; see <see cref="AutomountRoots"/>).
/// Everything else — UNC, device, extended-length (even to a local drive), a lone leading separator — is refused, and the write
/// reads <c>Unresolved</c> with an empty key. Claude Code writes plain absolute paths (E1, Q8), so nothing measured is refused.</para>
/// <para><b>A mapped network drive</b> (<c>Z:</c> → a share) is refused too, from the local drive table (<see cref="DriveInfo"/>),
/// which answers without contacting the share. Not covered: a network file system mounted into the POSIX tree (cifs, nfs) —
/// its credentials are the mount's own, set up by whoever mounted it, not offered per access. Nor is an existing junction or link
/// under a local path: this test reads TEXT, and the probe that follows does follow links (see <see cref="CheckoutRoots"/>).</para>
/// </remarks>
internal static class LocalPaths
{
    /// <summary>True only for a drive-rooted path on Windows, or a single-<c>/</c>-rooted path elsewhere.</summary>
    public static bool IsLocalRooted(string path, bool windows) => IsLocalRooted(path, windows, OperatingSystem.IsMacOS());

    /// <summary>The same, with the file system's case rule for the POSIX arm stated (<paramref name="foldCase"/>: macOS).</summary>
    public static bool IsLocalRooted(string path, bool windows, bool foldCase) =>
        windows ? IsDriveRooted(path) : IsSlashRooted(path) && !IsAutomountRoot(path, foldCase);

    /// <summary>
    /// The roots an automounter serves by HOST name: <c>/net</c> (macOS <c>auto_master</c> <c>-hosts</c>, and Linux autofs <c>-hosts</c> maps)
    /// and <c>/Network</c> (macOS <c>/Network/Servers</c>). Touching <c>/net/&lt;host&gt;/…</c> mounts that host — a host the path chose.
    /// <c>/Volumes</c> is NOT here: it holds volumes already mounted, so touching it contacts no new host.
    /// </summary>
    /// <remarks><b>Residual limit:</b> a custom autofs map on any other root is not detected. The full fix reads the mount table
    /// (<c>/proc/self/mountinfo</c>, <c>getmntinfo</c>) and is out of this scope.</remarks>
    private static readonly string[] AutomountRoots = ["net", "Network"];

    private static bool IsAutomountRoot(string path, bool foldCase) =>
        AutomountRoots.Contains(FirstSegment(path), foldCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>The first segment of a <c>/</c>-rooted path: <c>net</c> for <c>/net/host/x</c> and for <c>/net</c>.</summary>
    private static string FirstSegment(string path)
    {
        var end = path.IndexOf('/', 1);

        return end < 0 ? path[1..] : path[1..end];
    }

    /// <summary>Whether the path's drive is a network drive. Windows only; a root the drive table cannot name is refused (fail closed).</summary>
    public static bool IsOnNetworkDrive(string path) => OperatingSystem.IsWindows() && IsNetworkRoot(Path.GetPathRoot(path) ?? string.Empty);

    private static bool IsDriveRooted(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]);

    private static bool IsSlashRooted(string path) => path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsSeparator(char c) => c is '\\' or '/';

    /// <summary>Whether a drive of this type must not be asked about.</summary>
    /// <remarks>Fail closed: <c>Unknown</c> and <c>NoRootDirectory</c> (an unmapped or vanished drive) are refused with <c>Network</c>.</remarks>
    internal static bool IsNotLocal(DriveType type) => type is DriveType.Network or DriveType.Unknown or DriveType.NoRootDirectory;

    private static bool IsNetworkRoot(string root)
    {
        try
        {
            return IsNotLocal(new DriveInfo(root).DriveType);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}

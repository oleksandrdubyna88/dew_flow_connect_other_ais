namespace CoaiMcp.Store;

/// <summary>
/// Which write paths <see cref="CheckoutRoots"/> may hand to the file system at all. A write path comes from a transcript, and a
/// transcript can be steered by a prompt: a UNC path (<c>\\host\share\a.cs</c>, <c>//host/share</c>, <c>\\?\UNC\host\share</c>) or a
/// device path (<c>\\.\…</c>, <c>\\?\…</c>) handed to <c>Directory.Exists</c> makes Windows contact that host over SMB and offer the
/// user's NTLM credentials to it. So the test runs on the TEXT, before any file-system call.
/// </summary>
/// <remarks>
/// <para><b>Allowed:</b> on Windows a drive-rooted path (<c>D:\…</c> or <c>D:/…</c>); elsewhere a path rooted at one <c>/</c>.
/// Everything else — UNC, device, extended-length (even to a local drive), a lone leading separator — is refused, and the write
/// reads <c>Unresolved</c> with an empty key. Claude Code writes plain absolute paths (E1, Q8), so nothing measured is refused.</para>
/// <para><b>A mapped network drive</b> (<c>Z:</c> → a share) is refused too, from the local drive table (<see cref="DriveInfo"/>),
/// which answers without contacting the share. Not covered: a network file system mounted into the POSIX tree (cifs, nfs) —
/// its credentials are the mount's own, set up by whoever mounted it, not offered per access.</para>
/// </remarks>
internal static class LocalPaths
{
    /// <summary>True only for a drive-rooted path on Windows, or a single-<c>/</c>-rooted path elsewhere.</summary>
    public static bool IsLocalRooted(string path, bool windows) => windows ? IsDriveRooted(path) : IsSlashRooted(path);

    /// <summary>Whether the path's drive is a network drive. Windows only; a root the drive table cannot name is refused (fail closed).</summary>
    public static bool IsOnNetworkDrive(string path) => OperatingSystem.IsWindows() && IsNetworkRoot(Path.GetPathRoot(path) ?? string.Empty);

    private static bool IsDriveRooted(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]);

    private static bool IsSlashRooted(string path) => path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal);

    private static bool IsSeparator(char c) => c is '\\' or '/';

    private static bool IsNetworkRoot(string root)
    {
        try
        {
            return new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}

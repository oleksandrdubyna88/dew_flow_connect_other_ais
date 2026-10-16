using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Snapshots;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// The directory junction from a coai-owned Kimi home to the person's own <c>credentials</c> folder (D19 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): made, read back, and refused with a sentence before anything is made
/// (E4.1 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Made by the kernel call, not by <c>cmd.exe</c>.</b> <c>cmd /d /c mklink /J "link" "target"</c> passes the person's
/// PROFILE path through cmd's quoting rules, where <c>&amp;</c>, <c>^</c>, <c>%</c> or an embedded quote in a user name changes
/// the command. <c>DeviceIoControl(FSCTL_SET_REPARSE_POINT)</c> takes the path as UTF-16 bytes, starts no process and needs
/// no privilege — a mount point is unprivileged where a symbolic link needs Developer Mode. The read-back after making it
/// is what makes the maker provable whichever way it was made.</para>
/// <para><b>An ancestor of the target may be a link; the target itself may not</b> (E4.2 decision 13): a relocated profile is an
/// ordinary machine, so <see cref="Refusal(HostKind, string, string)"/> admits it, asks the kernel where the target really is
/// (<c>HandleRead.FinalDirectory</c>) and refuses only a resolution that leaves the local drives. The junction keeps the SPELLING
/// as its target — one level.</para>
/// <para><b>Windows only</b> (D18): on any other host <see cref="Refusal"/> answers the D18 sentence and nothing is made.</para>
/// <para>Nothing Kimi-specific lives here: no config, no launch — only the shape of a link into somebody else's folder.</para>
/// </remarks>
public static partial class Junctions
{
    /// <summary>MAXIMUM_REPARSE_DATA_BUFFER_SIZE: a reparse point holds at most 16 KiB.</summary>
    private const int ReparseBufferBytes = 16 * 1024;

    /// <summary>The fixed part of a mount point's buffer: tag, data length, reserved and the four name offsets/lengths.</summary>
    private const int HeaderBytes = 16;

    private const uint MountPointTag = 0xA0000003;

    private static readonly AsyncLocal<IJunctionShell?> _shell = new();

    /// <summary>
    /// What the operating system answers for a junction (D7's shape, as <c>IFolderShell</c> is for a private folder): the product's is
    /// <c>JunctionsNative</c>, the kernel calls; a test's, per async flow, makes a symbolic link and names paths by their real spelling, so every
    /// decision below runs on any OS. TEST ONLY — null in the product.
    /// </summary>
    internal static IJunctionShell Shell => _shell.Value ?? JunctionsNative.Instance;

    /// <summary>TEST ONLY: the shell of this async flow — <see langword="null"/> puts the product's back.</summary>
    internal static void StandIn(IJunctionShell? shell) => _shell.Value = shell;

    /// <summary>Why a junction at <paramref name="link"/> to <paramref name="target"/> would not be made — the FIRST reason, or empty.</summary>
    public static string Refusal(HostKind host, string link, string target) => Refusal(host, link, target, Shell.DriveOf);

    /// <summary>The seam of the drive type, so a test can make a drive look like a mapped network one.</summary>
    internal static string Refusal(HostKind host, string link, string target, Func<string, DriveType> driveType) =>
        Refusal(host, link, target, driveType, Shell.FinalDirectory);

    /// <summary>The seam of the final path as well, so a test can make an ancestor link lead off the local drives.</summary>
    internal static string Refusal(HostKind host, string link, string target, Func<string, DriveType> driveType, Func<string, string> final) =>
        new Func<string>[]
        {
            () => WindowsOnly(host),
            () => LocalDrive(target, driveType),
            () => Length(target),
            () => Missing(target),
            () => Chained(target),
            () => Resolved(target, driveType, final),
            () => Taken(link),
        }
        .Select(check => check())
        .FirstOrDefault(refusal => refusal.Length > 0) ?? string.Empty;

    /// <summary>
    /// Whether <paramref name="target"/> may be READ by a deleter that wants to know what a link's target holds: a local path,
    /// never a share, a device path, a relative name, a mapped network drive — reading one connects out (Windows sends NTLM) or
    /// waits out the SMB timeout. On a Unix host: absolute, and not under the usual network automount folders.
    /// </summary>
    internal static bool IsLocal(string target) => IsLocal(target, Shell.DriveOf);

    /// <summary>
    /// The same question with the drive's type given. A drive-rooted spelling on Windows; on any other OS the Unix rule — so the
    /// decisions that ask it run, and are tested, on a Linux host with a test's shell (D18 keeps the product off every host but Windows).
    /// </summary>
    internal static bool IsLocal(string target, Func<string, DriveType> driveType) =>
        OperatingSystem.IsWindows() ? IsLocalDrivePath(target, driveType) : IsLocalUnixPath(target);

    /// <summary>
    /// A rooted <c>X:/</c> path that is not a UNC or device path and whose drive is not a mapped network drive. Judged by its SPELLING (a
    /// letter, a colon and a separator — which is what <see cref="Path.IsPathFullyQualified(string)"/> asks for a drive path on Windows, and
    /// answers falsely on every other OS, where "C:\" names no root), so a Linux run can say what a Windows one would.
    /// </summary>
    internal static bool IsLocalDrivePath(string target, Func<string, DriveType> driveType) =>
        !target.StartsWith(@"\\", StringComparison.Ordinal)
        && DriveRooted().IsMatch(target)
        && IsLocalDrive(driveType(target[..3]));

    private static bool IsLocalDrive(DriveType type) => type is not (DriveType.Network or DriveType.Unknown or DriveType.NoRootDirectory);

    private static bool IsLocalUnixPath(string target) =>
        Path.IsPathRooted(target) && !target.StartsWith("//", StringComparison.Ordinal) && !NetworkFolders.Any(folder => target.StartsWith(folder, StringComparison.Ordinal));

    /// <summary>Where Unix hosts automount network shares: the autofs <c>/net</c> and <c>/misc</c>, macOS's <c>/Network</c>.</summary>
    private static readonly string[] NetworkFolders = ["/net/", "/misc/", "/Network/"];

    /// <summary>The junction made, read back and verified — empty on success, otherwise the sentence, with nothing left at <paramref name="link"/>.</summary>
    public static string Make(HostKind host, string link, string target) => Make(host, link, target, Target);
    /// <summary>The seam of the read-back, so a test can make it name another folder.</summary>
    internal static string Make(HostKind host, string link, string target, Func<string, string> readBack) =>
        Make(host, link, target, readBack, RemoveFolder);

    /// <summary>The seam of the removal as well, so a test can make taking the half-made link back out fail.</summary>
    internal static string Make(HostKind host, string link, string target, Func<string, string> readBack, Action<string> remove) =>
        Make(host, link, target, readBack, remove, Shell.FinalDirectory);

    /// <summary>The seam of the final path as well, so a test can make the target look like it resolves off the local drives.</summary>
    internal static string Make(HostKind host, string link, string target, Func<string, string> readBack, Action<string> remove, Func<string, string> final)
    {
        var refusal = Refusal(host, link, target, Shell.DriveOf, final);

        return refusal.Length > 0 ? refusal : Verified(link, target, readBack, remove, Built(link, target, remove));
    }

    /// <summary>Where the link points — ONE level, never chased — absolute; empty when <paramref name="link"/> is no link or cannot be read.</summary>
    public static string Target(string link)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(link) ? new DirectoryInfo(link) : new FileInfo(link);

            return Absolute(info.LinkTarget ?? string.Empty, link);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>Whether two spellings name one folder: through <c>GetFullPath</c>, without a trailing separator, case ignored on Windows.</summary>
    public static bool SameFolder(string a, string b) =>
        a.Length > 0 && b.Length > 0
        && string.Equals(Normalised(a), Normalised(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string WindowsOnly(HostKind host) => host == HostKind.Windows
        ? string.Empty
        : $"a directory junction is made on Windows only; Kimi is refused on a {HostKinds.Word(host)} host until it is measured there (D18 of todo/PLAN_vibe_and_kimi_runtimes.md)";

    private static string LocalDrive(string target, Func<string, DriveType> driveType) => IsLocal(target, driveType)
        ? string.Empty
        : $"{target} is not a local drive path, and a junction cannot point at a network or roaming location — coai refuses to link the Kimi sign-in from there (D19)";

    /// <summary><c>C:\</c> or <c>C:/</c> — a letter, a colon and a separator; the only roots a junction may name.</summary>
    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DriveRooted();

    /// <summary>
    /// The real bound: a mount point's two names (<c>\??\</c> + the path, and the path) must fit the 16 KiB reparse buffer —
    /// about 4 090 characters, not the 32 000 a path may have. Pure arithmetic, asked before anything touches the disk.
    /// </summary>
    private static string Length(string target) => ReparseSize(Path.TrimEndingDirectorySeparator(target)) > ReparseBufferBytes
        ? $"the target path is {target.Length} characters, more than the {MostCharacters} a junction's reparse point can name"
        : string.Empty;

    /// <summary>The longest path whose buffer still fits: <c>16 + 2·(n + 4) + 2 + 2·n + 2 ≤ 16 KiB</c>.</summary>
    private const int MostCharacters = (ReparseBufferBytes - HeaderBytes - 12) / 4;

    private static string Missing(string target) => Directory.Exists(target) ? string.Empty : Absent(target);

    private static string Absent(string target) => File.Exists(target) ? $"{target} is a file, not a folder" : $"{target} does not exist";

    private static string Chained(string target) => SnapshotLinks.IsLink(target)
        ? $"{target} is itself a link, and coai never chains links (D19)"
        : string.Empty;

    /// <summary>
    /// Where the target REALLY is, asked of the kernel through an opened handle: an ANCESTOR of the target may be a link (a
    /// relocated profile is one — admitted, the person's own doing, exactly what their own <c>kimi</c> runs through), but where the
    /// ancestors lead must still be a local drive. A directory symlink under <c>C:\…</c> to <c>\\server\share</c> passes
    /// <see cref="LocalDrive"/>'s look at the spelling and would be listed on every sweep. Empty answer: the platform cannot name it,
    /// so coai cannot tell where the sign-in is (E4.2 decision 13).
    /// </summary>
    private static string Resolved(string target, Func<string, DriveType> driveType, Func<string, string> final)
    {
        var resolved = final(target);

        return resolved.Length == 0
            ? $"{target} cannot be named by its handle on this host, so coai cannot tell where the Kimi sign-in really is — refused (D19)"
            : IsLocal(resolved, driveType)
                ? string.Empty
                : $"{target} resolves to {resolved}, which is not a local drive path — a link on the way to the Kimi sign-in points at a network or roaming location, and coai refuses to link the sign-in from there (D19)";
    }

    private static string Taken(string link)
    {
        try
        {
            return StillThere(link) ? $"{link} already exists, and a junction is only ever made at a fresh path" : string.Empty;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return $"{link} is not a path a folder can be made at: {e.Message}";
        }
    }

    /// <summary>The empty folder made and turned into the junction; the failure sentence otherwise, with the folder removed again — or the reason it could not be.</summary>
    private static string Built(string link, string target, Action<string> remove)
    {
        var failure = Reparsed(link, Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)));

        return failure.Length == 0 ? string.Empty : $"the junction at {link} could not be made: {failure}{Also(LeftOver(link, remove))}";
    }

    private static string Reparsed(string link, string target)
    {
        try
        {
            Directory.CreateDirectory(link);

            return ReparseSize(target) > ReparseBufferBytes
                ? $"the target path is {target.Length} characters, and a reparse point holds {ReparseBufferBytes / 1024} KiB"
                : Shell.SetReparsePoint(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return e.Message;
        }
    }

    /// <summary>The whole buffer in bytes: the header, then the substitute name (<c>\??\</c> + target) and the print name, each NUL-terminated.</summary>
    internal static int ReparseSize(string target) => HeaderBytes + ((target.Length + 4) * 2) + 2 + (target.Length * 2) + 2;

    /// <summary>
    /// A hand-laid <c>REPARSE_DATA_BUFFER</c> for a mount point: tag, <c>ReparseDataLength</c> (everything after the first eight
    /// bytes), two reserved bytes, then the substitute-name and print-name offset and length — all four IN BYTES — and the path
    /// buffer. The <c>\??\</c> prefix belongs to the substitute name only. Only ever laid for a size under 16 KiB, so the
    /// <see langword="ushort"/> fields cannot wrap.
    /// </summary>
    internal static byte[] ReparseBuffer(string target)
    {
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var print = Encoding.Unicode.GetBytes(target);
        var buffer = new byte[ReparseSize(target)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), MountPointTag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)(buffer.Length - 8));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), (ushort)print.Length);
        substitute.CopyTo(buffer, HeaderBytes);
        print.CopyTo(buffer, HeaderBytes + substitute.Length + 2);

        return buffer;
    }

    /// <summary>The read-back: the link is a link and names the target — else it is removed by its own name and the launch is refused.</summary>
    private static string Verified(string link, string target, Func<string, string> readBack, Action<string> remove, string built)
    {
        if (built.Length > 0)
        {
            return built;
        }

        var read = readBack(link);
        if (OwnedPaths.IsLinkIfThere(link) && SameFolder(read, target))
        {
            return string.Empty;
        }

        var left = LeftOver(link, remove);

        return $"the junction at {link} was made but reads back as '{read}', not {target} — {(left.Length == 0 ? "removed again" : $"and {left}")}, the launch is refused";
    }

    /// <summary>The link or folder removed by its own name; empty when nothing is left at <paramref name="link"/> afterwards, otherwise the sentence that says it is still there and why.</summary>
    private static string LeftOver(string link, Action<string> remove)
    {
        var reason = RemoveByName(link, remove);

        return StillThere(link) ? $"could not be removed: {(reason.Length > 0 ? reason : "it is still there")}" : string.Empty;
    }

    private static string Also(string left) => left.Length == 0 ? string.Empty : $" — and {left}";

    private static bool StillThere(string link) => File.Exists(link) || Directory.Exists(link) || OwnedPaths.IsLinkIfThere(link);

    /// <summary>Empty when the removal ran; the failure's message otherwise.</summary>
    private static string RemoveByName(string link, Action<string> remove)
    {
        try
        {
            remove(link);

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    /// <summary>The link or folder itself, NON-recursively — on a junction that unlinks it and never enters the target.</summary>
    private static void RemoveFolder(string link) => Directory.Delete(link, recursive: false);

    private static string Absolute(string linkTarget, string link) => linkTarget.Length == 0
        ? string.Empty
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(linkTarget, Path.GetDirectoryName(Path.GetFullPath(link)) ?? string.Empty));

    private static string Normalised(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

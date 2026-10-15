using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Snapshots;
using Microsoft.Win32.SafeHandles;

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
/// <para><b>Windows only</b> (D18): on any other host <see cref="Refusal"/> answers the D18 sentence and nothing is made.</para>
/// <para>Nothing Kimi-specific lives here: no config, no launch — only the shape of a link into somebody else's folder.</para>
/// </remarks>
public static partial class Junctions
{
    /// <summary>Longest target the refusal lets through; the reparse buffer's own 16 KiB is far lower and is checked again where the buffer is laid.</summary>
    private const int LongestTarget = 32_000;

    /// <summary>MAXIMUM_REPARSE_DATA_BUFFER_SIZE: a reparse point holds at most 16 KiB.</summary>
    private const int ReparseBufferBytes = 16 * 1024;

    /// <summary>The fixed part of a mount point's buffer: tag, data length, reserved and the four name offsets/lengths.</summary>
    private const int HeaderBytes = 16;

    private const uint MountPointTag = 0xA0000003;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;

    /// <summary>FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS: open the folder itself, not what it may already point at.</summary>
    private const uint OpenTheLinkItself = 0x00200000 | 0x02000000;

    /// <summary>Why a junction at <paramref name="link"/> to <paramref name="target"/> would not be made — the FIRST reason, or empty.</summary>
    public static string Refusal(HostKind host, string link, string target) =>
        new Func<string>[]
        {
            () => WindowsOnly(host),
            () => LocalDrive(target),
            () => Length(target),
            () => Missing(target),
            () => Chained(target),
            () => Taken(link),
        }
        .Select(check => check())
        .FirstOrDefault(refusal => refusal.Length > 0) ?? string.Empty;

    /// <summary>The junction made, read back and verified — empty on success, otherwise the sentence, with nothing left at <paramref name="link"/>.</summary>
    public static string Make(HostKind host, string link, string target) => Make(host, link, target, Target);

    /// <summary>The seam of the read-back, so a test can make it name another folder.</summary>
    internal static string Make(HostKind host, string link, string target, Func<string, string> readBack)
    {
        var refusal = Refusal(host, link, target);

        return refusal.Length > 0 ? refusal : Verified(link, target, readBack, Built(link, target));
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

    private static string LocalDrive(string target) => IsLocalDrivePath(target)
        ? string.Empty
        : $"{target} is not a local drive path, and a junction cannot point at a network or roaming location — coai refuses to link the Kimi sign-in from there (D19)";

    private static bool IsLocalDrivePath(string target) =>
        Path.IsPathFullyQualified(target) && !target.StartsWith(@"\\", StringComparison.Ordinal) && DriveRooted().IsMatch(target);

    /// <summary><c>C:\</c> or <c>C:/</c> — a letter, a colon and a separator; the only roots a junction may name.</summary>
    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DriveRooted();

    private static string Length(string target) => target.Length > LongestTarget
        ? $"the target path is {target.Length} characters, more than the {LongestTarget} a junction's reparse buffer can name"
        : string.Empty;

    private static string Missing(string target) => Directory.Exists(target) ? string.Empty : $"{target} does not exist";

    private static string Chained(string target) => SnapshotLinks.IsLink(target)
        ? $"{target} is itself a link, and coai never chains links (D19)"
        : string.Empty;

    private static string Taken(string link) =>
        File.Exists(link) || Directory.Exists(link) || OwnedPaths.IsLinkIfThere(link)
            ? $"{link} already exists, and a junction is only ever made at a fresh path"
            : string.Empty;

    /// <summary>The empty folder made and turned into the junction; the failure sentence otherwise, with the folder removed again.</summary>
    private static string Built(string link, string target)
    {
        var failure = Reparsed(link, Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)));
        if (failure.Length == 0)
        {
            return string.Empty;
        }

        RemoveByName(link);

        return $"the junction at {link} could not be made: {failure}";
    }

    private static string Reparsed(string link, string target)
    {
        try
        {
            Directory.CreateDirectory(link);

            return SetReparsePoint(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    private static string SetReparsePoint(string link, string target)
    {
        if (ReparseSize(target) > ReparseBufferBytes)
        {
            return $"the target path is {target.Length} characters, and a reparse point holds {ReparseBufferBytes / 1024} KiB";
        }

        using var handle = HandleRead.CreateFileW(link, GenericWrite, 0, 0, OpenExisting, OpenTheLinkItself, 0);
        if (handle.IsInvalid)
        {
            return Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());
        }

        var buffer = ReparseBuffer(target);

        return DeviceIoControl(handle, FsctlSetReparsePoint, buffer, (uint)buffer.Length, 0, 0, out _, 0)
            ? string.Empty
            : Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());
    }

    /// <summary>The whole buffer in bytes: the header, then the substitute name (<c>\??\</c> + target) and the print name, each NUL-terminated.</summary>
    private static int ReparseSize(string target) => HeaderBytes + ((target.Length + 4) * 2) + 2 + (target.Length * 2) + 2;

    /// <summary>
    /// A hand-laid <c>REPARSE_DATA_BUFFER</c> for a mount point: tag, <c>ReparseDataLength</c> (everything after the first eight
    /// bytes), two reserved bytes, then the substitute-name and print-name offset and length — all four IN BYTES — and the path
    /// buffer. The <c>\??\</c> prefix belongs to the substitute name only. Only ever laid for a size under 16 KiB, so the
    /// <see langword="ushort"/> fields cannot wrap.
    /// </summary>
    private static byte[] ReparseBuffer(string target)
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
    private static string Verified(string link, string target, Func<string, string> readBack, string built)
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

        RemoveByName(link);

        return $"the junction at {link} was made but reads back as '{read}', not {target} — removed again, the launch is refused";
    }

    /// <summary>The link or folder itself, NON-recursively — on a junction that unlinks it and never enters the target.</summary>
    private static void RemoveByName(string link)
    {
        try
        {
            Directory.Delete(link, recursive: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done from here: the caller's sentence already says the link was not made.
        }
    }

    private static string Absolute(string linkTarget, string link) => linkTarget.Length == 0
        ? string.Empty
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(linkTarget, Path.GetDirectoryName(Path.GetFullPath(link)) ?? string.Empty));

    private static string Normalised(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle handle, uint code, ReadOnlySpan<byte> input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
}

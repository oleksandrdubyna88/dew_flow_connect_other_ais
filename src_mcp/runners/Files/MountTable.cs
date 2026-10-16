using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Files;

// Ported from wsl_care (src_daemon/src/WslCare.Core/Files/MountTable.cs): same owner, MIT. Ported are the parser, `Holding`
// and `IsDrvfs` that DL6 of todo/PLAN_kimi_and_vibe_on_linux.md names; wsl_care's `IsWholeDrive` (which mount IS a Windows
// drive's root, for its system-drive lookup) is not ported: nothing here asks it. The kernel's own rules are unchanged —
// octal escapes decoded, super-options kept raw — and its vectors come with it (MountTableTests).

/// <summary>One line of <c>/proc/self/mountinfo</c>: <c>id parent major:minor root mount-point options [optional…] - type source
/// super-options</c>, its root, mount point and source decoded from the kernel's octal escapes (<c>\040</c> space, <c>\011</c> tab,
/// <c>\012</c> newline, <c>\134</c> backslash), its super-options kept raw (the kernel prints them so) and split at both <c>,</c>
/// and <c>;</c>.</summary>
public sealed record MountEntry(string Root, string MountPoint, uint Major, uint Minor, string Type, string Source, IReadOnlyList<string> Options)
{
    /// <summary>The value of a <c>name=</c> option; empty when there is none.</summary>
    public string Option(string name) =>
        Options.Where(o => o.StartsWith(name + "=", StringComparison.Ordinal)).Select(o => o[(name.Length + 1)..]).FirstOrDefault() ?? string.Empty;

    /// <summary>A drvfs mount — WSL 2's <c>9p</c> with <c>aname=drvfs</c>, WSL 1's <c>drvfs</c>, or <c>virtiofs</c>: a Windows
    /// filesystem whose modes are what the mount reports and whose access Windows decides.</summary>
    public bool IsDrvfs => (Type == "9p" && Options.Contains("aname=drvfs", StringComparer.Ordinal)) || Type is "drvfs" or "virtiofs";
}

/// <summary>
/// The kernel's mount table: parsed once, asked which mount holds a path. A sign-in folder on a Windows filesystem is shared with
/// a Windows program that no Linux lock can see, and such a filesystem reaches Linux through <c>/mnt/c</c>, a custom
/// <c>mount -t drvfs</c> point, a bind mount or <c>virtiofs</c> — so the rule is the TYPE of the mount that holds the path, never
/// a prefix of the path (DL6 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>).
/// </summary>
public static partial class MountTable
{
    /// <summary>Every well-formed line of <paramref name="mountInfo"/>; a short or empty line is skipped.</summary>
    public static IReadOnlyList<MountEntry> Parse(string mountInfo) => [.. mountInfo.Split('\n').SelectMany(Line)];

    /// <summary>The mount <paramref name="path"/> (a distro path, <c>/</c>-separated) lies on: the deepest mount point that is the
    /// path or above it, the LAST line for it when mounts are stacked (the one a path reaches); <c>null</c> when none is (a table
    /// without <c>/</c>).</summary>
    public static MountEntry? Holding(IReadOnlyList<MountEntry> mounts, string path) =>
        mounts.Where(m => IsSameOrUnder(path, m.MountPoint)).OrderBy(m => m.MountPoint.Length).LastOrDefault();

    /// <summary>Whether the mount that holds <paramref name="path"/> is a Windows filesystem. A path no mount holds is not judged one:
    /// a table without <c>/</c> is a table that could not be read, and the caller decides what that is worth.</summary>
    public static bool OnWindowsFilesystem(IReadOnlyList<MountEntry> mounts, string path) =>
        Holding(mounts, path) is { IsDrvfs: true };

    private static bool IsSameOrUnder(string path, string mountPoint) =>
        mountPoint == "/" || string.Equals(path, mountPoint, StringComparison.Ordinal) || path.StartsWith(mountPoint.TrimEnd('/') + "/", StringComparison.Ordinal);

    /// <summary>The line, or nothing when it is not one (empty, or short of its fields). The kernel escapes the root, the mount
    /// point and the source (decoded here); the SUPER OPTIONS it prints raw — live, Docker's line carries
    /// <c>path=C:\Program Files\…</c> — so they are never decoded: a folder named <c>C:\134</c> must not read as <c>C:\</c>.</summary>
    private static IEnumerable<MountEntry> Line(string line)
    {
        var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var dash = Separator(f);
        return dash > 0 && Device(f[2]) is [var major, var minor]
            ? [new(Unescaped(f[3]), Unescaped(f[4]), major, minor, f[dash + 1], Unescaped(f[dash + 2]), f[dash + 3].Split([',', ';']))]
            : [];
    }

    /// <summary>Where the <c>-</c> that ends the optional fields is, when the line holds every field around it; -1 otherwise.</summary>
    private static int Separator(string[] f)
    {
        var dash = f.Length >= 10 ? Array.IndexOf(f, "-", 6) : -1;
        return dash > 0 && dash + 3 < f.Length ? dash : -1;
    }

    /// <summary><c>major:minor</c> as two numbers; empty when it is not that.</summary>
    private static uint[] Device(string field)
    {
        var parts = field.Split(':');
        return parts.Length == 2 && uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            ? [major, minor]
            : [];
    }

    private static string Unescaped(string field) =>
        OctalEscape().Replace(field, m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());

    [GeneratedRegex(@"\\([0-7]{3})")]
    private static partial Regex OctalEscape();
}

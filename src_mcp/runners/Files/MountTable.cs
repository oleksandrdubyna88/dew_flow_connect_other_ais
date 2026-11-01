using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Files;

// Ported from wsl_care (MIT, same owner) src_daemon/src/WslCare.Core/Files/MountTable.cs — Parse, Holding, MountEntry.IsDrvfs; the whole-drive half (IsWholeDrive) is not ported: nothing here asks it.
// wsl_care has no tests of the parser itself (DL6 and §8 of research/PLAN_kimi_and_vibe_on_linux.md said it had): MountTableTests are written fresh.

/// <summary>One line of <c>/proc/self/mountinfo</c>: <c>id parent major:minor root mount-point options [optional…] - type source
/// super-options</c>, its root, mount point and source decoded from the kernel's octal escapes (<c>\040</c> space, <c>\011</c> tab,
/// <c>\012</c> newline, <c>\134</c> backslash), its super-options kept raw (the kernel prints them so) and split at both <c>,</c>
/// and <c>;</c>.</summary>
public sealed record MountEntry(string Root, string MountPoint, uint Major, uint Minor, string Type, string Source, IReadOnlyList<string> Options)
{
    /// <summary>The mount id (the first field of the line) — what <c>statx</c>'s <c>stx_mnt_id</c> names; 0 for an entry built by hand. An init-only property, so the positional shape above is unchanged.</summary>
    public ulong Id { get; init; }

    /// <summary>The value of a <c>name=</c> option; empty when there is none.</summary>
    public string Option(string name) =>
        Options.Where(o => o.StartsWith(name + "=", StringComparison.Ordinal)).Select(o => o[(name.Length + 1)..]).FirstOrDefault() ?? string.Empty;

    /// <summary>A drvfs mount — WSL 2's <c>9p</c> with <c>aname=drvfs</c>, WSL 1's <c>drvfs</c>, or <c>virtiofs</c>: a Windows
    /// filesystem whose modes are what the mount reports and whose access Windows decides.</summary>
    public bool IsDrvfs => (Type == "9p" && Options.Contains("aname=drvfs", StringComparer.Ordinal)) || Type is "drvfs" or "virtiofs";

    /// <summary>
    /// A network filesystem (DL6, decided 2026-10-17 pending the owner): <c>nfs</c>, <c>nfs4</c>, <c>cifs</c>, <c>smb3</c>, <c>fuse.sshfs</c>, <c>ceph</c>, <c>glusterfs</c>. A sign-in folder there is shared with a Kimi on ANOTHER machine,
    /// which refreshes the same rotating token — and no lock on this machine can see it.
    /// </summary>
    public bool IsNetwork => Type is "nfs" or "nfs4" or "cifs" or "smb3" or "fuse.sshfs" or "ceph" or "glusterfs";
}

/// <summary>What the kernel says, through <c>statx</c>, about the mount that really holds a path — a closed union, so "the kernel was not asked" and "the kernel could not say" are never one answer.</summary>
public abstract record MountIdentity
{
    private MountIdentity()
    {
    }

    /// <summary>Nobody asked the kernel (off Linux, or a test of the text rule alone): the mount point text decides.</summary>
    public sealed record NotAsked : MountIdentity;

    /// <summary>The kernel named the mount (<c>stx_mnt_id</c>, Linux 5.8 and later) and the device of the file: the mount is found by this id, whatever mount points the table lists.</summary>
    public sealed record Mounted(ulong MountId, uint Major, uint Minor) : MountIdentity;

    /// <summary>The kernel gave the device of the file but no mount id (before 5.8): the text rule decides, and the device must agree with it.</summary>
    public sealed record Device(uint Major, uint Minor) : MountIdentity;

    /// <summary>The call failed: nothing proves which mount holds the path.</summary>
    public sealed record Unreadable(string Reason) : MountIdentity;
}

/// <summary>Which mount holds a path — a closed union: the mount, no mount at all, or evidence that the text rule would name the wrong one.</summary>
public abstract record MountHolder
{
    private MountHolder()
    {
    }

    /// <summary>The mount the path lies on.</summary>
    public sealed record Found(MountEntry Entry) : MountHolder;

    /// <summary>No mount in the table holds the path (a table without <c>/</c>).</summary>
    public sealed record NoMount : MountHolder;

    /// <summary>The mount could not be proven: the kernel named a mount the table does not list, the device it reported is not the one the text rule found, or the call failed.</summary>
    public sealed record Unproven(string Why) : MountHolder;
}

/// <summary>
/// The kernel's mount table: parsed once, asked which mount holds a path. A sign-in folder on a Windows filesystem is shared with
/// a Windows program that no Linux lock can see, and such a filesystem reaches Linux through <c>/mnt/c</c>, a custom
/// <c>mount -t drvfs</c> point, a bind mount or <c>virtiofs</c> — so the rule is the TYPE of the mount that holds the path, never
/// a prefix of the path (DL6 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>).
/// </summary>
public static partial class MountTable
{
    /// <summary>Every well-formed line of <paramref name="mountInfo"/>; a short or empty line is skipped.</summary>
    public static IReadOnlyList<MountEntry> Parse(string mountInfo) => [.. mountInfo.Split('\n').SelectMany(Line)];

    /// <summary>The mount <paramref name="path"/> (a distro path, <c>/</c>-separated) lies on: the deepest mount point that is the
    /// path or above it, the LAST line for it when mounts are stacked (the one a path reaches); <c>null</c> when none is (a table
    /// without <c>/</c>).</summary>
    public static MountEntry? Holding(IReadOnlyList<MountEntry> mounts, string path) =>
        Find(mounts, path, new MountIdentity.NotAsked()) is MountHolder.Found found ? found.Entry : null;

    /// <summary>
    /// The mount <paramref name="path"/> lies on, by what the kernel said about it. The text rule (the deepest mount point at or above the path, the last line when mounts stack) is wrong under an
    /// OVER-MOUNT — a drvfs mounted later over <c>/data</c> hides an ext4 mounted at <c>/data/linux</c>, and the text still names the hidden one — so the kernel's own answer wins:
    /// <see cref="MountIdentity.Mounted"/> is found by its id; <see cref="MountIdentity.Device"/> keeps the text rule but refuses when the device it names is not the file's; an
    /// <see cref="MountIdentity.Unreadable"/> proves nothing.
    /// </summary>
    public static MountHolder Find(IReadOnlyList<MountEntry> mounts, string path, MountIdentity identity) => identity switch
    {
        MountIdentity.Mounted mounted => ById(mounts, mounted),
        MountIdentity.Device device => AgreedWith(ByText(mounts, path), device),
        MountIdentity.Unreadable unreadable => new MountHolder.Unproven($"the kernel's answer for {path} could not be read: {unreadable.Reason}"),
        _ => ByText(mounts, path),
    };

    private static MountHolder ById(IReadOnlyList<MountEntry> mounts, MountIdentity.Mounted mounted) =>
        mounts.FirstOrDefault(m => m.Id == mounted.MountId) is { } entry
            ? new MountHolder.Found(entry)
            : new MountHolder.Unproven($"the kernel says the path lies on mount {mounted.MountId}, which the table does not list");

    private static MountHolder AgreedWith(MountHolder byText, MountIdentity.Device device) => byText switch
    {
        MountHolder.Found { Entry: var entry } when entry.Major != device.Major || entry.Minor != device.Minor =>
            new MountHolder.Unproven($"the kernel says the path is on device {device.Major}:{device.Minor}, but the mount point text names {entry.MountPoint} ({entry.Major}:{entry.Minor}) — a mount hides it"),
        _ => byText,
    };

    private static MountHolder ByText(IReadOnlyList<MountEntry> mounts, string path) =>
        mounts.Where(m => IsSameOrUnder(path, m.MountPoint)).OrderBy(m => m.MountPoint.Length).LastOrDefault() is { } entry
            ? new MountHolder.Found(entry)
            : new MountHolder.NoMount();

    private static bool IsSameOrUnder(string path, string mountPoint) =>
        mountPoint == "/" || string.Equals(path, mountPoint, StringComparison.Ordinal) || path.StartsWith(mountPoint.TrimEnd('/') + "/", StringComparison.Ordinal);

    /// <summary>The line, or nothing when it is not one (empty, or short of its fields). The kernel escapes the root, the mount
    /// point and the source (decoded here); the SUPER OPTIONS it prints raw — live, Docker's line carries
    /// <c>path=C:\Program Files\…</c> — so they are never decoded: a folder named <c>C:\134</c> must not read as <c>C:\</c>.</summary>
    private static IEnumerable<MountEntry> Line(string line)
    {
        var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var dash = Separator(f);
        return dash > 0 && Device(f[2]) is [var major, var minor] && ulong.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? [new(Unescaped(f[3]), Unescaped(f[4]), major, minor, f[dash + 1], Unescaped(f[dash + 2]), f[dash + 3].Split([',', ';'])) { Id = id }]
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

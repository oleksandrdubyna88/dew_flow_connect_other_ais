using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// DL6 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>: the FILESYSTEM that holds the sign-in's real path, read from <c>/proc/self/mountinfo</c> — a
/// Windows filesystem (<c>9p</c> with <c>aname=drvfs</c>, <c>drvfs</c>, <c>virtiofs</c>) is refused wherever it is mounted; <c>/mnt/&lt;letter&gt;/</c> is the
/// example, the mount type is the rule.
/// </summary>
/// <remarks>
/// A sign-in folder on a Windows filesystem is shared with a Windows Kimi that no Linux lock can see, and such a filesystem reaches Linux through
/// <c>/mnt/c</c>, a custom <c>mount -t drvfs</c> point, a bind mount or <c>virtiofs</c> — so a prefix of the path is never the rule. A table that cannot be
/// read, or that no mount in holds the path, is not evidence that the folder is local: both are refused.
/// </remarks>
public static class MountLocality
{
    public const string MountInfoPath = "/proc/self/mountinfo";

    private const string Plan = "DL6 of todo/PLAN_kimi_and_vibe_on_linux.md";

    /// <summary>Empty when the real path lies on a non-Windows, non-network filesystem; else the sentence. <paramref name="mountInfo"/> reads the table (a test hands a string). The mount is found by the KERNEL's
    /// own answer for the path (<see cref="Identify"/>), so a mount laid over another cannot hide the one that really holds it.</summary>
    public static string Refusal(string target, string realPath, Func<string> mountInfo) => Refusal(target, realPath, mountInfo, Identify);

    /// <summary><see cref="Refusal(string, string, Func{string})"/> with the kernel's answer handed in — the seam: a test hands a <see cref="MountIdentity"/>, the product hands <see cref="Identify"/>. Network storage is refused (the default).</summary>
    public static string Refusal(string target, string realPath, Func<string> mountInfo, Func<string, MountIdentity> identify) =>
        Judge(target, realPath, mountInfo, identify, allowNetwork: false).Refusal;

    /// <summary>
    /// The whole judgement: a refusal, or a warning, or neither. A Windows filesystem and a table that cannot be read are refused whatever <paramref name="allowNetwork"/> says; network storage is refused unless it is
    /// on — then it is admitted with a one-line warning (DL6's default-off override, <c>coai.kimiAllowNetworkSignIn</c>).
    /// </summary>
    public static MountVerdict Judge(string target, string realPath, Func<string> mountInfo, Func<string, MountIdentity> identify, bool allowNetwork) =>
        Table(mountInfo) switch
        {
            ({ } text, _) => Judged(target, realPath, MountTable.Find(MountTable.Parse(text), realPath, identify(realPath)), allowNetwork),
            (_, var reason) => MountVerdict.Refused($"{target}: coai could not read {MountInfoPath} ({reason}), so it cannot tell which filesystem holds the Kimi sign-in — refused ({Plan})"),
        };

    /// <summary>The table's text, or why it could not be read.</summary>
    private static (string? Text, string Reason) Table(Func<string> mountInfo)
    {
        try
        {
            return (mountInfo(), string.Empty);
        }
        catch (Exception e)
        {
            return (null, e.GetType().Name);
        }
    }

    /// <summary>What the kernel says about the mount that holds <paramref name="realPath"/>: <c>statx</c> with <c>STATX_MNT_ID</c> (Linux 5.8 and later); without that bit, the device alone; off Linux, nothing asked.
    /// A call that fails is not "local" — it is <see cref="MountIdentity.Unreadable"/>, which refuses.</summary>
    public static MountIdentity Identify(string realPath) => OperatingSystem.IsLinux() ? Statx(realPath) : new MountIdentity.NotAsked();

    [SupportedOSPlatform("linux")]
    private static MountIdentity Statx(string realPath) =>
        LinuxStatx.OfPath(realPath, 0, LinuxStatx.StatxType | LinuxStatx.StatxIno | LinuxStatx.StatxMntId) switch
        {
            StatxRead.Read { Status: var status } => new MountIdentity.Mounted(status.MountId, status.DeviceMajor, status.DeviceMinor),
            StatxRead.Failed { Errno: 0 } => DeviceOnly(realPath),
            StatxRead.Failed failed => new MountIdentity.Unreadable($"statx answered errno {failed.Errno}"),
            _ => new MountIdentity.Unreadable("statx answered nothing"),
        };

    /// <summary>The kernel answered without a field it was asked for — a kernel before 5.8 has no mount id — so ask again for the device alone.</summary>
    [SupportedOSPlatform("linux")]
    private static MountIdentity DeviceOnly(string realPath) =>
        LinuxStatx.OfPath(realPath, 0, LinuxStatx.StatxType | LinuxStatx.StatxIno) switch
        {
            StatxRead.Read { Status: var status } => new MountIdentity.Device(status.DeviceMajor, status.DeviceMinor),
            StatxRead.Failed failed => new MountIdentity.Unreadable($"statx answered errno {failed.Errno}"),
            _ => new MountIdentity.Unreadable("statx answered nothing"),
        };

    private static MountVerdict Judged(string target, string realPath, MountHolder holder, bool allowNetwork) =>
        holder switch
        {
            MountHolder.NoMount => MountVerdict.Refused($"{target}: no mount in {MountInfoPath} holds {realPath}, so coai cannot tell which filesystem the Kimi sign-in is on — refused ({Plan})"),
            MountHolder.Unproven unproven => MountVerdict.Refused($"{target}: coai cannot prove which mount holds {realPath} ({unproven.Why}), so it cannot tell which filesystem the Kimi sign-in is on — refused ({Plan})"),
            MountHolder.Found { Entry.IsDrvfs: true } found => MountVerdict.Refused(Windows(target, found.Entry)),
            MountHolder.Found { Entry.IsNetwork: true } found => allowNetwork ? MountVerdict.Warned(NetworkWarning(found.Entry)) : MountVerdict.Refused(Network(target, found.Entry)),
            _ => MountVerdict.Admitted,
        };

    private static string Windows(string target, MountEntry entry) =>
        $"{target} is on a Windows filesystem ({entry.Type} mounted at {entry.MountPoint}, for example /mnt/<letter>/): a sign-in folder there is shared with a Windows Kimi "
        + $"that no Linux lock can see — two refreshers of one rotating token — so coai refuses to link the Kimi sign-in from there ({Plan})";

    /// <summary>The refusal, with its cure — a SEPARATE local sign-in, never a copy of the rotating token (D19) — and the override's name.</summary>
    private static string Network(string target, MountEntry entry) =>
        $"your Kimi sign-in folder is on network storage ({entry.Type}); a Kimi on another machine that shares this home can refresh the same sign-in and sign you out, and coai cannot see it. "
        + "Point `KIMI_CODE_HOME` at a folder on this machine's own disk (not under the network home) and sign in there once with `kimi login`, or set "
        + $"`coai.kimiAllowNetworkSignIn` if you never run Kimi on another machine — {target} is on {entry.MountPoint} ({Plan})";

    /// <summary>The one line the card shows while the override admits a network sign-in folder.</summary>
    private static string NetworkWarning(MountEntry entry) =>
        $"your Kimi sign-in folder is on network storage ({entry.Type}) and coai.kimiAllowNetworkSignIn admits it: a Kimi on another machine that shares this home can refresh the same sign-in and sign you out, and coai cannot see it";
}

/// <summary>What judging a sign-in folder's filesystem came to: a refusal, or a one-line warning (the override admitted network storage), or neither — a closed shape, so "refused" and "warned" are never one string.</summary>
/// <param name="Refusal">Why the sign-in is not linked, or empty.</param>
/// <param name="Warning">What the person should know about a sign-in that WAS admitted, or empty.</param>
public sealed record MountVerdict(string Refusal, string Warning)
{
    /// <summary>Neither refused nor warned about: a local disk.</summary>
    public static MountVerdict Admitted { get; } = new(string.Empty, string.Empty);

    /// <summary>Refused with <paramref name="sentence"/>.</summary>
    public static MountVerdict Refused(string sentence) => new(sentence, string.Empty);

    /// <summary>Admitted, with <paramref name="sentence"/> for the card.</summary>
    public static MountVerdict Warned(string sentence) => new(string.Empty, sentence);
}

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

    /// <summary>Empty when the real path lies on a non-Windows filesystem; else the sentence. <paramref name="mountInfo"/> reads the table (a test hands a string).</summary>
    public static string Refusal(string target, string realPath, Func<string> mountInfo) =>
        Table(mountInfo) switch
        {
            ({ } text, _) => Judged(target, realPath, MountTable.Parse(text)),
            (_, var reason) => $"{target}: coai could not read {MountInfoPath} ({reason}), so it cannot tell which filesystem holds the Kimi sign-in — refused ({Plan})",
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

    private static string Judged(string target, string realPath, IReadOnlyList<MountEntry> mounts) =>
        MountTable.Holding(mounts, realPath) switch
        {
            null => $"{target}: no mount in {MountInfoPath} holds {realPath}, so coai cannot tell which filesystem the Kimi sign-in is on — refused ({Plan})",
            { IsDrvfs: true } entry => Windows(target, entry),
            _ => string.Empty,
        };

    private static string Windows(string target, MountEntry entry) =>
        $"{target} is on a Windows filesystem ({entry.Type} mounted at {entry.MountPoint}, for example /mnt/<letter>/): a sign-in folder there is shared with a Windows Kimi "
        + $"that no Linux lock can see — two refreshers of one rotating token — so coai refuses to link the Kimi sign-in from there ({Plan})";
}

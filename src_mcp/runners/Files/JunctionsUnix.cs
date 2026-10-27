namespace CoaiMcp.Runners.Files;

/// <summary>
/// The product's junction shell on Linux and WSL (DL6 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): a DIRECTORY SYMBOLIC LINK — <c>symlink(2)</c> at a path that does not exist, which is atomic and whose <c>EEXIST</c> is a refusal,
/// never a delete-then-create window — read back by <see cref="Junctions.Target"/>. It decides nothing: every refusal is <see cref="Junctions"/>'s.
/// </summary>
internal sealed class JunctionsUnix : IJunctionShell
{
    public static readonly JunctionsUnix Instance = new();

    /// <summary>The real spelling of a folder that exists, or empty — the fallback <c>HandleRead.CanonicalDirectory</c> already uses off Windows.</summary>
    public string FinalDirectory(string path) => Directory.Exists(path) ? Worktrees.WorktreePaths.Real(path) : string.Empty;

    /// <summary>Never asked on Unix: <c>Junctions.IsLocal</c> takes the Unix rule there.</summary>
    public DriveType DriveOf(string root) => DriveType.Unknown;

    public string MakeLink(string link, string target)
    {
        try
        {
            // The Windows shell's folder-making creates missing parents; the link's parent is made here for the same reason (a symlink cannot be made in a folder that is not there).
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(link)) ?? string.Empty);
            Directory.CreateSymbolicLink(link, target);

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}

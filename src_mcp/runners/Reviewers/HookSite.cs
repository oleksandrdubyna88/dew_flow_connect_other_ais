using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What one agy hook process knows by itself, never from the payload: the coai folder, and how an entry on the disk is looked at.
/// </summary>
/// <remarks>
/// <para><b>The coai folder is where the hook runs.</b> The hook's command is relative (<c>sh ./coai-hook.sh</c>, <c>.\coai-hook.cmd</c>), so it
/// starts only when the shell's working folder is the folder that holds the script, <c>&lt;coai folder&gt;/.agents</c>, and the handler inherits
/// that folder. The environment plays no part: the launcher's <see cref="AntigravityReadOnly.DefaultBase"/> and the hook's differ on the Team
/// server and on a question row, where the child's environment is not the launcher's. The payload's <c>cwd</c> is NOT the coai folder: agy sends
/// the first of its sorted <c>workspacePaths</c>, which is a granted root whenever one sorts before the coai folder.</para>
/// </remarks>
/// <param name="CoaiFolder">The folder the hook was prepared in; null when the hook cannot tell (every read is then denied).</param>
/// <param name="Read">How one entry is looked at — the seam <see cref="RealPath.Resolve"/> has.</param>
/// <param name="IsLocal">Whether a path is on a local drive: the only paths the hook looks at (see <see cref="AgyHookPlaces"/>).</param>
/// <param name="FoldsDotsAsText">Whether the OS folds <c>.</c> and <c>..</c> as text before it follows any link (Windows): a <c>Path</c> with such a segment is then denied.</param>
public sealed record HookSite(string? CoaiFolder, EntryReader Read, Func<string, bool> IsLocal, bool FoldsDotsAsText)
{
    /// <summary>The site of the running hook process. Never throws.</summary>
    public static HookSite OfThisProcess() => new(CoaiFolderOf(WorkingFolder()), RealPath.Lstat, text => Junctions.IsLocal(text, Junctions.Shell.DriveOf, OperatingSystem.IsWindows()), OperatingSystem.IsWindows());

    /// <summary>The coai folder for a hook working in <paramref name="workingDirectory"/>: its parent when it is a fully qualified <c>.agents</c> folder, else null. Text only, no disk.</summary>
    internal static string? CoaiFolderOf(string workingDirectory) =>
        Path.IsPathFullyQualified(workingDirectory) ? ParentOfAgentsFolder(Path.TrimEndingDirectorySeparator(workingDirectory)) : null;

    private static string? ParentOfAgentsFolder(string trimmed) =>
        string.Equals(Path.GetFileName(trimmed), ".agents", DocumentId.Comparison) ? ParentOf(trimmed) : null;

    private static string? ParentOf(string folder) => Path.GetDirectoryName(folder) is { Length: > 0 } parent ? parent : null;

    private static string WorkingFolder()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception)
        {
            // The process edge: a working folder that cannot be read is a coai folder nobody can name.
            return string.Empty;
        }
    }
}

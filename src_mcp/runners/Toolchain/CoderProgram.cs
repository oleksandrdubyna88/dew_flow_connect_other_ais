using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The program a coder's unit runs, as the unit must be given it: its REAL path (a link of the person's home is not there inside the unit, and the unit execs the file itself), and the folder that holds it, bound back read-only because
/// the unit hides <c>/home</c> and replaces <c>/tmp</c> (epoch 3 E2 s2 t5).
/// </summary>
/// <param name="Real">The real path of the program's file. Empty when <paramref name="Problem"/> says why there is none.</param>
/// <param name="BindFolder">The folder to bind read-only, or empty when the file lies in a system folder the unit sees anyway.</param>
/// <param name="Problem">Why this program cannot be run inside the layer, or empty.</param>
internal sealed record CoderProgram(string Real, string BindFolder, string Problem)
{
    /// <summary>Folders the unit sees with no bind: <c>ProtectSystem=strict</c> keeps them, read-only.</summary>
    private static readonly string[] SystemFolders = ["/usr", "/bin", "/sbin", "/lib", "/lib64", "/opt"];

    /// <summary>
    /// Finds <paramref name="executable"/> (an absolute path, or a name looked up in <paramref name="path"/>) and says what the unit needs. The program must NOT lie inside the worktree or the job's folder: a coder that could write the
    /// binary it is launched with would be launched with its own binary next time.
    /// </summary>
    public static CoderProgram Resolve(string executable, string path, BoundaryJob job)
    {
        var found = Find(executable, path);
        if (found.Length == 0)
        {
            return Refused($"the program '{executable}' was not found: it must be an absolute path to a file, or a name on coai's own PATH");
        }

        var real = RealPath.Resolve(found);
        if (!real.Ok || !File.Exists(real.Path))
        {
            return Refused($"the program '{executable}' could not be resolved to a file ({real.Why})");
        }

        var folder = Path.GetDirectoryName(real.Path) ?? string.Empty;
        if (new[] { job.Tree.Root, job.JobRoot }.Any(inside => RealPath.IsInside(inside, real.Path, StringComparison.Ordinal)))
        {
            return Refused($"the program '{real.Path}' lies inside the worktree or the job folder, which the coder can write; a coder is never launched with a binary it could replace");
        }

        return !IsPlain(real.Path) || folder.Length == 0 || folder == Path.GetPathRoot(folder)
            ? Refused($"the program '{real.Path}' cannot be given to a unit: its folder must be absolute, not the root of the file system, and hold no space, colon, backslash, '=' or control character")
            : new CoderProgram(real.Path, SystemFolders.Any(system => RealPath.IsInside(system, folder, StringComparison.Ordinal)) ? string.Empty : folder, string.Empty);
    }

    private static CoderProgram Refused(string problem) => new(string.Empty, string.Empty, problem);

    private static bool IsPlain(string path) =>
        Path.IsPathFullyQualified(path) && !path.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ':' or '\\' or '=');

    private static string Find(string executable, string path)
    {
        if (Path.IsPathFullyQualified(executable))
        {
            return File.Exists(executable) ? executable : string.Empty;
        }

        if (executable.Length == 0 || executable.Contains('/', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathFullyQualified)
            .Select(folder => Path.Combine(folder, executable))
            .FirstOrDefault(File.Exists) ?? string.Empty;
    }
}

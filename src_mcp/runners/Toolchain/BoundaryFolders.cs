namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// What coai makes on the disk before a preflight and removes after it, for either boundary: the job's folders, the three marker files the preflight looks for from inside (in the unit's <c>/tmp</c>, its <c>/var/tmp</c> and the worktree),
/// and a copy of the Windows program the preflight tries to run from the worktree. One place, two callers (<see cref="CheckBoundary"/>, <see cref="CoderBoundary"/>).
/// </summary>
internal static class BoundaryFolders
{
    /// <summary>
    /// Makes <paramref name="folders"/> and the markers, and copies the Windows program into the worktree when the host has one. A sentence says why it could not; the copy's path is empty when there is none.
    /// </summary>
    /// <param name="host">The machine: whether it has a Windows program to try.</param>
    /// <param name="job">The job: its worktree and its id (the marker's name).</param>
    /// <param name="folders">Every folder the job's unit binds from the job's own root.</param>
    /// <param name="temp">The folder the unit sees as <c>/tmp</c>.</param>
    /// <param name="varTemp">The folder the unit sees as <c>/var/tmp</c>.</param>
    internal static (string Sentence, string Copy) Prepare(BoundaryHost host, BoundaryJob job, IReadOnlyList<string> folders, string temp, string varTemp)
    {
        try
        {
            foreach (var folder in folders)
            {
                Directory.CreateDirectory(folder);
            }

            WriteOwnFile(Path.Combine(temp, BoundaryPreflight.MarkerName(job)), source: null, executable: false);
            WriteOwnFile(Path.Combine(varTemp, BoundaryPreflight.MarkerName(job)), source: null, executable: false);
            WriteOwnFile(Path.Combine(job.Tree.Root, BoundaryPreflight.MarkerName(job)), source: null, executable: false);

            return (string.Empty, CopyProgram(host, job));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ($"The job folders could not be made, so the job was refused: {e.Message}", string.Empty);
        }
    }

    private static string CopyProgram(BoundaryHost host, BoundaryJob job)
    {
        if (host.WindowsProgram.Length == 0 || !File.Exists(host.WindowsProgram))
        {
            return string.Empty;
        }

        var copy = Path.Combine(job.Tree.Root, BoundaryPreflight.ProgramCopyName);
        WriteOwnFile(copy, host.WindowsProgram, executable: true);

        return copy;
    }

    /// <summary>
    /// M16 of pass 3: empties the job's home and its two temp folders (the folders stay: they are bind sources). Called before AND after every check step, so a step's writes — a configuration in the home that MSBuild or npm reads by itself, a file in the temp folder — are seen by no other step and
    /// no other round. A directory a step made read-only is made writable first. Links are removed, never followed. A sentence says what could not be removed; empty when all is empty.
    /// </summary>
    internal static string EmptyForStep(BoundaryJob job)
    {
        foreach (var folder in new[] { job.HomeFolder, job.TempFolder, job.VarTempFolder })
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Files.GitScratch.DeleteLeavingLinkTargetsUnread(folder);
                }

                Directory.CreateDirectory(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return $"a folder of the job could not be emptied between two steps, so the step was not run: {e.GetType().Name}";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Makes a file of coai's own, on the host, in a folder that a unit can write (the unit's temp folders, the worktree): the name is looked at WITHOUT following it, a file or a link at it is deleted by its own name (that removes a link, never its
    /// target), a folder or anything else is a stop, and the file is made with <see cref="FileMode.CreateNew"/> — a link planted between the delete and the create makes the create fail, and is never followed (verify M15: a link at the name used to
    /// truncate, or fill, any host file the person could write, or to make one that was not there).
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="source">The file whose bytes are copied in; <c>null</c>: an empty file.</param>
    /// <param name="executable">Whether the file is made mode 0700 (the program's copy) — on the open handle, never by path.</param>
    private static void WriteOwnFile(string path, string? source, bool executable)
    {
        switch (Files.RealPath.Lstat(path).Kind)
        {
            case Files.EntryKind.File or Files.EntryKind.Link:
                File.Delete(path);
                break;
            case Files.EntryKind.Directory:
                throw new IOException("a folder stands at the name of a file of coai's own, so it was not written");
        }

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = executable
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        }

        using var stream = new FileStream(path, options);
        if (source is not null)
        {
            using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            from.CopyTo(stream);
        }

        if (executable && !OperatingSystem.IsWindows())
        {
            // The creation mode is cut by the umask; the handle is the file just made, so this cannot reach another.
            File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Removes the Windows program's copy and the three markers, as soon as the preflight unit has ended.</summary>
    internal static void Tidy(BoundaryJob job, string copy, string temp, string varTemp)
    {
        TryDelete(copy);
        TryDelete(Path.Combine(temp, BoundaryPreflight.MarkerName(job)));
        TryDelete(Path.Combine(varTemp, BoundaryPreflight.MarkerName(job)));
        TryDelete(Path.Combine(job.Tree.Root, BoundaryPreflight.MarkerName(job)));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (path.Length > 0)
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The marker is the job's own file in the job's own folder; a copy of a program that cannot be removed is reported by the next git status, not here.
        }
    }
}

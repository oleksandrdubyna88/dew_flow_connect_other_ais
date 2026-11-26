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

            File.WriteAllText(Path.Combine(temp, BoundaryPreflight.MarkerName(job)), string.Empty);
            File.WriteAllText(Path.Combine(varTemp, BoundaryPreflight.MarkerName(job)), string.Empty);
            File.WriteAllText(Path.Combine(job.Tree.Root, BoundaryPreflight.MarkerName(job)), string.Empty);

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
        File.Copy(host.WindowsProgram, copy, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(copy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

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

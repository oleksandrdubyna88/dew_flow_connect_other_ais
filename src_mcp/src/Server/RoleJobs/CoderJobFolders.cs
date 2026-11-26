using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What a coder job leaves on the disk beside its record, and when coai takes it away (epoch 3 E2, the retention rule the proof run found missing): the submodule mounts of a job go when its work ends, and the job's own folder (the coder's home, the checks' folders) goes
/// before the job's record when <see cref="RoleJobRetention"/> removes it. The worktree and its branch are NOT here: they hold the coder's commit, and what happens to them is the person's.
/// </summary>
/// <remarks>
/// <para><b>Only coai's own folders, by name.</b> A folder is removed only when it is <c>&lt;coder-jobs&gt;/&lt;job id&gt;</c> or that name plus <c>.mounts</c>, for a job id of the strict shape (<see cref="CoderWorktree.IsJobId"/>), and only a real folder: a link in its place is
/// LEFT (it is not coai's folder, and it is not followed). A link INSIDE a folder is removed by its own name and its target is not even listed (<see cref="GitScratch.DeleteLeavingLinkTargetsUnread"/>): the checks wrote into the job's home, so a link there may point at <c>/proc</c> or a
/// dropped share. A folder that cannot be removed stays; for a retained job the record stays with it, so the next retention finds it again.</para>
/// <para><b>The mounts at the end of the work, not of the record.</b> A mount's source is read by the job's units only; once the work is over (checks included) nothing reads it. It is not removed by a runner that lost the job (a cancel or a sweep fenced it): the job may be
/// running again in another runner, on the same folder.</para>
/// <para><b>Where it looks.</b> Under the coder-jobs folder of the CALLER's settings. A data folder shared by two machines, or <c>COAI_CODER_JOBS</c> changed since the job ran, leaves the folders of a job that ran elsewhere where they are: the record does not say where they were made.</para>
/// </remarks>
internal static class CoderJobFolders
{
    private enum Outcome
    {
        /// <summary>A real folder, removed.</summary>
        Removed,

        /// <summary>Nothing is there, or what is there is not a folder of coai's (a link or a file in its place): nothing of ours is left.</summary>
        NothingOfOurs,

        /// <summary>It is a folder and could not be removed.</summary>
        Stays,
    }

    /// <summary>Removes the submodule mounts folder of <paramref name="job"/> — the folder beside its job folder that holds the mounts' sources. True when a folder was removed.</summary>
    public static bool RemoveMounts(BoundaryJob job, string jobId, Action<Exception> failed) =>
        Named(job.JobRoot, jobId) && Remove(job.MountsFolder, failed) == Outcome.Removed;

    /// <summary>
    /// Removes what the coder job <paramref name="jobId"/> left in the coder-jobs folder named by <paramref name="settings"/> (<see cref="CoderOsLayer.JobsVariable"/> or the machine-local folder): its job folder, its mounts folder and the stand-in the check preflight may have left beside it. Never anything but those three names.
    /// True when none of them is left as a folder of coai's: the record of the job may go.
    /// </summary>
    public static bool RemoveOf(Func<string, string?> settings, string jobId, Action<Exception> failed)
    {
        if (!CoderWorktree.IsJobId(jobId))
        {
            return true;
        }

        var folder = CoderOsLayer.JobFolder(settings, _ => { }, jobId);
        var first = Remove(folder, failed);
        var second = Remove(folder + ".mounts", failed);
        // The preflight's stand-in for another job's folder (K2) lies beside the job folder; a killed runner leaves it (security verify pass 2, S13).
        var third = Remove(Path.Combine(Path.GetDirectoryName(folder) ?? string.Empty, CheckBoundary.StandInName(jobId)), failed);

        return first != Outcome.Stays && second != Outcome.Stays && third != Outcome.Stays;
    }

    /// <summary>Whether the job folder is the one named for <paramref name="jobId"/>: its last segment is the id, which is of the strict shape.</summary>
    private static bool Named(string jobRoot, string jobId) =>
        CoderWorktree.IsJobId(jobId) && string.Equals(Path.GetFileName(jobRoot.TrimEnd(Path.DirectorySeparatorChar)), jobId, StringComparison.Ordinal);

    private static Outcome Remove(string folder, Action<Exception> failed)
    {
        try
        {
            if (Runners.Files.RealPath.Lstat(folder).Kind is not Runners.Files.EntryKind.Directory)
            {
                return Outcome.NothingOfOurs;
            }

            GitScratch.DeleteLeavingLinkTargetsUnread(folder);

            return Outcome.Removed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failed(e);

            return Outcome.Stays;
        }
    }
}

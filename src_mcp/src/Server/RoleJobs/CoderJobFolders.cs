using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What a coder job leaves on the disk beside its record, and when coai takes it away (epoch 3 E2, the retention rule the proof run found missing): the submodule mounts of a job go when its work ends, and the job's own folder (the coder's home, the checks' folders) goes
/// with the job's record when <see cref="RoleJobRetention"/> removes it. The worktree and its branch are NOT here: they hold the coder's commit, and what happens to them is the person's.
/// </summary>
/// <remarks>
/// <para><b>Only coai's own folders, by name.</b> A folder is removed only when it is <c>&lt;coder-jobs&gt;/&lt;job id&gt;</c> or that name plus <c>.mounts</c>, for a job id of the strict shape (<see cref="CoderWorktree.IsJobId"/>), and only a real folder: a link in its place is not
/// followed (<see cref="GitScratch.DeleteEvenIfReadOnly"/> unlinks it) and nothing else is searched for. A folder that cannot be removed stays and is tried again; no answer to a caller depends on it.</para>
/// <para><b>The mounts at the end of the work, not of the record.</b> A mount's source is read by the job's units only; once the work is over (checks included) nothing reads it. It is not removed by a runner that lost the job (a cancel or a sweep fenced it): the job may be
/// running again in another runner, on the same folder.</para>
/// </remarks>
internal static class CoderJobFolders
{
    /// <summary>Removes the submodule mounts folder of <paramref name="job"/> — the folder beside its job folder that holds the mounts' sources. True when a folder was removed.</summary>
    public static bool RemoveMounts(BoundaryJob job, string jobId, Action<Exception> failed) =>
        Named(job.JobRoot, jobId) && Remove(job.MountsFolder, failed);

    /// <summary>
    /// Removes what the coder job <paramref name="jobId"/> left in the coder-jobs folder named by <paramref name="settings"/> (<see cref="CoderOsLayer.JobsVariable"/> or the machine-local folder): its job folder and its mounts folder. Never anything but those two names.
    /// </summary>
    public static void RemoveOf(Func<string, string?> settings, string jobId, Action<Exception> failed)
    {
        if (!CoderWorktree.IsJobId(jobId))
        {
            return;
        }

        var folder = CoderOsLayer.JobFolder(settings, _ => { }, jobId);
        Remove(folder, failed);
        Remove(folder + ".mounts", failed);
    }

    /// <summary>Whether the job folder is the one named for <paramref name="jobId"/>: its last segment is the id, which is of the strict shape.</summary>
    private static bool Named(string jobRoot, string jobId) =>
        CoderWorktree.IsJobId(jobId) && string.Equals(Path.GetFileName(jobRoot.TrimEnd(Path.DirectorySeparatorChar)), jobId, StringComparison.Ordinal);

    private static bool Remove(string folder, Action<Exception> failed)
    {
        try
        {
            if (Runners.Files.RealPath.Lstat(folder).Kind is not Runners.Files.EntryKind.Directory)
            {
                return false;
            }

            GitScratch.DeleteEvenIfReadOnly(folder);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failed(e);

            return false;
        }
    }
}

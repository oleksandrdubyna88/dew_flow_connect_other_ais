using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The retention of job folders (spec A): a terminal job folder goes after 30 days, and at most 200 terminal folders are kept — the newest. Never a folder whose lease is held, never one that is
/// not over. The rows of the rounds database stay (about 1 KB each): a job's folder is the evidence, its row is the history.
/// </summary>
/// <remarks>
/// A folder that cannot be removed (a file in use, a permission) stays and is tried again at the next call; no answer to a caller depends on it. What a folder without a readable
/// <c>job.json</c> is, is not decided here — it is not counted and not removed, because it may be a job being made.
/// </remarks>
internal static class RoleJobRetention
{
    /// <summary>Removes what the rules say, and answers the ids it removed.</summary>
    /// <param name="store">The jobs' store.</param>
    /// <param name="log">The log.</param>
    /// <param name="settings">When given, a removed CODER job's own folder and mounts folder go with it (<see cref="CoderJobFolders"/>) — they are found by the job's id under the coder-jobs folder these settings name, and nowhere else.</param>
    public static IReadOnlyList<string> Apply(RoleJobStore store, Serilog.ILogger log, Func<string, string?>? settings = null) =>
        Apply(store, store.Clock.GetUtcNow().UtcDateTime, RoleJobLimits.RetainFor, RoleJobLimits.RetainAtMost, log, settings);

    internal static IReadOnlyList<string> Apply(RoleJobStore store, DateTime nowUtc, TimeSpan keepFor, int atMost, Serilog.ILogger log, Func<string, string?>? settings = null)
    {
        var over = store.JobIds()
            .Select(id => store.Read(id))
            .OfType<RoleJobRecord>()
            .Where(job => job.Terminal)
            .OrderByDescending(job => job.EndedUtc ?? job.UpdatedUtc)
            .ToList();

        var doomed = over.Where((job, rank) => rank >= atMost || nowUtc - (job.EndedUtc ?? job.UpdatedUtc) > keepFor).ToList();
        var removed = new List<string>();
        foreach (var job in doomed)
        {
            if (Remove(store, job, settings, log))
            {
                removed.Add(job.JobId);
            }
        }

        return removed;
    }

    private static bool Remove(RoleJobStore store, RoleJobRecord job, Func<string, string?>? settings, Serilog.ILogger log)
    {
        var jobId = job.JobId;
        if (store.FolderOf(jobId) is not { } folder || HeldFile.TryHold(folder.LeaseFile) is not { } held)
        {
            // The lease is held: a runner is still finishing in a folder it has already marked over. Not now.
            return false;
        }

        // A coder job's own folders go BEFORE its record, under the lease: when one cannot be removed the record stays, and the next retention finds the job again.
        if (settings is not null && job.Role == RoleDescriptors.Coder
            && !CoderJobFolders.RemoveOf(settings, jobId, e => log.Warning(e, "the folders of coder job {JobId} could not be removed; the job's record stays until they are", jobId)))
        {
            held.Dispose();

            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            // An open file cannot be deleted there; the hold is let go and the folder is a terminal job, whose runner has nothing left to write.
            held.Dispose();
        }

        try
        {
            Runners.Files.GitScratch.DeleteEvenIfReadOnly(folder.Location);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning(e, "role job folder {JobId} could not be removed; it stays until the next retention", jobId);

            return false;
        }
        finally
        {
            held.Dispose();
        }
    }
}

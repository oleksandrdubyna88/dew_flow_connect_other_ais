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
    public static IReadOnlyList<string> Apply(RoleJobStore store, Serilog.ILogger log) =>
        Apply(store, store.Clock.GetUtcNow().UtcDateTime, RoleJobLimits.RetainFor, RoleJobLimits.RetainAtMost, log);

    internal static IReadOnlyList<string> Apply(RoleJobStore store, DateTime nowUtc, TimeSpan keepFor, int atMost, Serilog.ILogger log)
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
            if (Remove(store, job.JobId, log))
            {
                removed.Add(job.JobId);
            }
        }

        return removed;
    }

    private static bool Remove(RoleJobStore store, string jobId, Serilog.ILogger log)
    {
        if (store.FolderOf(jobId) is not { } folder || HeldFile.TryHold(folder.LeaseFile) is not { } held)
        {
            // The lease is held: a runner is still finishing in a folder it has already marked over. Not now.
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

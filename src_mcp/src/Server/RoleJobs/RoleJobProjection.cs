using CoaiMcp.Core.Notices;
using CoaiMcp.Store;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// A job file as rows of the rounds database (schema step 26): the one place the files are turned into a projection, handed to <see cref="RoleJobStore"/> as its <c>project</c> action and run
/// through <see cref="Projection"/> — which swallows every way a database can fail. The rows are rewritten whole each time, so the view catches up at the next change.
/// </summary>
internal static class RoleJobProjection
{
    /// <summary>The action a store is built with: opens the database, writes the job and its attempts, and never lets a failure out.</summary>
    public static Action<RoleJobRecord> For(string dataDir, Serilog.ILogger log)
    {
        var projection = new Projection(dataDir, log);

        return job => projection.Write(db => RoleJobsTable.Record(db, RowOf(job), [.. job.Attempts.Select(attempt => AttemptRowOf(job.JobId, attempt))]), $"role job {job.JobId}");
    }

    internal static RoleJobRow RowOf(RoleJobRecord job) => new(
        job.JobId, job.Role, job.Repo, job.WorkItem, job.TaskFile, job.State, job.Generation, job.Launcher,
        Stamp(job.CreatedUtc), Stamp(job.StartedUtc), Stamp(job.EndedUtc), Stamp(job.UpdatedUtc),
        job.Model, job.Attempts.Count, job.Stop, job.Reason, job.RepairTurns, job.Complexity, job.Security);

    internal static RoleAttemptRow AttemptRowOf(string jobId, RoleAttempt attempt) => new(
        jobId, attempt.N, attempt.Model, attempt.Vendor, Stamp(attempt.StartedUtc), Stamp(attempt.EndedUtc), attempt.Stop, attempt.Reason, attempt.TokensIn, attempt.TokensOut, attempt.KeyId);

    /// <summary>The instant as the notices and the rounds write it — millisecond precision, always <c>Z</c> — or empty for "not yet".</summary>
    private static string Stamp(DateTime? utc) =>
        utc is { } at ? ServerNotice.Iso(new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))) : string.Empty;
}

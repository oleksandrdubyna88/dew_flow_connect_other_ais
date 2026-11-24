namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The start-up sweep of the role jobs (spec A): when a server starts it takes one look at every job that is not over — a runner that died while no server was watching has its job closed as
/// <c>lost</c> and its recorded child stopped — and runs the retention. In the background and never fatal: a start must not wait on it or fail because of it.
/// </summary>
internal static class RoleJobStartup
{
    public static Task SweepInBackground(string dataDir, Func<string, string?> settings, Serilog.ILogger log, Noticing noticing, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                await new RoleJobService(dataDir, settings, log, noticing.Offer).SweepAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // The server is ending; the next start looks again.
            }
            catch (Exception e)
            {
                // The detached edge: a sweep that fails is a line in the log and a retry at the next start, never a server that does not start.
                log.Warning(e, "the role jobs could not be swept at start");
            }
        }, CancellationToken.None);
}

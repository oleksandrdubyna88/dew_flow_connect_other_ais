namespace CoaiServer;

/// <summary>
/// The loop that starts queued reviews and stops ones that have run out of time.
/// </summary>
/// <remarks>
/// <para><b>The sweep is on a TIMER, not on an event.</b> An event-driven deadline is only checked
/// when something happens, so a running job whose deadline passes on an otherwise idle server would
/// go on running and go on spending. The plan round raised exactly that.</para>
/// <para>A short tick, because the alternative to polling here is a scheduler that has to be woken
/// by slot releases, job submissions AND deadlines — three sources for a loop that costs a dictionary
/// scan a second on a box with at most a handful of jobs.</para>
/// </remarks>
public sealed class JobPump(
    JobStore jobs,
    VendorCatalogHost catalog,
    JobRunner runner,
    ILogger<JobPump> log,
    TimeSpan? tick = null) : BackgroundService
{
    private readonly TimeSpan _tick = tick ?? TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_tick);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Expire();
                StartWhatCanRun(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // The pump is the only thing that starts work. If it dies, the server accepts reviews
                // for ever and runs none of them — and answers 202 the whole time. So it survives
                // anything, loudly.
                log.LogError(e, "the job pump failed a tick and is continuing");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private void Expire()
    {
        foreach (var job in jobs.Sweep(DateTimeOffset.UtcNow))
        {
            log.LogInformation(
                "job {Id} for {Email} {Reason}", job.Id, job.Email, JobTransitions.ExpiryReason(job));
        }
    }

    /// <summary>Give every vendor a chance to start one job, without waiting for any of them.</summary>
    /// <remarks>
    /// The run itself continues on the thread pool: awaiting a ten-minute review here would stop
    /// every other vendor's queue for ten minutes. It is not fire-and-forget, though — a dropped task
    /// is a swallowed exception, and the one thing that starts work must not fail silently. Each is
    /// given a continuation that logs. Back-pressure comes from the slot lock: while a job holds an
    /// account, the next tick finds nothing free and does nothing.
    /// </remarks>
    private void StartWhatCanRun(CancellationToken ct)
    {
        foreach (var vendor in catalog.Current.Vendors)
        {
            var id = vendor.Id;
            _ = runner.PumpAsync(id, ct).ContinueWith(
                t => log.LogError(t.Exception, "starting a job for {Vendor} threw", id),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}

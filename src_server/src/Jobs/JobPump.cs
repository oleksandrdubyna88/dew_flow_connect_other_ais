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
                await StartWhatCanRunAsync(stoppingToken);
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
            // The store's own sentence, not a second one worked out here. It has already decided
            // WHICH clock ran out — an abandoned job and a timed-out one read very differently, and
            // recomputing after the record went terminal would have got the answer wrong.
            //
            // Guarded, because the arguments are not constants and a sweep on a busy server walks
            // every expired job to build them for a sink that may be discarding them. (CA1873.)
            if (log.IsEnabled(LogLevel.Information))
            {
                log.LogInformation("job {Id} for {Email} {Reason}", job.Id, job.Email, job.Reason);
            }
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
    /// <summary>Start everything that CAN start, without waiting for any of it to finish.</summary>
    /// <remarks>
    /// <para>Keeps pumping a vendor while it keeps saying yes. Starting one review per vendor per
    /// tick meant a vendor with ten free accounts and a full queue started one a second and left
    /// nine accounts idle — the tick became the throughput limit instead of the accounts.</para>
    /// <para>The runs themselves are not awaited: a ten-minute review would otherwise stop every
    /// other vendor's queue for ten minutes. Not fire-and-forget, though — each gets a continuation
    /// that logs, because the one thing that starts work must not fail silently. Back-pressure is
    /// the slot lock: `PumpAsync` returns false the moment no account is free, which is what ends
    /// this loop.</para>
    /// </remarks>
    private async Task StartWhatCanRunAsync(CancellationToken ct)
    {
        foreach (var vendor in catalog.Current.Vendors)
        {
            var id = vendor.Id;
            // Bounded by the number of accounts this vendor HAS: one tick can start at most that
            // many, because that is the most that can be running at once. A bare `while` was correct
            // in principle — a claim is progress and the queue is finite — but it is the kind of
            // correct that stops being true after somebody edits the claim path, and this loop runs
            // once a second for ever. (Second code round.)
            var starts = Math.Max(vendor.Slots.Count, 1);
            while (starts-- > 0 && !ct.IsCancellationRequested && await CanStartAsync(id, ct))
            {
                // The body is empty on purpose: CanStartAsync did the starting.
            }
        }
    }

    /// <summary>Claim and start one job, and say whether there might be another.</summary>
    private async Task<bool> CanStartAsync(string vendorId, CancellationToken ct)
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = runner.PumpAsync(vendorId, ct, started);
        _ = run.ContinueWith(
            t => log.LogError(t.Exception, "running a job for {Vendor} threw", vendorId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return await started.Task;
    }
}

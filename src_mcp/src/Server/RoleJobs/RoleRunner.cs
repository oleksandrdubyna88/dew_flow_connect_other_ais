using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The detached runner of ONE job (<c>coai-mcp --role-run &lt;jobId&gt;</c>): takes the lease, restores the environment, runs the work while it beats and watches for a cancel, then writes the
/// result or the terminal state — each write compared with the lease's generation first (spec A).
/// </summary>
/// <remarks>
/// <para><b>Exit codes.</b> <b>0</b>: this runner took the job to a terminal state (whatever it was). <b>65</b>: no such job — the id is the wrong shape, there is no folder or no request.
/// <b>75</b> (EX_TEMPFAIL): this runner is not, or is no longer, the writer — another process holds the lease, the job is already over, or its generation was taken over (a cancel, a sweep);
/// it published nothing and stopped its child. <b>70</b>: an unexpected fault after the lease was taken; the job was closed as <c>failed</c> with stop <c>runner-error</c>. Never 64.</para>
/// <para><b>Why it stops.</b> One token reaches the work. It fires for a cancel (the <c>cancel</c> file, looked at every second), for the job's own timeout, for a signal to this process
/// (<c>systemctl stop</c>, a terminal's kill) and for a lost lease — and the FIRST cause is the one the terminal state names, so a job that was cancelled is never reported as a timeout that
/// happened to follow.</para>
/// <para><b>It is the only writer of its CLI child</b> — through <see cref="RoleChildTracker"/> — and the server does not track it. A runner killed outright leaves the child recorded in
/// <c>job.json</c>; the keeper that finds the lease free stops it by identity (<see cref="RoleJobKeeper"/>).</para>
/// </remarks>
internal sealed class RoleRunner(RoleJobStore store, IReadOnlyList<RoleDescriptor> descriptors, TimeProvider clock, Action<string> note, Serilog.ILogger log)
{
    internal const int Done = 0;
    internal const int NoSuchJob = 65;
    internal const int Fault = 70;
    internal const int NotTheWriter = 75;

    /// <summary>The cadence of the loop that watches for a cancel (a second); the heartbeat is written every <see cref="HeartbeatEvery"/>. A test shortens both.</summary>
    internal TimeSpan WatchEvery { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often the heartbeat is written: <see cref="RoleJobLimits.HeartbeatEvery"/>, 15 seconds.</summary>
    internal TimeSpan HeartbeatEvery { get; init; } = RoleJobLimits.HeartbeatEvery;

    /// <summary>Where the runner's notices go (a model switch, a failed job): <c>ServerNotices.Append</c> over the data directory in the real runner. Without one a notice is dropped.</summary>
    internal Func<ServerNotice, bool> Notice { get; init; } = _ => false;

    /// <summary>The settings the work reads — the environment over the data directory's settings file — asked AFTER the job's environment was restored, so it sees what the start captured.</summary>
    internal Func<Func<string, string?>> SettingsOf { get; init; } = () => Environment.GetEnvironmentVariable;

    public async Task<int> RunAsync(string jobId, CancellationToken stop)
    {
        if (store.FolderOf(jobId) is not { Exists: true } folder || folder.ReadRequest() is not { } request)
        {
            note($"--role-run: there is no job '{jobId}' in this data directory");

            return NoSuchJob;
        }

        var self = new RoleChild(Environment.ProcessId, ProcessTracking.StartedAt(Environment.ProcessId) ?? clock.GetUtcNow().UtcDateTime);
        using var lease = await store.TakeWithinAsync(
            jobId,
            job => job with { State = RoleJobStates.Starting, Runner = self, StartedUtc = job.StartedUtc ?? clock.GetUtcNow().UtcDateTime },
            stop);
        if (lease is null)
        {
            note($"--role-run: job '{jobId}' is held by another runner, or is already over; nothing was started");

            return NotTheWriter;
        }

        log.Information("role job {JobId} ({Role}) taken at generation {Generation} by pid {Pid}", jobId, request.Role, lease.Generation, self.Pid);
        lease.Beat("starting", 0, 0);
        try
        {
            return await RunTakenAsync(folder, request, lease, stop);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The detached edge of the runner: nothing may leave it as a crash that leaves the job at `running` until a sweep. The job is closed, and the fault is in the log.
            log.Error(e, "role job {JobId} failed inside its runner", jobId);
            if (Close(lease, current => current with { State = RoleJobStates.Failed, Stop = "runner-error", Reason = $"the runner failed: {e.GetType().Name}: {Redaction.SafeText(e.Message, Redaction.TitleLimit)}", EndedUtc = clock.GetUtcNow().UtcDateTime, Child = null }) is RoleJobWrite.Written { Job: var faulted })
            {
                Notice(RoleJobNotices.Failed(faulted, clock.GetUtcNow().UtcDateTime));
            }

            return Fault;
        }
    }

    private async Task<int> RunTakenAsync(RoleJobFolder folder, RoleJobRequest request, RoleLease lease, CancellationToken stop)
    {
        if (folder.TakeEnvironment() is { } environment)
        {
            RoleRunEnvironment.Apply(environment);
        }

        if (RoleDescriptors.Find(descriptors, request.Role) is not { } descriptor)
        {
            return Finished(Close(lease, job => job with { State = RoleJobStates.Failed, Stop = "no-descriptor", Reason = $"this build has no work for role '{request.Role}'", EndedUtc = clock.GetUtcNow().UtcDateTime, Child = null }));
        }

        if (lease.Write(job => job with { State = RoleJobStates.Running }) is not RoleJobWrite.Written { Job: var running })
        {
            return NotTheWriter;
        }

        using var watch = new RunWatch(clock, folder, lease, stop, TimeSpan.FromMinutes(Math.Max(1, request.TimeoutMinutes)), WatchEvery, HeartbeatEvery);
        var outcome = await RunWorkAsync(descriptor, new RunContext(request, running, folder.Location, store.DataDir, SettingsOf(), log, clock, Notice, lease, watch), watch);
        await watch.EndedAsync();

        return Finish(lease, outcome, watch.Why, request);
    }

    private static async Task<RoleWorkOutcome> RunWorkAsync(RoleDescriptor descriptor, RunContext run, RunWatch watch)
    {
        try
        {
            return await descriptor.CreateWork().RunAsync(run, watch.Token);
        }
        catch (OperationCanceledException)
        {
            return new RoleWorkOutcome.Stopped();
        }
    }

    /// <summary>The terminal write for what the work came to, compared with the lease first; 75 when this runner is no longer the writer.</summary>
    private int Finish(RoleLease lease, RoleWorkOutcome outcome, StopWhy why, RoleJobRequest request)
    {
        if (why == StopWhy.Stale || !lease.IsCurrent())
        {
            note("the job was taken over while it ran; nothing was published");

            return NotTheWriter;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var written = outcome switch
        {
            RoleWorkOutcome.Succeeded done => lease.Publish(done.ResultJson, job => job with { State = RoleJobStates.Succeeded, EndedUtc = now, Child = null }),
            RoleWorkOutcome.Failed failed => Close(lease, job => job with { State = RoleJobStates.Failed, Stop = failed.Stop, Reason = failed.Reason, EndedUtc = now, Child = null }),
            _ => Close(lease, job => Stopped(job, why, request, now)),
        };

        if (written is RoleJobWrite.Written { Job: { State: RoleJobStates.Failed } failedJob })
        {
            Notice(RoleJobNotices.Failed(failedJob, now));
        }

        return Finished(written);
    }

    private static RoleJobRecord Stopped(RoleJobRecord job, StopWhy why, RoleJobRequest request, DateTime now) => why switch
    {
        StopWhy.Cancel => job with { State = RoleJobStates.Cancelled, Stop = "cancelled", Reason = "cancelled by role_cancel", EndedUtc = now, Child = null },
        StopWhy.Timeout => job with { State = RoleJobStates.Failed, Stop = "timeout", Reason = $"the job ran past its {request.TimeoutMinutes} minute budget", EndedUtc = now, Child = null },
        _ => job with { State = RoleJobStates.Failed, Stop = "stopped", Reason = "the runner was stopped by a signal", EndedUtc = now, Child = null },
    };

    private static RoleJobWrite Close(RoleLease lease, Func<RoleJobRecord, RoleJobRecord> change) => lease.Write(change);

    private static int Finished(RoleJobWrite written) => written is RoleJobWrite.Written ? Done : NotTheWriter;

    /// <summary>Why the work's token fired — the FIRST cause wins.</summary>
    internal enum StopWhy
    {
        None = 0,
        Cancel,
        Timeout,
        Signal,
        Stale,
    }

    private sealed class RunContext(
        RoleJobRequest request, RoleJobRecord job, string folder, string dataDir, Func<string, string?> settings, Serilog.ILogger log, TimeProvider clock, Func<ServerNotice, bool> notice, RoleLease lease, RunWatch watch) : IRoleRun
    {
        public RoleJobRequest Request => request;

        public string Folder => folder;

        public IProcessTracker Children => watch.Tracker;

        public void Phase(string phase, int attempt) => watch.SetPhase(phase, attempt);

        public RoleJobRecord Job => job;

        public string DataDir => dataDir;

        public Func<string, string?> Settings => settings;

        public Serilog.ILogger Log => log;

        public TimeProvider Clock => clock;

        public bool Notice(ServerNotice offered)
        {
            try
            {
                return notice(offered);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Warning(e, "a notice of role job {JobId} could not be written", request.JobId);

                return false;
            }
        }

        public bool Trying(string model) => Written(lease.Write(current => current with { Model = model }));

        public bool Attempted(RoleAttempt attempt) => Written(lease.Write(current => current with { Attempts = [.. current.Attempts, attempt] }));

        public bool Repaired(int turns) => Written(lease.Write(current => current with { RepairTurns = turns }));

        public bool StillWriter() => lease.IsCurrent();

        /// <summary>A write the lease refused as stale means somebody took the job over: the work must stop, and the watch is told so.</summary>
        private bool Written(RoleJobWrite written)
        {
            if (written is RoleJobWrite.Stale or RoleJobWrite.Over)
            {
                watch.Stale();
            }

            return written is RoleJobWrite.Written;
        }
    }

    /// <summary>The loop beside the work: the heartbeat, the look for a cancel, the timeout and the signal — one token out, one cause kept.</summary>
    private sealed class RunWatch : IDisposable
    {
        private readonly RoleJobFolder _folder;
        private readonly RoleLease _lease;
        private readonly CancellationTokenSource _linked;
        private readonly CancellationTokenSource _timeout;
        private readonly CancellationTokenSource _ending = new();
        private readonly CancellationTokenRegistration _signal;
        private readonly TimeProvider _clock;
        private readonly Task _loop;
        private readonly TimeSpan _watchEvery;
        private readonly int _beatEvery;
        private int _why;
        private string _phase = "running";
        private int _attempt;

        internal RunWatch(TimeProvider clock, RoleJobFolder folder, RoleLease lease, CancellationToken stop, TimeSpan budget, TimeSpan watchEvery, TimeSpan heartbeatEvery)
        {
            _watchEvery = watchEvery;
            _beatEvery = Math.Max(1, (int)(heartbeatEvery / watchEvery));
            _folder = folder;
            _lease = lease;
            _clock = clock;
            _linked = CancellationTokenSource.CreateLinkedTokenSource(stop);
            _timeout = new CancellationTokenSource(budget, _clock);
            Tracker = new RoleChildTracker(lease, () => Fire(StopWhy.Stale));
            _signal = stop.Register(() => Fire(StopWhy.Signal));
            _timeout.Token.Register(() => Fire(StopWhy.Timeout));
            // The loop must run even when the stop token is already cancelled: it is what reads the stop and fires it. Not cancellable at start, said out loud.
            _loop = Task.Run(() => LoopAsync(_ending.Token), CancellationToken.None);
        }

        public RoleChildTracker Tracker { get; }

        public CancellationToken Token => _linked.Token;

        public StopWhy Why => (StopWhy)Volatile.Read(ref _why);

        public void SetPhase(string phase, int attempt)
        {
            _phase = phase;
            _attempt = attempt;
        }

        public async Task EndedAsync()
        {
            await _ending.CancelAsync();
            await _loop;
        }

        /// <summary>The lease is no longer this runner's: the first cause, unless an earlier one stands.</summary>
        internal void Stale() => Fire(StopWhy.Stale);

        private void Fire(StopWhy why)
        {
            Interlocked.CompareExchange(ref _why, (int)why, 0);
            _linked.Cancel();
        }

        private async Task LoopAsync(CancellationToken ending)
        {
            var ticks = 0;
            using var timer = new PeriodicTimer(_watchEvery, _clock);
            try
            {
                while (await timer.WaitForNextTickAsync(ending))
                {
                    if (_folder.CancelRequested)
                    {
                        Fire(StopWhy.Cancel);
                    }

                    if (++ticks % _beatEvery == 0 && !_lease.Beat(_phase, _attempt, Tracker.ChildPid))
                    {
                        Fire(StopWhy.Stale);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The work ended; the loop's job is over.
            }
        }

        public void Dispose()
        {
            _signal.Dispose();
            _timeout.Dispose();
            _linked.Dispose();
            _ending.Dispose();
        }
    }
}

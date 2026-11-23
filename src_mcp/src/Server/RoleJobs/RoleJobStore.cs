using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The role jobs of one data directory: <c>&lt;dataDir&gt;/roles/jobs/&lt;jobId&gt;/</c>. The job FILES are the authority (decision 1 of spec section 0); the rounds database is a projection of
/// them, handed to <c>project</c> after each write and never allowed to change one (<see cref="Projected"/>).
/// </summary>
/// <remarks>
/// <para><b>Lost only when the OS lock is free</b> (decision 2). <see cref="Observe"/> answers three different facts and keeps them apart: the lease is HELD and the heartbeat is fresh (running);
/// the lease is held and the heartbeat is older than two minutes (<c>stalled</c> — a runner that is stopped or hung is still the runner, and nothing may take its job); the lease is FREE and
/// the job is not over (a dead runner — <c>lost</c>, after a grace so a runner still starting is not taken for one).</para>
/// <para><b>One door for the lease.</b> <see cref="TryTake"/> opens <c>lease</c> exclusively and adds one to <c>generation</c> under <c>job.lock</c>; of two takers racing, one gets the file and
/// the other gets null, so the generation moves once. <see cref="BumpGeneration"/> is the other writer of the number: a cancel fences a runner it cannot kill.</para>
/// </remarks>
internal sealed class RoleJobStore(string dataDir, TimeProvider clock, Serilog.ILogger log, Action<RoleJobRecord>? project = null)
{
    /// <summary>How long a runner keeps trying for its lease: an observer's probe holds the file for microseconds, and a runner that gave up on that would leave a job nobody runs.</summary>
    internal static readonly TimeSpan TakeWindow = TimeSpan.FromSeconds(2);

    public string JobsRoot => Path.Combine(dataDir, "roles", "jobs");

    public TimeProvider Clock => clock;

    /// <summary>The folder of a job, or null for an id that is not the exact shape of one (so no caller can name a folder outside the root).</summary>
    public RoleJobFolder? FolderOf(string jobId) => RoleJobIds.IsValid(jobId) ? new RoleJobFolder(Path.Combine(JobsRoot, jobId)) : null;

    /// <summary>Makes the job: its folder, <c>request.json</c> and a <c>job.json</c> in state <c>queued</c>, generation 0. Request first, record last, so a record is never without its request.</summary>
    public RoleJobRecord Create(RoleJobRequest request)
    {
        var folder = FolderOf(request.JobId) ?? throw new ArgumentException($"'{request.JobId}' is not a job id", nameof(request));
        Directory.CreateDirectory(folder.Location);
        var now = clock.GetUtcNow().UtcDateTime;
        var record = new RoleJobRecord
        {
            JobId = request.JobId,
            Role = request.Role,
            Repo = request.RepoPath,
            WorkItem = request.WorkItem,
            TaskFile = request.TaskFile,
            TimeoutMinutes = request.TimeoutMinutes,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        folder.WriteRequest(request with { CreatedUtc = now });
        folder.WriteJob(record);
        Projected(record);

        return record;
    }

    public RoleJobRecord? Read(string jobId) => FolderOf(jobId)?.ReadJob();

    public RoleJobRequest? ReadRequest(string jobId) => FolderOf(jobId)?.ReadRequest();

    /// <summary>The ids of every job folder, oldest first (the id sorts by time).</summary>
    public IReadOnlyList<string> JobIds() =>
        Directory.Exists(JobsRoot)
            ? [.. Directory.EnumerateDirectories(JobsRoot).Select(Path.GetFileName).OfType<string>().Where(RoleJobIds.IsValid).Order(StringComparer.Ordinal)]
            : [];

    /// <summary>Changes the record WITHOUT the lease — for the fields only an observer knows (the launcher a start recorded). Never touches the generation, and refuses a job that is over.</summary>
    public RoleJobWrite Change(string jobId, Func<RoleJobRecord, RoleJobRecord> change)
    {
        if (FolderOf(jobId) is not { } folder)
        {
            return new RoleJobWrite.Gone();
        }

        RoleJobWrite outcome = new RoleJobWrite.Busy();
        var locked = folder.UnderLock(() => outcome = ChangeLocked(folder, change), clock);
        if (locked && outcome is RoleJobWrite.Written written)
        {
            Projected(written.Job);
        }

        return locked ? outcome : new RoleJobWrite.Busy();
    }

    private RoleJobWrite ChangeLocked(RoleJobRecord current, RoleJobFolder folder, Func<RoleJobRecord, RoleJobRecord> change)
    {
        if (current.Terminal)
        {
            return new RoleJobWrite.Over(current);
        }

        var next = change(current) with { Generation = current.Generation, UpdatedUtc = clock.GetUtcNow().UtcDateTime };
        folder.WriteJob(next);

        return new RoleJobWrite.Written(next);
    }

    private RoleJobWrite ChangeLocked(RoleJobFolder folder, Func<RoleJobRecord, RoleJobRecord> change) =>
        folder.ReadJob() is { } current ? ChangeLocked(current, folder, change) : new RoleJobWrite.Gone();

    /// <summary>
    /// Takes the lease of a job that is not over — or null: somebody holds it, the job is over or unreadable. Adds one to <c>generation</c> exactly once, under <c>job.lock</c>, and lets
    /// <paramref name="identify"/> record who took it (the runner's pid and start time) in the same write.
    /// </summary>
    public RoleLease? TryTake(string jobId, Func<RoleJobRecord, RoleJobRecord>? identify = null)
    {
        if (FolderOf(jobId) is not { } folder || !folder.Exists || HeldFile.TryHold(folder.LeaseFile) is not { } held)
        {
            return null;
        }

        RoleLease? taken = null;
        RoleJobRecord? written = null;
        var locked = folder.UnderLock(() => taken = TakeLocked(folder, held, jobId, identify, out written), clock);
        if (taken is null)
        {
            held.Dispose();
        }
        else if (locked && written is not null)
        {
            Projected(written);
        }

        return locked ? taken : null;
    }

    private RoleLease? TakeLocked(RoleJobFolder folder, FileStream held, string jobId, Func<RoleJobRecord, RoleJobRecord>? identify, out RoleJobRecord? written)
    {
        written = null;
        if (folder.ReadJob() is not { Terminal: false } current)
        {
            return null;
        }

        var generation = current.Generation + 1;
        var next = (identify?.Invoke(current) ?? current) with { Generation = generation, UpdatedUtc = clock.GetUtcNow().UtcDateTime };
        folder.WriteJob(next);
        written = next;

        return new RoleLease(held, folder, clock, jobId, generation, Projected);
    }

    /// <summary><see cref="TryTake"/> again and again for <see cref="TakeWindow"/> — the runner's road to its lease; a probe in flight must not cost a job its runner.</summary>
    public async Task<RoleLease?> TakeWithinAsync(string jobId, Func<RoleJobRecord, RoleJobRecord>? identify, CancellationToken ct)
    {
        var until = clock.GetTimestamp() + (long)(TakeWindow.TotalSeconds * clock.TimestampFrequency);
        while (true)
        {
            if (TryTake(jobId, identify) is { } lease)
            {
                return lease;
            }

            if (clock.GetTimestamp() >= until || Read(jobId) is not { Terminal: false })
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), clock, ct);
        }
    }

    /// <summary>
    /// Adds one to the generation WITHOUT the lease: how a cancel fences a runner it cannot take the lease from, so a stopped runner that is resumed finds its writes refused. Answers the new
    /// generation, or 0 when the job is over, missing or the lock could not be taken.
    /// </summary>
    public int BumpGeneration(string jobId)
    {
        var bumped = 0;
        if (FolderOf(jobId) is not { } folder)
        {
            return 0;
        }

        folder.UnderLock(() => bumped = BumpLocked(folder), clock);

        return bumped;
    }

    private int BumpLocked(RoleJobFolder folder)
    {
        if (folder.ReadJob() is not { Terminal: false } current)
        {
            return 0;
        }

        var next = current with { Generation = current.Generation + 1, UpdatedUtc = clock.GetUtcNow().UtcDateTime };
        folder.WriteJob(next);

        return next.Generation;
    }

    /// <summary>The job, the heartbeat that belongs to its generation, whether its lease is held, and what that makes of it (<see cref="RoleJobLiveness"/>). Null for no such job.</summary>
    public RoleJobObservation? Observe(string jobId)
    {
        if (FolderOf(jobId) is not { } folder || folder.ReadJob() is not { } job)
        {
            return null;
        }

        var beat = folder.ReadHeartbeat() is { } found && found.Generation == job.Generation ? found : null;
        var held = !job.Terminal && LeaseIsHeld(folder);

        return RoleJobLiveness.Judge(job, beat, held, clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>True when somebody holds the lease file open right now. The probe takes it for an instant and lets it go; a file that cannot be opened at all is not a holder.</summary>
    public static bool LeaseIsHeld(RoleJobFolder folder)
    {
        switch (HeldFile.Take(folder.LeaseFile))
        {
            case Hold.Taken taken:
                taken.Stream.Dispose();

                return false;
            case Hold.Busy:
                return true;
            default:
                return false;
        }
    }

    /// <summary>True once per stall: the marker file is made exclusively, so of two observers only one writes the <c>role-job-stalled</c> notice.</summary>
    public bool TryMarkStalled(string jobId)
    {
        if (FolderOf(jobId) is not { } folder)
        {
            return false;
        }

        try
        {
            using var marker = new FileStream(folder.StalledMarker, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The stall is over (the heartbeat is fresh again): the next one gets its own notice.</summary>
    public void ClearStalled(string jobId)
    {
        if (FolderOf(jobId) is { } folder)
        {
            RoleJobFolder.DeleteQuietly(folder.StalledMarker);
        }
    }

    /// <summary>Hands a written record to the projection — and never lets the projection change a job: the files are the authority, a database that will not open is a line in the log.</summary>
    internal void Projected(RoleJobRecord record)
    {
        try
        {
            project?.Invoke(record);
        }
        catch (Exception e)
        {
            // The one catch-all of the store: a projection is a view, and an unwritable database is never a failed job (the rounds-database rule, Store/Projection.cs).
            log.Warning(e, "role job {JobId} could not be projected into the rounds database", record.JobId);
        }
    }
}

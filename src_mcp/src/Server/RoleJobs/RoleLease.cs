namespace CoaiMcp.Server.RoleJobs;

/// <summary>What a write through a job's lease (or the store) came to — a closed union, so a caller cannot forget the refusals.</summary>
internal abstract record RoleJobWrite
{
    private RoleJobWrite()
    {
    }

    /// <summary>The record as it now stands on disk.</summary>
    public sealed record Written(RoleJobRecord Job) : RoleJobWrite;

    /// <summary>The generation on disk is not the lease's: somebody took the job over (a cancel, a sweep). The file was NOT changed and the holder must stop — kill its child, exit 75.</summary>
    public sealed record Stale(int Current) : RoleJobWrite;

    /// <summary>The job is already in a terminal state; a terminal state is final and the file was not changed.</summary>
    public sealed record Over(RoleJobRecord Job) : RoleJobWrite;

    /// <summary>There is no readable <c>job.json</c> (the folder was removed, or the file is torn). Nothing was written.</summary>
    public sealed record Gone : RoleJobWrite;

    /// <summary><c>job.lock</c> could not be taken in time. Nothing was written; the caller may try again.</summary>
    public sealed record Busy : RoleJobWrite;
}

/// <summary>
/// The right to run ONE job, held by one process: the <c>lease</c> file open with <see cref="FileShare.None"/> — the <c>SessionClaim</c> shape — which the kernel releases when the holder dies, and the
/// <see cref="Generation"/> the taker wrote when it took it (spec A, "Lease + generation").
/// </summary>
/// <remarks>
/// <para><b>Every write compares.</b> <see cref="Write"/>, <see cref="Publish"/> and <see cref="Beat"/> read <c>job.json</c> under <c>job.lock</c> and refuse when its generation is not this
/// lease's — so a holder that was stopped for minutes, cancelled by a person and then resumed finds its own writes refused instead of overwriting the cancel. <see cref="IsCurrent"/> is the same
/// comparison without a write, for the places that must ask first: before a result, before a terminal state, before a repository write.</para>
/// <para><b>A terminal state is final.</b> A write on a job that is already over is refused (<see cref="RoleJobWrite.Over"/>), whoever holds the lease.</para>
/// <para>Dropped, not released by a call: <see cref="Dispose"/> closes the file, and a process that is killed closes it for the kernel.</para>
/// </remarks>
internal sealed class RoleLease : IDisposable
{
    private readonly FileStream _held;
    private readonly RoleJobFolder _folder;
    private readonly TimeProvider _clock;
    private readonly Action<RoleJobRecord> _written;

    internal RoleLease(FileStream held, RoleJobFolder folder, TimeProvider clock, string jobId, int generation, Action<RoleJobRecord> written)
    {
        _held = held;
        _folder = folder;
        _clock = clock;
        _written = written;
        JobId = jobId;
        Generation = generation;
    }

    public string JobId { get; }

    /// <summary>The generation this lease wrote when it was taken; every write below compares it with the file's.</summary>
    public int Generation { get; }

    /// <summary>True while <c>job.json</c> still carries this lease's generation and the job is not over. Reads only.</summary>
    public bool IsCurrent() => _folder.ReadJob() is { } job && job.Generation == Generation && !job.Terminal;

    /// <summary>Changes the record — if this lease is still the current one and the job is not over. The generation is never changed by <paramref name="change"/>.</summary>
    public RoleJobWrite Write(Func<RoleJobRecord, RoleJobRecord> change) => Apply(null, change);

    /// <summary>Writes <c>result.json</c> and the record in ONE step under the lock, after the same comparison: a stale holder publishes nothing.</summary>
    public RoleJobWrite Publish(string resultJson, Func<RoleJobRecord, RoleJobRecord> change) => Apply(resultJson, change);

    /// <summary>The heartbeat — and false when this lease is no longer the current one, which tells the holder to stop.</summary>
    public bool Beat(string phase, int attempt, int childPid)
    {
        if (!IsCurrent())
        {
            return false;
        }

        _folder.WriteHeartbeat(new RoleHeartbeat(Generation, _clock.GetUtcNow().UtcDateTime, phase, attempt, childPid));

        return true;
    }

    private RoleJobWrite Apply(string? result, Func<RoleJobRecord, RoleJobRecord> change)
    {
        RoleJobWrite outcome = new RoleJobWrite.Busy();
        var locked = _folder.UnderLock(() => outcome = ApplyLocked(result, change), _clock);
        if (locked && outcome is RoleJobWrite.Written written)
        {
            // After the lock is let go: the projection may wait on a busy database, and nobody else should wait on job.lock for that.
            _written(written.Job);
        }

        return locked ? outcome : new RoleJobWrite.Busy();
    }

    private RoleJobWrite ApplyLocked(string? result, Func<RoleJobRecord, RoleJobRecord> change)
    {
        if (_folder.ReadJob() is not { } current)
        {
            return new RoleJobWrite.Gone();
        }

        if (current.Generation != Generation)
        {
            return new RoleJobWrite.Stale(current.Generation);
        }

        if (current.Terminal)
        {
            return new RoleJobWrite.Over(current);
        }

        var next = change(current) with { Generation = current.Generation, UpdatedUtc = _clock.GetUtcNow().UtcDateTime };
        if (result is not null)
        {
            _folder.WriteResult(result);
        }

        _folder.WriteJob(next);

        return new RoleJobWrite.Written(next);
    }

    public void Dispose() => _held.Dispose();
}

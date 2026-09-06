namespace CoaiServer;

/// <summary>Why a submission was refused, or <see cref="None"/> when it was not.</summary>
public enum SubmitRefusal
{
    None,
    TooManyQueued,
}

/// <summary>
/// Every job this run of the server knows about, and the only place their state changes.
/// </summary>
/// <remarks>
/// <para><b>In memory, deliberately.</b> A restart loses queued work, and the client is TOLD so — a
/// poll for an id from the previous epoch answers <c>lost</c>, which costs one resubmit of a prompt
/// the client still holds. The alternative was a durable store whose recovery scan must decide
/// whether a job that was Running when the process died is safe to restart, and it is not: the vendor
/// may already have been charged for it. One resubmit beats paying twice.</para>
/// <para><b>Claiming is atomic.</b> <see cref="TryClaim"/> moves a job from Queued to Running under
/// the same lock <see cref="Cancel"/> takes, so a cancel that returns 204 cannot be followed by a
/// dispatch that launches the vendor anyway. Without that the API could tell somebody their review
/// was cancelled and then spend the subscription on it — raised on the plan round.</para>
/// </remarks>
public sealed class JobStore(int perCallerQueued = 20, int perCallerRunning = 3, TimeSpan? keepFinished = null)
{
    /// <summary>How long a finished job can still be polled for.</summary>
    private readonly TimeSpan _keep = keepFinished ?? TimeSpan.FromHours(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, JobRecord> _jobs = [];

    /// <summary>Woken whenever any job changes, so a long poll returns on the transition.</summary>
    private readonly List<TaskCompletionSource> _waiters = [];

    /// <summary>
    /// The two caps, and what each one counts.
    /// </summary>
    /// <remarks>
    /// The plan round was right that one number called both things is not a rule. They are separate
    /// and they count different states: <b>Queued</b> is how many of a caller's jobs may be WAITING —
    /// exceeding it refuses the submission with 429, because a queue nobody drains is just a way to
    /// hold other people's turn. <b>Running</b> is how many may be on a vendor AT ONCE — exceeding it
    /// does not refuse anything, it simply leaves the job queued until one of the caller's own
    /// finishes. So a caller may hold 20 queued and 3 running; the 21st queued is refused, and jobs
    /// 4..20 are accepted and wait, which is the behaviour the earlier wording left undefined.
    /// </remarks>
    public int PerCallerQueued { get; } = perCallerQueued;

    /// <inheritdoc cref="PerCallerQueued"/>
    public int PerCallerRunning { get; } = perCallerRunning;

    /// <summary>Accept a job, or say why not.</summary>
    public (JobRecord? Job, SubmitRefusal Refusal, int Position) Submit(JobRecord job)
    {
        lock (_gate)
        {
            var queued = _jobs.Values.Count(j => j.Email == job.Email && j.Status == JobStatus.Queued);
            if (queued >= PerCallerQueued)
            {
                return (null, SubmitRefusal.TooManyQueued, 0);
            }

            _jobs[job.Id] = job;
            Wake();

            return (job, SubmitRefusal.None, PositionOf(job));
        }
    }

    /// <summary>The job, if this caller owns it.</summary>
    /// <remarks>
    /// Ownership is checked HERE rather than in the endpoint, so there is no route that can read a
    /// job without saying whose it is.
    /// </remarks>
    public JobRecord? Find(string id, string email)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(id, out var job) && job.Email == email ? job : null;
        }
    }

    /// <summary>True when the id exists but belongs to somebody else.</summary>
    public bool BelongsToSomeoneElse(string id, string email)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(id, out var job) && job.Email != email;
        }
    }

    /// <summary>Where this job sits in its vendor's queue, 1-based; 0 once it is running.</summary>
    public int PositionOf(JobRecord job)
    {
        lock (_gate)
        {
            return job.Status != JobStatus.Queued
                ? 0
                : 1 + _jobs.Values.Count(j =>
                    j.Status == JobStatus.Queued
                    && j.Vendor == job.Vendor
                    && j.SubmittedUtc < job.SubmittedUtc);
        }
    }

    /// <summary>
    /// Take the next job that may run on <paramref name="vendor"/>, moving it to Running atomically.
    /// </summary>
    /// <returns>The claimed job, or null when there is nothing to start.</returns>
    public JobRecord? TryClaim(string vendor, string slot, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            var candidate = _jobs.Values
                .Where(j => j.Status == JobStatus.Queued && j.Vendor == vendor)
                .Where(j => !JobTransitions.IsExpired(j, nowUtc))
                .Where(j => _jobs.Values.Count(r => r.Email == j.Email && r.Status == JobStatus.Running) < PerCallerRunning)
                .OrderBy(j => j.SubmittedUtc)
                .FirstOrDefault();

            if (candidate is null)
            {
                return null;
            }

            var started = JobTransitions.Start(candidate, slot, nowUtc);
            _jobs[started.Id] = started;
            Wake();

            return started;
        }
    }

    /// <summary>Record a terminal state. Ignored when the job is already finished.</summary>
    public void Finish(JobRecord job)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(job.Id, out var current) && current.IsTerminal)
            {
                return;
            }

            _jobs[job.Id] = job;
            Wake();
        }
    }

    /// <summary>
    /// Withdraw a job. Returns the record if it was cancelled, null if there was nothing to cancel.
    /// </summary>
    /// <remarks>
    /// A QUEUED job is moved to Failed(cancelled) here and under the same lock <see cref="TryClaim"/>
    /// uses, so it can never be dispatched afterwards. A RUNNING job is marked and its token is
    /// signalled by the runner; the process dies and the runner writes the terminal state.
    /// </remarks>
    public JobRecord? Cancel(string id, string email, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job) || job.Email != email || job.IsTerminal)
            {
                return null;
            }

            var cancelled = JobTransitions.Fail(job, FailureKind.Cancelled, "cancelled by the caller", nowUtc);
            _jobs[id] = cancelled;
            Wake();

            return cancelled;
        }
    }

    /// <summary>
    /// Expire what has run out of time, and forget what finished long enough ago.
    /// </summary>
    /// <returns>The jobs that were expired, so the caller can stop their processes.</returns>
    /// <remarks>
    /// Run on a timer rather than only when something happens: an event-driven deadline is only
    /// checked when there is an event, so a running job whose deadline passes on an otherwise idle
    /// server would keep going and keep spending. (Plan round.)
    /// </remarks>
    public IReadOnlyList<JobRecord> Sweep(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            var expired = _jobs.Values.Where(j => JobTransitions.IsExpired(j, nowUtc)).ToList();
            foreach (var job in expired)
            {
                _jobs[job.Id] = JobTransitions.Fail(
                    job, FailureKind.Cancelled, JobTransitions.ExpiryReason(job), nowUtc);
            }

            foreach (var old in _jobs.Values
                .Where(j => j.IsTerminal && j.FinishedUtc is { } at && nowUtc - at > _keep)
                .ToList())
            {
                _jobs.Remove(old.Id);
            }

            if (expired.Count > 0)
            {
                Wake();
            }

            return expired;
        }
    }

    /// <summary>Wait until any job changes, or <paramref name="wait"/> passes.</summary>
    /// <remarks>
    /// A <see cref="TaskCompletionSource"/>, not a sleep loop: an awaiting request occupies no thread
    /// on Kestrel, which is what makes a 25-second poll cheap enough to be the normal way a client
    /// watches a review.
    /// </remarks>
    public async Task WaitForChangeAsync(TimeSpan wait, CancellationToken ct)
    {
        TaskCompletionSource waiter;
        lock (_gate)
        {
            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add(waiter);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        using (timeout.Token.Register(() => waiter.TrySetResult()))
        {
            await waiter.Task;
        }

        lock (_gate)
        {
            _waiters.Remove(waiter);
        }
    }

    /// <summary>Every job, for the sweeper and the tests.</summary>
    public IReadOnlyList<JobRecord> All()
    {
        lock (_gate)
        {
            return [.. _jobs.Values];
        }
    }

    private void Wake()
    {
        foreach (var waiter in _waiters)
        {
            waiter.TrySetResult();
        }

        _waiters.Clear();
    }
}

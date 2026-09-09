namespace CoaiServer;

/// <summary>Why a submission was refused, or <see cref="None"/> when it was not.</summary>
public enum SubmitRefusal
{
    None,
    TooManyQueued,

    /// <summary>
    /// The idempotency key has been used, for a DIFFERENT question.
    /// </summary>
    /// <remarks>
    /// Not a retry: a retry is the same key and the same request, and that is answered with the first
    /// job. This is a client bug, and returning the earlier job for it would hand somebody the answer
    /// to a question they did not ask while looking exactly like success.
    /// </remarks>
    KeyUsedForSomethingElse,
}

/// <summary>
/// Every job this run of the server knows about, and the only place their state changes.
/// </summary>
/// <remarks>
/// <para><b>In memory, deliberately.</b> A restart loses queued work, and the client is TOLD so — a
/// poll for an id from the previous epoch answers <c>lost</c>, which costs one resubmit of a prompt
/// the client still holds. The alternative was a durable store whose recovery scan must decide
/// whether a job that was Running when the process died is safe to restart, and it is not: the vendor
/// may already have been charged for it.</para>
/// <para><b>Claiming, cancelling and expiring are atomic with each other.</b> They take the same lock,
/// so a cancel that returned 204 cannot be followed by a dispatch — the API must never tell somebody
/// their review was cancelled and then spend the subscription on it.</para>
/// <para><b>A cancelled or expired RUNNING job has its process stopped.</b> Marking the record
/// terminal is not enough: the CLI goes on running and goes on spending against the account, and the
/// slot is held the whole time. Each running job registers a cancellation source here, and the same
/// call that ends the record fires it. Raised as Blocking on this change's code round.</para>
/// <para><b>An ordinary <c>lock</c>, not a <c>SemaphoreSlim</c>.</b> Everything under it is a
/// dictionary operation measured in microseconds and no I/O ever happens inside it, so an async lock
/// would add a state machine and a heap allocation per call to avoid a contention that cannot occur.
/// </para>
/// </remarks>
public sealed class JobStore(int perCallerQueued = 20, int perCallerRunning = 3, TimeSpan? keepFinished = null)
{
    private readonly TimeSpan _keep = keepFinished ?? TimeSpan.FromHours(1);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, JobRecord> _jobs = [];

    /// <summary>
    /// Who is waiting to hear about WHICH job.
    /// </summary>
    /// <remarks>
    /// Per job, not one global list. A single shared waiter is woken by every submit, claim and
    /// finish anywhere on the server, so a client polling its own queued review would return within
    /// milliseconds because somebody else's job moved — turning a long poll into a busy poll, and a
    /// busy poll into the rate limiter. Three reviewers found this independently.
    /// </remarks>
    private readonly Dictionary<string, List<TaskCompletionSource>> _waiters = new(StringComparer.Ordinal);

    /// <summary>The token that stops the vendor process of a job that is running right now.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);

    /// <summary>How many of one person's reviews may WAIT. The next is refused.</summary>
    /// <remarks>
    /// Separate from <see cref="PerCallerRunning"/> and counting a different state, which is what the
    /// plan round found undefined. A queue nobody drains is just a way to hold other people's turn,
    /// so exceeding this is a 429 rather than a silent accept.
    /// </remarks>
    public int PerCallerQueued { get; } = perCallerQueued;

    /// <summary>How many of one person's reviews may be on a vendor AT ONCE.</summary>
    /// <remarks>
    /// Exceeding this refuses nothing — it leaves the job queued until one of that caller's own
    /// finishes. So a caller may hold 20 queued and 3 running, and jobs 4..20 are accepted and wait.
    /// </remarks>
    public int PerCallerRunning { get; } = perCallerRunning;

    /// <summary>
    /// Accept a job, or say why not — or hand back the one this key already made.
    /// </summary>
    /// <remarks>
    /// <para>The idempotency lookup is INSIDE this lock, with the insert. Two simultaneous posts of
    /// one key would otherwise both find nothing and both create a job, which is the exact duplicate
    /// the key exists to prevent — and the window is widest under precisely the conditions that
    /// produce a retry. (codex, plan round.)</para>
    /// <para>The mapping lives on the job RECORD rather than in a dictionary beside it, so there is
    /// no second lifetime to keep in step: <see cref="Sweep"/> forgets a finished job an hour later
    /// and the key goes with it. A separate index would have needed its own pruning, and an index
    /// that outlives what it points at is how a repeat comes to return a 404. (gemini, plan round.)</para>
    /// </remarks>
    public (JobRecord? Job, SubmitRefusal Refusal, int Position) Submit(JobRecord job)
    {
        lock (_gate)
        {
            if (job.IdempotencyKey.Length > 0 && Keyed(job.Email, job.IdempotencyKey) is { } already)
            {
                return already.Fingerprint == job.Fingerprint
                    ? (already, SubmitRefusal.None, Position(already))
                    : (null, SubmitRefusal.KeyUsedForSomethingElse, 0);
            }

            var queued = _jobs.Values.Count(j => j.Email == job.Email && j.Status == JobStatus.Queued);
            if (queued >= PerCallerQueued)
            {
                return (null, SubmitRefusal.TooManyQueued, 0);
            }

            _jobs[job.Id] = job;
            Wake(job.Id);

            return (job, SubmitRefusal.None, Position(job));
        }
    }

    /// <summary>This caller's job under this key, whatever state it is in. Call under the lock.</summary>
    /// <remarks>
    /// A scan, not an index. At this deployment's ceiling — twenty queued per person and an hour of
    /// finished work — it is a walk of a few hundred records under a lock that already does exactly
    /// that to count somebody's queue, and it cannot fall out of step with the jobs it describes.
    /// </remarks>
    private JobRecord? Keyed(string email, string key) =>
        _jobs.Values.FirstOrDefault(j =>
            j.IdempotencyKey.Length > 0
            && string.Equals(j.IdempotencyKey, key, StringComparison.Ordinal)
            && string.Equals(j.Email, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Somebody asked about this job: it is not abandoned.
    /// </summary>
    /// <remarks>
    /// <b>The order here is the whole point.</b> A job that is ALREADY past its abandonment window is
    /// expired on the spot rather than revived by the poll that found it — otherwise a client that
    /// went away for five minutes and came back would resurrect a job the server had every right to
    /// have dropped, and whether it survived would depend on when the sweep timer last happened to
    /// fire. Deciding it here makes the boundary the same for every caller. (codex and local, plan
    /// round, from two directions.)
    /// </remarks>
    /// <returns>The job as it stands after the poll, or null when this caller has none.</returns>
    public JobRecord? Polled(string id, string email, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job) || job.Email != email)
            {
                return null;
            }
            if (job.IsTerminal)
            {
                return job;
            }
            if (JobTransitions.IsAbandoned(job, nowUtc))
            {
                return Expire(job, nowUtc);
            }

            var heard = job with { LastPolledUtc = nowUtc };
            _jobs[id] = heard;

            return heard;
        }
    }

    /// <summary>The job, if this caller owns it.</summary>
    /// <remarks>Ownership is checked HERE, so no route can read a job without saying whose it is.</remarks>
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
            return Position(job);
        }
    }

    /// <summary>
    /// Take the next job that may run on <paramref name="vendor"/>, moving it to Running atomically.
    /// </summary>
    /// <param name="ct">The host's token. The job's own token is linked to it and returned.</param>
    /// <returns>The claimed job and the token its vendor process must observe, or null.</returns>
    public (JobRecord Job, CancellationToken Token)? TryClaim(
        string vendor, string slot, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            // Counted ONCE, before filtering, rather than re-scanned for every candidate — the nested
            // count made a dispatch tick quadratic in the number of jobs, under this lock. (Two
            // reviewers, code round.)
            var running = _jobs.Values
                .Where(j => j.Status == JobStatus.Running)
                .GroupBy(j => j.Email, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var candidate = _jobs.Values
                .Where(j => j.Status == JobStatus.Queued && j.Vendor == vendor)
                .Where(j => !JobTransitions.IsExpired(j, nowUtc))
                .Where(j => running.GetValueOrDefault(j.Email) < PerCallerRunning)
                .OrderBy(j => j.SubmittedUtc)
                // Two submissions can share a timestamp; without a tie-break the order between them
                // is whatever the dictionary happens to yield, which is not an order at all.
                .ThenBy(j => j.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (candidate is null)
            {
                return null;
            }

            var started = JobTransitions.Start(candidate, slot, nowUtc);
            _jobs[started.Id] = started;
            var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _running[started.Id] = source;
            Wake(started.Id);

            return (started, source.Token);
        }
    }

    /// <summary>Record a terminal state. Ignored when the job is already finished.</summary>
    public void Finish(JobRecord job)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(job.Id, out var current) && current.IsTerminal)
            {
                Release(job.Id);

                return;
            }

            _jobs[job.Id] = job;
            Release(job.Id);
            Wake(job.Id);
        }
    }

    /// <summary>
    /// Withdraw a job, and stop it if it is running.
    /// </summary>
    /// <returns>The cancelled record, or null if there was nothing to cancel.</returns>
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
            Stop(id);
            Wake(id);

            return cancelled;
        }
    }

    /// <summary>
    /// Expire what has run out of time, and forget what finished long enough ago.
    /// </summary>
    /// <returns>The expired jobs AS ENDED — each already carrying the reason it ended for.</returns>
    /// <remarks>
    /// On a timer rather than on an event: an event-driven deadline is only checked when there is an
    /// event, so a running job whose deadline passes on an otherwise idle server would keep going and
    /// keep spending. Expiring a RUNNING job also stops its process — the record going terminal while
    /// the CLI carries on is the failure this exists to prevent.
    /// </remarks>
    public IReadOnlyList<JobRecord> Sweep(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            // The ENDED records are returned, not the ones as they were: each carries the sentence
            // that says why it ended, so the caller logs the reason this store decided rather than
            // computing a second one that can disagree with it.
            var expired = _jobs.Values
                .Where(j => JobTransitions.IsExpired(j, nowUtc))
                .ToList()
                .Select(job => Expire(job, nowUtc))
                .ToList();

            foreach (var old in _jobs.Values
                .Where(j => j.IsTerminal && j.FinishedUtc is { } at && nowUtc - at > _keep)
                .ToList())
            {
                _jobs.Remove(old.Id);
                _waiters.Remove(old.Id);
            }

            return expired;
        }
    }

    /// <summary>
    /// Wait until THIS job changes, or <paramref name="wait"/> passes.
    /// </summary>
    /// <remarks>
    /// A <see cref="TaskCompletionSource"/>, so an open poll occupies no thread. The registration and
    /// the removal are both under the lock, and the removal is in a <c>finally</c> — a cancelled
    /// request that skipped it left a waiter in the list until the next wake. (gemini, code round.)
    /// </remarks>
    public async Task WaitForChangeAsync(string id, TimeSpan wait, CancellationToken ct)
    {
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (!_waiters.TryGetValue(id, out var list))
            {
                _waiters[id] = list = [];
            }

            list.Add(waiter);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        try
        {
            using (timeout.Token.Register(() => waiter.TrySetResult()))
            {
                await waiter.Task;
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_waiters.TryGetValue(id, out var list))
                {
                    list.Remove(waiter);
                    if (list.Count == 0)
                    {
                        _waiters.Remove(id);
                    }
                }
            }
        }
    }

    /// <summary>Put a running job back in the queue, so another account can take it.</summary>
    /// <remarks>
    /// Used when the account said "rate limited" and a different one is free. The job keeps its
    /// original queue deadline — a rotation must not extend how long an abandoned review can live —
    /// and loses its run clock, because the run has not started yet.
    /// </remarks>
    public void Requeue(JobRecord job, string refusedBy, string note)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(job.Id, out var current) || current.IsTerminal)
            {
                return;
            }

            _jobs[job.Id] = current with
            {
                Status = JobStatus.Queued,
                Slot = string.Empty,
                StartedUtc = null,
                RunDeadlineUtc = null,
                Reason = note,
                Refused = new HashSet<string>(current.RefusedBy, StringComparer.Ordinal) { refusedBy },
            };
            // The RUN is over even though the job is not: the next account gets a fresh token.
            Release(job.Id);
            Wake(job.Id);
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

    /// <summary>Called with the lock held.</summary>
    private int Position(JobRecord job) =>
        job.Status != JobStatus.Queued
            ? 0
            : 1 + _jobs.Values.Count(j =>
                j.Status == JobStatus.Queued
                && j.Vendor == job.Vendor
                && (j.SubmittedUtc < job.SubmittedUtc
                    || (j.SubmittedUtc == job.SubmittedUtc && string.CompareOrdinal(j.Id, job.Id) < 0)));

    /// <summary>
    /// End a job that has run out of whichever clock applies. Called with the lock held.
    /// </summary>
    /// <remarks>
    /// <b>Marking the record is not the half that matters.</b> A RUNNING job has a vendor CLI behind
    /// it that goes on running, goes on spending against the shared account and goes on holding the
    /// slot; <see cref="Stop"/> fires the token the runner is awaiting on, which is what actually
    /// frees it. Both the sweep and a poll that finds an abandoned job come through here, so there is
    /// one path from "expired" to "stopped" rather than two that can disagree. (codex, plan round.)
    /// </remarks>
    private JobRecord Expire(JobRecord job, DateTimeOffset nowUtc)
    {
        var ended = JobTransitions.Fail(
            job, FailureKind.Cancelled, JobTransitions.ExpiryReason(job, nowUtc), nowUtc);
        _jobs[job.Id] = ended;
        Stop(job.Id);
        Wake(job.Id);

        return ended;
    }

    /// <summary>Called with the lock held: fire the token, and LEAVE the source alone.</summary>
    /// <remarks>
    /// Cancelling and disposing in one breath was a real bug and both vendors caught it: the runner
    /// is at that moment awaiting on this source's token, and a token whose source has been disposed
    /// throws <see cref="ObjectDisposedException"/> the next time anything registers on it — which
    /// turns an ordinary cancellation into an infrastructure exception the runner reports as
    /// "the server could not run this review". The source stays until the runner reaches a terminal
    /// state and <see cref="Release"/> disposes it.
    /// </remarks>
    private void Stop(string id)
    {
        if (_running.TryGetValue(id, out var source))
        {
            source.Cancel();
        }
    }

    /// <summary>Called with the lock held: the runner is done with this job's token.</summary>
    private void Release(string id)
    {
        if (_running.Remove(id, out var source))
        {
            source.Dispose();
        }
    }
    /// <summary>Called with the lock held.</summary>
    private void Wake(string id)
    {
        if (!_waiters.TryGetValue(id, out var list))
        {
            return;
        }

        foreach (var waiter in list)
        {
            waiter.TrySetResult();
        }
    }
}

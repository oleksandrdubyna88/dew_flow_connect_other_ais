namespace CoaiMcp.Runners.Processes;

/// <summary>
/// What a launch has learned about the descendants of a child it ends itself: every one it ever saw, recorded as it
/// appeared, and the confirmation that they are gone (the CodeRabbit finding on PR #790 against story 6.1a of
/// <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Why a watch rather than one listing before the kill.</b> A root that EXITS BY ITSELF can no longer name its
/// children — they are reparented the moment it goes — so a listing taken after the wait ends finds nothing, and the
/// only listing the launcher had (just before it kills an answered or timed-out root) never ran. The leg here lists the
/// root's tree every <see cref="ProcessTree.ListingStep"/> while it runs, so whatever it started and left behind has been
/// seen, recorded and is ready to be ended whichever way the root leaves.</para>
/// <para><b>Held, not thrown.</b> A tracker that cannot write a descendant does not stop the leg, or the end that follows:
/// the failure is kept in <see cref="Unrecorded"/>, recording stops, and the caller raises it once the tree is ended and
/// the drain has settled — the same contract <see cref="ProcessLauncher"/> always had for a record that fails.</para>
/// <para><b>The limit.</b> A descendant born inside the last listing step before the root's own exit, which also holds none
/// of the stdio the root inherited, is neither seen nor detected — nothing connects it to the launch any more.</para>
/// </remarks>
internal sealed class TreeWatch(IProcessTracker tracker, string label, bool tracked)
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<ProcessIdentity> _seen = [];
    private Task _leg = Task.CompletedTask;
    private bool _stopped;

    /// <summary>The first failure of a record (or of a listing), held until the caller can raise it; null while all is well.</summary>
    public Exception? Unrecorded { get; private set; }

    /// <summary>Starts listing <paramref name="rootPid"/>'s descendants every <paramref name="step"/>, the first time after one step.</summary>
    public void Start(int rootPid, TimeSpan step) => _leg = ListAsync(rootPid, step, _stop.Token);

    /// <summary>Ends the leg and waits until it has finished, so nothing lists or records after this returns.</summary>
    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        await _stop.CancelAsync();
        await _leg;
        _stop.Dispose();
    }

    /// <summary>One listing of the root's tree, observed — a listing that FAILS is held like a record failure, never thrown: the root must always be ended.</summary>
    public void ListOnce(int rootPid)
    {
        try
        {
            Observe(ProcessTree.Descendants(rootPid));
        }
        catch (Exception e)
        {
            Unrecorded ??= e;
        }
    }

    /// <summary>Adds the members not seen before, and records them while the tracker has taken every one so far.</summary>
    public void Observe(IReadOnlyList<ProcessIdentity> listed)
    {
        var fresh = listed.Where(member => !_seen.Any(known => Same(known, member))).ToList();
        _seen.AddRange(fresh);
        if (tracked)
        {
            Unrecorded ??= RecordEach(fresh);
        }
    }

    /// <summary>Kills what was seen, waits the bound for it to be gone, and forgets the ones confirmed — the survivors are returned.</summary>
    public async Task<IReadOnlyList<ProcessIdentity>> ConfirmAsync()
    {
        if (_seen.Count == 0)
        {
            return [];
        }

        var survivors = await ProcessTree.ConfirmGoneAsync(_seen);
        ForgetConfirmed(survivors);

        return survivors;
    }

    /// <summary>Tells the tracker the members confirmed gone — only when tracked, and only while every record was taken.</summary>
    private void ForgetConfirmed(IReadOnlyList<ProcessIdentity> survivors)
    {
        if (!tracked || Unrecorded is not null)
        {
            return;
        }

        foreach (var gone in _seen.Where(member => !survivors.Contains(member)))
        {
            tracker.Forget(gone.Pid);
        }
    }

    private async Task ListAsync(int rootPid, TimeSpan step, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(step);
            while (await timer.WaitForNextTickAsync(ct))
            {
                ListOnce(rootPid);
            }
        }
        catch (OperationCanceledException)
        {
            // The wait ended; this is the leg's own stop.
        }
        catch (Exception e)
        {
            // A listing that fails is a tree nobody can fully name: held like a record failure, never thrown mid-run.
            Unrecorded ??= e;
        }
    }

    private static bool Same(ProcessIdentity a, ProcessIdentity b) => a.Pid == b.Pid && OrphanSweep.Same(a.StartedUtc, b.StartedUtc);

    private Exception? RecordEach(IReadOnlyList<ProcessIdentity> members)
    {
        try
        {
            foreach (var member in members)
            {
                tracker.Record(member.Pid, member.StartedUtc, label);
            }

            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}

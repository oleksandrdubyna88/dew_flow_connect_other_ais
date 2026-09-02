namespace CoaiMcp.Runners.Processes;

/// <summary>
/// A child this product started, written down so it can be found again after a crash.
/// </summary>
/// <param name="StartedUtc">
/// The child's own start time. Recorded because a PID alone is not an identity: the number is
/// reused, and a sweep that trusts it would eventually kill a stranger's process.
/// </param>
/// <param name="OwnerPid">The <c>coai-mcp</c> that launched it, and whose death orphans it.</param>
public sealed record TrackedProcess(
    int Pid,
    DateTime StartedUtc,
    int OwnerPid,
    DateTime OwnerStartedUtc,
    string Label);

/// <summary>What a sweep decided: what to kill, and what to stop remembering.</summary>
public sealed record SweepPlan(IReadOnlyList<TrackedProcess> Reap, IReadOnlyList<TrackedProcess> Forget);

/// <summary>
/// Which recorded children are orphans, decided without touching a process.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A reviewer that overruns its deadline is killed with its whole
/// tree by <see cref="ProcessLauncher"/> — but the kill is performed by the PARENT, so it only
/// happens while the parent is alive. When <c>coai-mcp</c> itself goes away (an MCP client
/// restarting is the ordinary case, not the rare one), every reviewer it had in flight is
/// orphaned with nothing left to stop it. Reported from a macOS checkout: an Antigravity child
/// started at 00:03 was still running at 10:00, hours after its round, its vendor removed from
/// the configuration, and its server long gone.</para>
/// <para>Worktrees already had this: <c>open</c> prunes whatever a killed session left behind.
/// Processes did not, and a leaked reviewer costs more than a leaked directory — it holds a
/// vendor's rate limit, a GPU, or a paid token budget.</para>
///
/// <para><b>The danger is killing the wrong thing</b>, and it is what shapes every rule here. The
/// vendor CLIs are programs a person also runs by hand; "kill every codex" would be a product that
/// terminates its user's terminal session. So a process is killed only when THIS product recorded
/// starting it, the recorded start time still matches (so the PID has not been reused), and the
/// server that started it is provably gone. Anything short of all three leaves it alone.</para>
/// </remarks>
public static class OrphanSweep
{
    /// <summary>
    /// A second of slack when comparing start times.
    /// </summary>
    /// <remarks>
    /// The record round-trips through JSON and the platform reports the time at its own
    /// resolution, so exact equality would occasionally say "not the same process" about a process
    /// that is. A second is far tighter than any real PID reuse: for it to matter, the operating
    /// system would have to hand the same number to a new process within a second of the old one's
    /// start time, which is not a thing that happens.
    /// </remarks>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    /// <param name="startedAt">
    /// When a live process with that PID started, or null when there is none. Injected so the
    /// decision is a unit test rather than an experiment with real processes.
    /// </param>
    /// <param name="currentPid">This server. Its own children are in flight, never orphans.</param>
    public static SweepPlan Plan(
        IReadOnlyList<TrackedProcess> tracked,
        Func<int, DateTime?> startedAt,
        int currentPid)
    {
        var reap = new List<TrackedProcess>();
        var forget = new List<TrackedProcess>();

        foreach (var record in tracked)
        {
            // Ours. It may be running a review at this very moment, and the reason this sweep runs
            // on `open` is that `open` happens while rounds are in flight.
            if (record.OwnerPid == currentPid)
            {
                continue;
            }

            var child = startedAt(record.Pid);
            if (child is null || !Same(child.Value, record.StartedUtc))
            {
                // Gone, or the number belongs to somebody else now. Either way there is nothing of
                // ours to kill and nothing left to remember.
                forget.Add(record);
                continue;
            }

            if (startedAt(record.OwnerPid) is { } owner && Same(owner, record.OwnerStartedUtc))
            {
                // A DIFFERENT server is alive and owns this child. Two servers against one data
                // directory is ordinary — an editor and a CLI — and killing each other's reviewers
                // would be the worst possible reading of "clean up".
                continue;
            }

            reap.Add(record);
        }

        return new SweepPlan(reap, forget);
    }

    private static bool Same(DateTime a, DateTime b) => (a - b).Duration() <= Slack;
}

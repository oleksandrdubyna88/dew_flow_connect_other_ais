using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// Writes the CLI child of a role job into <c>job.json</c> — its pid and its start time — under the runner's lease, from the moment it exists to the moment it is forgotten. It is what the
/// work hands to <see cref="ProcessLauncher"/> (<see cref="IRoleRun.Children"/>), so a cancel or a sweep that finds the runner stopped or gone can stop the child by identity.
/// </summary>
/// <remarks>
/// A write the lease refuses (a stale generation: somebody took the job over) calls <c>onStale</c> — the runner then stops, and the child it just started with it. The child's pid also goes into
/// the next heartbeat (<see cref="ChildPid"/>).
/// </remarks>
internal sealed class RoleChildTracker(RoleLease lease, Action onStale) : IProcessTracker
{
    private int _childPid;

    public int ChildPid => Volatile.Read(ref _childPid);

    public void Record(int pid, DateTime startedUtc, string label)
    {
        Volatile.Write(ref _childPid, pid);
        Offer(lease.Write(job => job with { Child = new RoleChild(pid, startedUtc) }));
    }

    public void Forget(int pid)
    {
        Volatile.Write(ref _childPid, 0);
        Offer(lease.Write(job => job.Child?.Pid == pid ? job with { Child = null } : job));
    }

    private void Offer(RoleJobWrite written)
    {
        if (written is RoleJobWrite.Stale)
        {
            onStale();
        }
    }
}

using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>How a piece of work ended, as the runner needs to know it. A closed union, so the runner cannot forget a case.</summary>
internal abstract record RoleWorkOutcome
{
    private RoleWorkOutcome()
    {
    }

    /// <summary>The role finished and has an answer — a JSON document, written to <c>result.json</c> after the generation is compared.</summary>
    public sealed record Succeeded(string ResultJson) : RoleWorkOutcome;

    /// <summary>The role could not finish. <see cref="Stop"/> is a short word and <see cref="Reason"/> the sentence a person reads.</summary>
    public sealed record Failed(string Stop, string Reason) : RoleWorkOutcome;

    /// <summary>The work saw its token fire and stopped. The runner knows WHY it fired (a cancel, a timeout, a signal, a lost lease) and writes the matching state.</summary>
    public sealed record Stopped : RoleWorkOutcome;
}

/// <summary>What a piece of work is given by the runner: the request, the job folder, and the road to record the CLI child it starts.</summary>
internal interface IRoleRun
{
    RoleJobRequest Request { get; }

    /// <summary>The job folder: where <c>attempts/&lt;n&gt;/</c> evidence goes.</summary>
    string Folder { get; }

    /// <summary>
    /// Hand this to <see cref="ProcessLauncher"/> and ask for tracking: the child's pid AND start time are written into <c>job.json</c> under the lease, so a cancel or a sweep that finds the
    /// runner gone can stop the child it owned — by identity, never by name.
    /// </summary>
    IProcessTracker Children { get; }

    /// <summary>What the heartbeat reports: the phase and the attempt number the work is in.</summary>
    void Phase(string phase, int attempt);
}

/// <summary>
/// The pluggable part of a role job (epoch 3 E1 s1): what the runner runs between taking the lease and writing the result. Story s1 ships the job, the lease and the runner; the router, the
/// failover loop and the role CLI launch belong to stories s3 and s4, which register their work here.
/// </summary>
/// <remarks>
/// The work OWNS its CLI child — the server does not track it — and must end it when <paramref name="ct"/> fires. It never writes <c>job.json</c> itself: the runner does, under the lease.
/// </remarks>
internal interface IRoleWork
{
    Task<RoleWorkOutcome> RunAsync(IRoleRun run, CancellationToken ct);
}

/// <summary>What a role IS to the job machinery: its name, a one-line summary for the tool's refusal, and how to make its work. A role with no descriptor cannot be started.</summary>
internal sealed record RoleDescriptor(string Role, string Summary, Func<IRoleWork> CreateWork);

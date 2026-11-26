using CoaiMcp.Core.Notices;
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

    /// <summary>
    /// The role could not finish. <see cref="Stop"/> is a short word and <see cref="Reason"/> the sentence a person reads. <see cref="ResultJson"/>, when the role has one, is written to <c>result.json</c> in the same step as the terminal
    /// state — the evidence of a failed job that did leave something behind (the coder's branch, its checks and the last failure digest); null for the failures that leave nothing.
    /// </summary>
    public sealed record Failed(string Stop, string Reason, string? ResultJson = null) : RoleWorkOutcome;

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

    /// <summary>The job as the runner took it: its route, its complexity and its security flag, as the router left them at the start (spec A).</summary>
    RoleJobRecord Job { get; }

    /// <summary>The data directory the job lives in — where the usage ledger and the notices are written.</summary>
    string DataDir { get; }

    /// <summary>The settings the work reads (the environment over the data directory's settings file): where a vendor's CLI is.</summary>
    Func<string, string?> Settings { get; }

    /// <summary>The runner's log: the one Information line of each model switch goes here.</summary>
    Serilog.ILogger Log { get; }

    /// <summary>The clock of the job — a wait of the retry ladder runs on it, so a test moves it.</summary>
    TimeProvider Clock { get; }

    /// <summary>Offers a notice; false when it could not be written (never a reason to stop the job).</summary>
    bool Notice(ServerNotice notice);

    /// <summary>Writes the model the work is trying now into <c>job.json</c>, under the lease. False when the lease is no longer this runner's: the work must stop.</summary>
    bool Trying(string model);

    /// <summary>Appends one finished attempt to <c>job.json</c> (and, through the store's projection, to <c>role_attempts</c>), under the lease. False when the lease is no longer this runner's.</summary>
    bool Attempted(RoleAttempt attempt);

    /// <summary>Writes how many repair turns the job has used (<c>job.json</c>'s <c>repairTurns</c>, <c>role_jobs.repair_turns</c>), under the lease. False when the lease is no longer this runner's.</summary>
    bool Repaired(int turns);

    /// <summary>
    /// True while this runner is still the job's writer (the generation on disk is the lease's and the job is not over). A role asks it before it writes into the REPOSITORY — a stale runner (a cancel fenced it) must publish
    /// nothing there (spec A, "Lease + generation").
    /// </summary>
    bool StillWriter();
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

/// <summary>
/// What a role IS to the job machinery: its name, a one-line summary for the tool's refusal, how to make its work, and the entry of the roles table its models come from. A role with no descriptor cannot be started.
/// </summary>
/// <param name="Check">
/// A role's own look at a request, made at the start BEFORE the router and before any folder is made: the empty string when the request is fit for the role, else the sentence that refuses it (the BA refuses an epic that already has
/// a story). Null for a role with nothing to check.
/// </param>
/// <param name="NeedsBrief">
/// True for a role that answers a QUESTION (the scout, the judge): <c>brief</c> is required and <c>taskFile</c> is optional, the file the question is about. False, the default, keeps the rule of the BA and the coder: exactly one of
/// <c>taskFile</c> and <c>brief</c>.
/// </param>
/// <param name="RouteAs">
/// The role-table name the router reads for this role's models (<c>ba</c>, <c>coder</c>…), or empty for a role that asks no model — the <c>probe</c>, whose route is empty. A start for a role whose list is empty is refused
/// naming <c>COAI_ROLES_MODELS</c> before any folder is made (epoch 3 E1 s3).
/// </param>
internal sealed record RoleDescriptor(string Role, string Summary, Func<IRoleWork> CreateWork, string RouteAs = "", Func<RoleJobRequest, string>? Check = null, bool NeedsBrief = false);

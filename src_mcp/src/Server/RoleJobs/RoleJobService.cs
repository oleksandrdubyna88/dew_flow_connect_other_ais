using System.Collections;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the three role tools do (spec A): <see cref="StartAsync"/> makes a job and launches its detached runner; <see cref="StatusAsync"/> reads it (and, on the way, closes a dead runner's job as
/// <c>lost</c> and notices a stall); <see cref="CancelAsync"/> stops it. Every answer is the JSON string the tool returns; every refusal is <c>{"error": ...}</c>.
/// </summary>
/// <remarks>
/// <para><b>Starting is fast and does not wait.</b> The job is made, its environment written, the runner launched (about 15 ms by <c>systemd-run</c>) and the answer returned — it never waits for the
/// runner to start, so <c>start_role</c> answers in well under the two seconds the spec allows, and the server does not track the runner as a child: it owns its CLI child and the lease.</para>
/// <para><b>Never queued.</b> At the cap (<c>COAI_ROLE_JOBS_MAX</c>, 3, and 2 on WSL) a start is REFUSED, naming the live jobs. The count is taken under a lock file together with the creation of
/// the job, so two starts at the cap cannot both pass; and a job whose runner is dead is closed as lost first, so a crashed runner never holds a slot.</para>
/// <para><b>A role without a descriptor is refused</b> before any folder is made (<see cref="RoleDescriptors"/>).</para>
/// </remarks>
internal sealed class RoleJobService(string dataDir, Func<string, string?> settings, Serilog.ILogger log, Func<ServerNotice, bool> notice)
{
    /// <summary>The longest <c>role_status</c> may wait for a job to end — the spec's 0 to 50 seconds, inside a tool call's patience.</summary>
    public const int MostWaitSeconds = 50;

    /// <summary>How often a waiting <c>role_status</c> looks again.</summary>
    internal static readonly TimeSpan LookEvery = TimeSpan.FromMilliseconds(500);

    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    internal IReadOnlyList<RoleDescriptor> Descriptors { get; init; } = RoleDescriptors.Shipped;

    internal DetachedLauncher Launcher { get; init; } = new(new ProcessLauncher());

    /// <summary>How to start this binary again; a test points it at the built binary.</summary>
    internal (string Executable, IReadOnlyList<string> Prefix) Runner { get; init; } = LocalRuntime.SelfInvocation();

    internal TimeSpan CancelGrace { get; init; } = RoleJobLimits.CancelGrace;

    internal bool OnWsl { get; init; } = RoleJobSettings.OnWsl;

    /// <summary>The process environment the runner's <c>env.json</c> is taken from.</summary>
    internal IDictionary EnvironmentSource { get; init; } = Environment.GetEnvironmentVariables();

    private RoleJobStore Store() => new(dataDir, Clock, log, RoleJobProjection.For(dataDir, log));

    private RoleJobKeeper Keeper(RoleJobStore store) => new(store, notice, log);

    public async Task<string> StartAsync(string role, string repoPath, string? taskFile, string? brief, string? model, int? timeoutMinutes, string? workItem, CancellationToken ct = default)
    {
        var (request, refusal) = Checked(role, repoPath, taskFile, brief, model, timeoutMinutes, workItem);
        if (refusal.Length > 0)
        {
            return RoleJobAnswers.Refused(refusal);
        }

        var read = RoleJobSettings.Read(settings, OnWsl);
        if (read.Settings is not { } config)
        {
            return RoleJobAnswers.Refused(read.Refusal);
        }

        // The router first, before anything is made: a role whose list is empty, or a model the task may not take, is refused here with the setting that fixes it — no folder, no runner.
        var (plan, routeRefusal) = RoleRouting.Plan(RoleDescriptors.Find(Descriptors, role)!, request, settings);
        if (plan is null)
        {
            return RoleJobAnswers.Refused(routeRefusal);
        }

        var store = Store();
        var keeper = Keeper(store);
        await keeper.SweepAsync(ct);
        RoleJobRetention.Apply(store, log);

        return await MadeAsync(store, request, plan, config, ct);
    }

    private async Task<string> MadeAsync(RoleJobStore store, RoleJobRequest request, RoleJobPlan plan, RoleJobSettings config, CancellationToken ct)
    {
        Directory.CreateDirectory(store.JobsRoot);
        using var capLock = await CapLockAsync(store.JobsRoot, ct);
        if (capLock is null)
        {
            return RoleJobAnswers.Refused("another role start is in progress on this data directory; try again in a moment");
        }

        if (LiveJobs(store) is { Count: > 0 } live && live.Count >= config.MaxLiveJobs)
        {
            return RoleJobAnswers.Refused(
                $"{live.Count} role jobs are already running here and the cap is {config.MaxLiveJobs} ({RoleJobSettings.JobsMaxKey}); a start is refused, never queued. Running: {string.Join(", ", live)}. "
                + "Wait for one to end, or stop one with role_cancel.");
        }

        var made = request with { JobId = RoleJobIds.New(Clock) };
        var job = store.Create(made, plan);
        store.FolderOf(made.JobId)!.WriteEnvironment(RoleRunEnvironment.Capture(EnvironmentSource));

        return await LaunchedAsync(store, job, config, ct);
    }

    private async Task<string> LaunchedAsync(RoleJobStore store, RoleJobRecord job, RoleJobSettings config, CancellationToken ct)
    {
        var folder = store.FolderOf(job.JobId)!;
        var spec = new DetachSpec(
            Runner.Executable,
            [.. Runner.Prefix, .. RoleRunMode.ArgumentsFor(job.JobId, dataDir)],
            folder.Location,
            RoleJobIds.UnitOf(job.JobId),
            config.MemoryMegabytes);
        var launched = await Launcher.StartAsync(spec, config.Launcher, ct);
        if (launched.Started)
        {
            var recorded = store.Change(job.JobId, current => current with { Launcher = launched.Launcher });

            return RoleJobAnswers.Started(recorded is RoleJobWrite.Written written ? written.Job : job with { Launcher = launched.Launcher });
        }

        RoleJobFolder.DeleteQuietly(folder.EnvFile);
        var now = Clock.GetUtcNow().UtcDateTime;
        store.Change(job.JobId, current => current with { State = RoleJobStates.Failed, Stop = "launch-failed", Reason = launched.Detail, EndedUtc = now });

        return RoleJobAnswers.Refused($"the runner could not be started: {launched.Detail}", job.JobId);
    }

    /// <summary>The ids of the jobs that are not over, after a sweep closed the dead ones. Called under the cap lock.</summary>
    private static List<string> LiveJobs(RoleJobStore store) =>
        [.. store.JobIds().Where(id => store.Read(id) is { Terminal: false })];

    /// <summary>The lock that makes "count the live jobs, then make one" a single step, held open exclusively like every lock here; null when it could not be had.</summary>
    private async Task<FileStream?> CapLockAsync(string jobsRoot, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetDirectoryName(jobsRoot)!, "cap.lock");
        var until = Clock.GetTimestamp() + (2 * Clock.TimestampFrequency);
        while (true)
        {
            if (HeldFile.TryHold(file) is { } held)
            {
                return held;
            }

            if (Clock.GetTimestamp() >= until)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), Clock, ct);
        }
    }

    private (RoleJobRequest Request, string Refusal) Checked(string role, string repoPath, string? taskFile, string? brief, string? model, int? timeoutMinutes, string? workItem)
    {
        var none = new RoleJobRequest();
        if (RoleDescriptors.Find(Descriptors, role) is null)
        {
            return (none, $"role '{role}' has no descriptor in this build, so it cannot be started. Roles that can be started: {string.Join(", ", Descriptors.Select(descriptor => descriptor.Role))}");
        }

        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return (none, $"repoPath '{repoPath}' is not a folder on this machine");
        }

        var (file, fileRefusal) = RoleRequestChecks.RelativeFile(taskFile, "taskFile");
        var text = brief ?? string.Empty;
        if (fileRefusal.Length > 0 || (file.Length == 0) == string.IsNullOrWhiteSpace(text) || text.Length > RoleRequestChecks.MaxBriefLength)
        {
            return (none, fileRefusal.Length > 0 ? fileRefusal : $"send exactly one of taskFile (a path inside the repository) or brief (text of at most {RoleRequestChecks.MaxBriefLength} characters)");
        }

        return Bounded(none with { Role = role, RepoPath = Path.GetFullPath(repoPath), TaskFile = file, Brief = text }, model, timeoutMinutes, workItem);
    }

    private static (RoleJobRequest Request, string Refusal) Bounded(RoleJobRequest request, string? model, int? timeoutMinutes, string? workItem)
    {
        var minutes = timeoutMinutes ?? RoleJobLimits.DefaultTimeoutMinutes;
        if (minutes is < 1 or > RoleRequestChecks.MostTimeoutMinutes)
        {
            return (request, $"timeoutMinutes must be from 1 to {RoleRequestChecks.MostTimeoutMinutes} (the default is {RoleJobLimits.DefaultTimeoutMinutes})");
        }

        if (RoleRequestChecks.ModelRefusal(model) is { Length: > 0 } modelRefusal)
        {
            return (request, modelRefusal);
        }

        var (item, itemRefusal) = WorkItems.Check(workItem);

        return itemRefusal.Length > 0
            ? (request, itemRefusal)
            : (request with { Model = model ?? string.Empty, TimeoutMinutes = minutes, WorkItem = item }, string.Empty);
    }

    public async Task<string> StatusAsync(string jobId, int maxSeconds = 0, CancellationToken ct = default)
    {
        if (maxSeconds is < 0 or > MostWaitSeconds)
        {
            return RoleJobAnswers.Refused($"maxSeconds must be from 0 to {MostWaitSeconds}");
        }

        if (!RoleJobIds.IsValid(jobId))
        {
            return RoleJobAnswers.Refused($"'{jobId}' is not a role job id");
        }

        var store = Store();
        var keeper = Keeper(store);
        var until = Clock.GetTimestamp() + (maxSeconds * Clock.TimestampFrequency);
        while (true)
        {
            if (await keeper.InspectAsync(jobId, ct) is not { } seen)
            {
                return RoleJobAnswers.Refused($"there is no role job '{jobId}' on this data directory (a finished job's folder is removed after 30 days)");
            }

            if (seen.Job.Terminal || Clock.GetTimestamp() >= until)
            {
                return RoleJobAnswers.Status(seen, store.FolderOf(jobId)!.ReadResult());
            }

            await Task.Delay(LookEvery, Clock, ct);
        }
    }

    public async Task<string> CancelAsync(string jobId, CancellationToken ct = default)
    {
        if (!RoleJobIds.IsValid(jobId))
        {
            return RoleJobAnswers.Refused($"'{jobId}' is not a role job id");
        }

        var store = Store();
        var before = store.Read(jobId);
        if (before is null)
        {
            return RoleJobAnswers.Refused($"there is no role job '{jobId}' on this data directory");
        }

        var after = await Keeper(store).CancelAsync(jobId, CancelGrace, ct) ?? store.Read(jobId) ?? before;

        return RoleJobAnswers.Cancelled(after, before.Terminal);
    }

    /// <summary>The start-up sweep: every job that is not over gets one look (a dead runner's is closed as lost, its child stopped), then the retention runs.</summary>
    public async Task SweepAsync(CancellationToken ct = default)
    {
        var store = Store();
        await Keeper(store).SweepAsync(ct);
        RoleJobRetention.Apply(store, log);
    }
}

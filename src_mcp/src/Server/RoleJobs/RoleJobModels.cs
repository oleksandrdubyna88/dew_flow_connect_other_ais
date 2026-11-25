using System.Text.Json.Serialization;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>The states of a role job (spec A): <c>queued → starting → running → succeeded | failed | cancelled | lost</c>. A model switch stays inside <c>running</c>.</summary>
internal static class RoleJobStates
{
    public const string Queued = "queued";
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Lost = "lost";

    /// <summary>A terminal state is final: nothing writes a job in one again, except the retention that removes it.</summary>
    public static bool IsTerminal(string state) => state is Succeeded or Failed or Cancelled or Lost;
}

/// <summary>A process named the only safe way: its number AND when it started (the identity <c>OrphanSweep</c> and <c>ProcessTree</c> use).</summary>
internal sealed record RoleChild(int Pid, DateTime StartedUtc)
{
    internal Runners.Processes.ProcessIdentity Identity => new(Pid, StartedUtc);
}

/// <summary>One try of one model (spec A, <c>role_status</c>). Filled by the failover loop of story s3; a job of story s1 has none.</summary>
internal sealed record RoleAttempt(int N, string Model, string Vendor, string Stop, string Reason, string EvidenceRef)
{
    public DateTime? StartedUtc { get; init; }

    public DateTime? EndedUtc { get; init; }

    public long TokensIn { get; init; }

    public long TokensOut { get; init; }

    /// <summary>
    /// The claude key this attempt ran on (epoch 3 E2 s7): the pool key's id, <c>person-sign-in</c> when the device switch ran it on the person's own sign-in, empty when the attempt carried no key (another vendor, or a job from before the pool). Not a secret: a hash prefix.
    /// </summary>
    public string KeyId { get; init; } = string.Empty;

    public RoleAttempt Normalised() => this with { Model = Model ?? string.Empty, Vendor = Vendor ?? string.Empty, Stop = Stop ?? string.Empty, Reason = Reason ?? string.Empty, EvidenceRef = EvidenceRef ?? string.Empty, KeyId = KeyId ?? string.Empty };
}

/// <summary>
/// <c>request.json</c>: what the caller asked for, written once when the job is made and never changed.
/// </summary>
internal sealed record RoleJobRequest
{
    public string JobId { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string RepoPath { get; init; } = string.Empty;

    /// <summary>The repository-relative task or epic file the role is for; empty when a <see cref="Brief"/> was sent instead.</summary>
    public string TaskFile { get; init; } = string.Empty;

    public string Brief { get; init; } = string.Empty;

    /// <summary>The model the caller named, or empty for the role's own list.</summary>
    public string Model { get; init; } = string.Empty;

    public int TimeoutMinutes { get; init; }

    public string WorkItem { get; init; } = string.Empty;

    /// <summary>The commit a coder's worktree starts at (a full 40-character id); empty for "the HEAD of the repository when the coder's work began" (epoch 3 E2 s3 t3). Other roles ignore it.</summary>
    public string BaseSha { get; init; } = string.Empty;

    public DateTime CreatedUtc { get; init; }

    /// <summary>Strings a reader may meet as null (a field an older or newer build did not write, doctrine 4a) become empty; the request is read through this.</summary>
    public RoleJobRequest Normalised() => this with
    {
        JobId = JobId ?? string.Empty,
        Role = Role ?? string.Empty,
        RepoPath = RepoPath ?? string.Empty,
        TaskFile = TaskFile ?? string.Empty,
        Brief = Brief ?? string.Empty,
        Model = Model ?? string.Empty,
        WorkItem = WorkItem ?? string.Empty,
        BaseSha = BaseSha ?? string.Empty,
    };
}

/// <summary>
/// <c>job.json</c> — THE AUTHORITY on one job (decision 1 of spec section 0). The rounds database holds a projection of it, never the other way round.
/// </summary>
/// <remarks>
/// <see cref="Generation"/> is the fence: whoever takes the lease adds one, and every later write compares it. <see cref="Runner"/> and <see cref="Child"/> are identities (pid and start time) so
/// a cancel or a sweep kills the process that was recorded and never a stranger that inherited the number.
/// </remarks>
internal sealed record RoleJobRecord
{
    public string JobId { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string Repo { get; init; } = string.Empty;

    public string WorkItem { get; init; } = string.Empty;

    public string TaskFile { get; init; } = string.Empty;

    /// <summary>The commit a coder's worktree starts at, settled at the start (<see cref="CoderBase"/>); empty for every other role and for a job written before the field.</summary>
    public string BaseSha { get; init; } = string.Empty;

    /// <summary>
    /// The plan a coder job was started under — the branch of the repository when it started, the session of that branch and the plan revision that session had proceeded (<see cref="CoderPlanBinding"/>). Null for every other role
    /// and for a job written before the field, which the gate then refuses to bind. Read by the gate from here, never from the branch name.
    /// </summary>
    public CoderPlanLink? Plan { get; init; }

    public string State { get; init; } = RoleJobStates.Queued;

    public int Generation { get; init; }

    /// <summary>The road the runner was started by: <c>systemd-run</c>, <c>setsid</c>, <c>sh-nohup</c>, <c>windows</c>, <c>windows-in-job</c>; empty until a launcher answered.</summary>
    public string Launcher { get; init; } = string.Empty;

    public int TimeoutMinutes { get; init; }

    /// <summary>The model that gave (or last tried to give) the answer; empty while none has run.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>The ordered model list the router chose (story s3); empty until it runs.</summary>
    public IReadOnlyList<string> Route { get; init; } = [];

    public IReadOnlyList<RoleAttempt> Attempts { get; init; } = [];

    /// <summary>Why a job that did not succeed ended: a short word. Empty for a job that is not over, or succeeded.</summary>
    public string Stop { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public int RepairTurns { get; init; }

    public int Complexity { get; init; }

    public bool Security { get; init; }

    public DateTime CreatedUtc { get; init; }

    public DateTime? StartedUtc { get; init; }

    public DateTime? EndedUtc { get; init; }

    public DateTime UpdatedUtc { get; init; }

    /// <summary>The runner process, recorded at take-over — what a cancel kills when the runner stops answering.</summary>
    public RoleChild? Runner { get; init; }

    /// <summary>The CLI child the runner owns right now, if it has one.</summary>
    public RoleChild? Child { get; init; }

    public bool Terminal => RoleJobStates.IsTerminal(State);

    /// <summary>A record as read back: a string or list a writer left out is empty, never null.</summary>
    public RoleJobRecord Normalised() => this with
    {
        JobId = JobId ?? string.Empty,
        Role = Role ?? string.Empty,
        Repo = Repo ?? string.Empty,
        WorkItem = WorkItem ?? string.Empty,
        TaskFile = TaskFile ?? string.Empty,
        BaseSha = BaseSha ?? string.Empty,
        Plan = Plan?.Normalised(),
        State = State ?? RoleJobStates.Queued,
        Launcher = Launcher ?? string.Empty,
        Model = Model ?? string.Empty,
        Stop = Stop ?? string.Empty,
        Reason = Reason ?? string.Empty,
        Route = Route ?? [],
        Attempts = [.. (Attempts ?? []).Select(attempt => attempt.Normalised())],
    };
}

/// <summary><c>heartbeat</c>: written by the lease holder every <see cref="RoleJobLimits.HeartbeatEvery"/>. Older than <see cref="RoleJobLimits.StalledAfter"/> with the lease held is <c>stalled</c>.</summary>
internal sealed record RoleHeartbeat(int Generation, DateTime Utc, string Phase, int Attempt, int ChildPid)
{
    public RoleHeartbeat Normalised() => this with { Phase = Phase ?? string.Empty };
}

/// <summary>The environment the runner restores: <c>env.json</c>, owner-only, read once and deleted.</summary>
internal sealed record RoleJobEnvironment(IReadOnlyDictionary<string, string> Variables);

/// <summary>The numbers of spec A in one place.</summary>
internal static class RoleJobLimits
{
    public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(15);

    /// <summary>A heartbeat older than this, with the lease held, is <c>stalled</c> — never <c>lost</c> (decision 2 of spec section 0).</summary>
    public static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(2);

    /// <summary>A free lease on a job that is not over is a dead runner only after this long, so a runner still starting is not taken for one.</summary>
    public static readonly TimeSpan LostGrace = TimeSpan.FromSeconds(30);

    /// <summary><c>role_cancel</c> waits this long for the runner to stop by itself before it kills the recorded tree.</summary>
    public static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(10);

    /// <summary>A terminal job folder goes after this long (rows stay).</summary>
    public static readonly TimeSpan RetainFor = TimeSpan.FromDays(30);

    /// <summary>At most this many terminal job folders are kept, the newest.</summary>
    public const int RetainAtMost = 200;

    /// <summary>The default and the WSL cap on live jobs (<c>COAI_ROLE_JOBS_MAX</c>).</summary>
    public const int DefaultLiveJobs = 3;

    public const int DefaultLiveJobsOnWsl = 2;

    /// <summary>The default <c>MemoryMax</c> of a runner's unit, in MiB (<c>COAI_ROLE_MEMORY_MB</c>).</summary>
    public const int DefaultMemoryMegabytes = 4096;

    public const int DefaultTimeoutMinutes = 30;
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RoleJobRecord))]
[JsonSerializable(typeof(RoleJobRequest))]
[JsonSerializable(typeof(RoleHeartbeat))]
[JsonSerializable(typeof(RoleJobEnvironment))]
internal sealed partial class RoleJobsJson : JsonSerializerContext;

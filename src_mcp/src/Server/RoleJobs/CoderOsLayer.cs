using System.Globalization;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// A stop the OS layer put on a coder launch (epoch 3 E2, s2/s3 integration): the layer refused a write the coder made, or refused to start the coder at all. Either ends the job — never green, never a failover to another model: the next model
/// would meet the same layer, and a coder that was stopped from writing did not do its work.
/// </summary>
/// <param name="Kind">The word the attempt's ledger line and evidence carry: <c>write-stopped</c> or <c>boundary-refused</c>.</param>
/// <param name="Sentence">What a person reads: the boundary's own sentence, with the paths it refused.</param>
/// <param name="Started">True when the coder process ran (a write was stopped, and the launch consumed what it consumed); false when the preflight refused it first.</param>
internal sealed record CoderOsStop(string Kind, string Sentence, bool Started);

/// <summary>
/// The OS layer of ONE coder job, as the role work holds it (epoch 3 E2, s2/s3 integration; <c>v2/architecture/server.md</c> § 16 item 8): the job's folders, the <see cref="CoderTurns"/> that runs the claude coder inside its unit, and everything the
/// turns of the job leave behind — the unit part counter (every turn is a unit of its own, <c>coai-coder-&lt;job&gt;-c1</c>, <c>-c2</c>…) and the evidence about the MAIN checkout that each turn took.
/// </summary>
/// <remarks>
/// <para><b>The one road.</b> Every launch of a coder — turn 0, the repair turn, the fix turns of the checks — goes through <see cref="TurnAsync"/>; <see cref="RoleModelLauncher"/> refuses a coder launch that has no layer. The failover loop around it
/// is unchanged: the models of the route in order, the classification of a limit, a refusal or a crash, the ledger, the switch line and the notice.</para>
/// <para><b>The coder's home lives as long as the job.</b> It is under <see cref="JobFolder"/> and is made by the first preflight; a <c>--resume</c> of the repair turn reads the same home, which is where the CLI keeps its transcript. Removing it is the
/// retention sweep's, with the worktree — never a turn's.</para>
/// <para><b>Evidence, never a verdict</b> (decision 8): <see cref="Evidence"/> is the union of what each turn's two snapshots of the main checkout showed. A person's edit of the main checkout during the job shows there and fails nothing.</para>
/// </remarks>
internal sealed class CoderOsLayer(BoundaryJob job, CoderTurns turns, IProcessLauncher launcher)
{
    /// <summary>The variable that moves the folders the coder's homes live in; an absolute path (the same rule as <see cref="CoderWorktree.RootVariable"/>).</summary>
    public const string JobsVariable = "COAI_CODER_JOBS";

    private readonly Lock _gate = new();
    private readonly List<CoderMainEvidence> _evidence = [];
    private int _parts;

    /// <summary>The submodules of the job as coai made them (<see cref="CoderSubmodules"/>): written into the record of every launch. None for a job with no submodule.</summary>
    public CoderMounts Mounts { get; init; } = CoderMounts.None;

    /// <summary>The job the layer binds: worktree, main checkout and the job's own folders.</summary>
    public BoundaryJob Job => job;

    /// <summary>The job's one launcher: what starts the coder's units starts the checks' too, so every child of the job is one the runner recorded.</summary>
    public IProcessLauncher Launcher => launcher;

    /// <summary>The boundary the job's checks run in — the one the turns run in, on the same machine.</summary>
    public CheckBoundary ChecksBoundary() => turns.ChecksBoundary();

    /// <summary>
    /// The folder of one job's own files (home, temp, caches of the coder): <see cref="JobsVariable"/> or the machine-local <c>coder-jobs</c> folder, then the job id. Never inside the coder root — the coder root is read-only to the coder, and
    /// another job's home must not be readable from this job's unit — and never inside the data directory (a network share, perhaps).
    /// </summary>
    public static string JobFolder(Func<string, string?> settings, Action<string> said, string jobId) =>
        Path.Combine(ReviewTreeRoot.DefaultIn(settings, said, JobsVariable, ReviewTreeRoot.MachineLocal("coder-jobs"), "coder job folders"), jobId);

    /// <summary>The turns of a job as the product makes them: the boundary on this machine, the evidence from coai's own git, and every process through <paramref name="launcher"/>.</summary>
    public static CoderTurns ShippedTurns(IProcessLauncher launcher) =>
        new(new CoderBoundary(launcher, BoundaryHost.Current()), new FilesystemInvariant(launcher));

    /// <summary>One launch of the coder inside the layer: the next unit part, the turn, its evidence kept, and — when <paramref name="recordTo"/> names a folder — the properties of its unit written there (<see cref="CoderUnitRecord"/>).</summary>
    public async Task<CoderTurnOutcome> TurnAsync(ProcessRequest request, CancellationToken ct, string recordTo = "")
    {
        var part = "c" + Interlocked.Increment(ref _parts).ToString(CultureInfo.InvariantCulture);
        var outcome = await turns.RunAsync(job, request, part, ct);
        Keep(outcome);
        if (recordTo.Length > 0 && outcome.Boundary is { } record)
        {
            CoderUnitRecord.Write(recordTo, part, CoderUnitRecord.UnitName(job, part), record, Mounts, outcome.UnitStarted);
        }

        return outcome;
    }

    /// <summary>What the layer says of the launch when it ends the job, or null when the launch is for the failover loop to read as it reads any other.</summary>
    public static CoderOsStop? StopOf(CoderTurnOutcome outcome) => outcome switch
    {
        CoderTurnOutcome.WriteStopped stopped => new CoderOsStop("write-stopped", stopped.Sentence, Started: true),
        CoderTurnOutcome.Refused refused => new CoderOsStop("boundary-refused", refused.Sentence, Started: false),
        _ => null,
    };

    private void Keep(CoderTurnOutcome outcome)
    {
        var seen = outcome switch
        {
            CoderTurnOutcome.Finished finished => finished.Evidence,
            CoderTurnOutcome.Failed failed => failed.Evidence,
            CoderTurnOutcome.WriteStopped stopped => stopped.Evidence,
            _ => null,
        };
        if (seen is not null)
        {
            lock (_gate)
            {
                _evidence.Add(seen);
            }
        }
    }

    /// <summary>
    /// What the turns so far showed of the main checkout, as one record: every path that differed in any turn (once), and a note for a reader. <see cref="CoderMainEvidence.None"/> when no turn took one. <c>Taken</c> is false when ANY
    /// turn's snapshot could not be taken: a partial record must not read as a clean one.
    /// </summary>
    public CoderMainEvidence Evidence
    {
        get
        {
            CoderMainEvidence[] seen;
            lock (_gate)
            {
                seen = [.. _evidence];
            }

            if (seen.Length == 0)
            {
                return CoderMainEvidence.None;
            }

            var changes = seen.SelectMany(evidence => evidence.Changes).DistinctBy(change => (change.Path, change.What)).ToList();

            return new CoderMainEvidence(seen.All(evidence => evidence.Taken), changes, string.Join(" | ", seen.Select(evidence => evidence.Note).Distinct(StringComparer.Ordinal)));
        }
    }
}

/// <summary>
/// The launcher the executor of ONE coder launch runs through: <see cref="ReviewerExecutor"/> asks it for the process, and it answers by running the coder inside the layer. What the executor reads back is a plain <see cref="ProcessResult"/> — so the
/// classification of a limit, a crash and a timeout is the failover's own — and what the layer said beyond that is on <see cref="Outcome"/>.
/// </summary>
internal sealed class CoderTurnLauncher(CoderOsLayer layer, string recordTo = "") : IProcessLauncher
{
    /// <summary>What the layer made of the launch; null until the launch has run.</summary>
    public CoderTurnOutcome? Outcome { get; private set; }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        try
        {
            Outcome = await layer.TurnAsync(request, ct, recordTo);
        }
        catch (OperationCanceledException)
        {
            // The caller's cancel reaches here as an exception when it fired between two steps of the turn. The failover reads a cancelled launch from the result, as it reads the launcher's own.
            return new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: true);
        }

        return Outcome switch
        {
            CoderTurnOutcome.Finished finished => finished.Run.Result,
            CoderTurnOutcome.WriteStopped stopped => stopped.Run.Result,
            CoderTurnOutcome.Failed failed => Failure(failed),
            CoderTurnOutcome.Refused refused => new ProcessResult(-1, string.Empty, refused.Sentence, TimedOut: false),
            _ => new ProcessResult(-1, string.Empty, "the coder launch ended in a way coai does not know", TimedOut: false),
        };
    }

    /// <summary>A failed turn keeps the process's own result — its exit, its deadline, its cancel — except where the process said exit 0 and the layer did not trust it (a unit that would not stop): that is a failed launch, never an answer.</summary>
    private static ProcessResult Failure(CoderTurnOutcome.Failed failed) =>
        failed.Run.Result is { ExitCode: 0, TimedOut: false, Cancelled: false } ? new ProcessResult(1, string.Empty, failed.Sentence, TimedOut: false) : failed.Run.Result;
}

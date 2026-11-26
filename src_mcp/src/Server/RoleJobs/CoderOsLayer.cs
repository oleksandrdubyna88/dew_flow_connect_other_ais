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
    public async Task<CoderTurnOutcome> TurnAsync(ProcessRequest request, CancellationToken ct, string recordTo = "", ICoderVendor? vendor = null)
    {
        var part = "c" + Interlocked.Increment(ref _parts).ToString(CultureInfo.InvariantCulture);
        var outcome = await turns.RunAsync(job, request, part, vendor, ct);
        Keep(outcome);
        if (recordTo.Length > 0 && outcome.Boundary is { } record)
        {
            CoderUnitRecord.Write(recordTo, part, CoderUnitRecord.UnitName(job, part), record, Mounts, outcome.UnitStarted);
        }

        return outcome;
    }

    /// <summary>
    /// The job leaves <paramref name="vendor"/> for another (epoch 3 E3 s8 t1): what its sign-in left in the job's home is deleted before the unit of the next vendor starts (<see cref="CoderSignInCopy"/>) — its COPY, or, for a bind of a real file (claude's), the empty place the bind left. Answers
    /// empty, or the sentence of a file that could not be deleted — the next vendor must then not start.
    /// </summary>
    public string LeaveVendor(ICoderVendor vendor)
    {
        // A copy is deleted. A BIND of the real file (claude's) leaves the empty place it was bound onto in the home when its unit ends, and the next vendor's preflight asks whether that sign-in is visible: the empty place goes too.
        var said = CoderSignInCopy.Remove(job.CoderHomeFolder, vendor.Home(job).SignInFile, mountPointOnly: vendor.SignIn is CoderSignInRecipe.BindRealFile);

        return said.Length == 0 ? string.Empty : $"the sign-in copy of {vendor.Id} could not be removed before the next vendor started: {said}";
    }

    /// <summary>What the layer says of the launch when it ends the job, or null when the launch is for the failover loop to read as it reads any other.</summary>
    public static CoderOsStop? StopOf(CoderTurnOutcome outcome) => outcome switch
    {
        CoderTurnOutcome.WriteStopped stopped => new CoderOsStop("write-stopped", stopped.Sentence, Started: true),
        CoderTurnOutcome.Refused refused => new CoderOsStop("boundary-refused", refused.Sentence, Started: false),
        CoderTurnOutcome.UnitNotStopped notStopped => new CoderOsStop("unit-not-stopped", notStopped.Sentence, Started: true),
        _ => null,
    };

    private void Keep(CoderTurnOutcome outcome)
    {
        var seen = outcome switch
        {
            CoderTurnOutcome.Finished finished => finished.Evidence,
            CoderTurnOutcome.Failed failed => failed.Evidence,
            CoderTurnOutcome.WriteStopped stopped => stopped.Evidence,
            CoderTurnOutcome.UnitNotStopped notStopped => notStopped.Evidence,
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
internal sealed class CoderTurnLauncher(CoderOsLayer layer, string recordTo = "", ICoderVendor? vendor = null) : IProcessLauncher
{
    /// <summary>What the layer made of the launch; null until the launch has run.</summary>
    public CoderTurnOutcome? Outcome { get; private set; }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        try
        {
            Outcome = await layer.TurnAsync(request, ct, recordTo, vendor);
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
            CoderTurnOutcome.UnitNotStopped notStopped => notStopped.Run.Result,
            CoderTurnOutcome.Failed failed => failed.Run.Result,
            CoderTurnOutcome.Refused refused => new ProcessResult(-1, string.Empty, refused.Sentence, TimedOut: false),
            _ => new ProcessResult(-1, string.Empty, "the coder launch ended in a way coai does not know", TimedOut: false),
        };
    }
}

/// <summary>
/// The deletion of the sign-in COPY of a vendor the job has left (epoch 3 E3 s8 t1; SPEC E7 and F, judgement S3): a copy lies in the job's coder home, which every unit of the job binds, so the unit of the NEXT vendor would see it. It is deleted before that unit starts.
/// </summary>
/// <remarks>
/// <b>It never follows a link out of the home.</b> The coder writes into its home, and a link it made where the copy's folder is (<c>.codex</c> to a person's real folder) would make a delete through it delete the person's own sign-in. The folder is resolved first; a copy whose
/// resolved folder is not inside the home's resolved path is not deleted and is said. This runs only when the vendor's unit was SEEN GONE: <c>CoderTurns.Classify</c> asks <c>BoundaryRun.Gone</c> first, for every exit, and a unit that is still there is <c>CoderTurnOutcome.UnitNotStopped</c>, which <see cref="CoderOsLayer.StopOf"/> turns into the job-ending stop <c>coder-boundary</c> (kind <c>unit-not-stopped</c>) for turn 0, the repair turn and every fix turn — so <see cref="RoleFailover"/>, which calls this between two attempts, is never reached with a live unit, and nothing races the resolution. <c>TimeoutStopSec=10</c> on every unit makes "gone" the usual end of a process that ignores the stop signal (<c>ACoderUnitThatCannotBeStoppedTests</c>, <c>ACoderFailsOverAcrossVendorsTests</c>).
/// </remarks>
internal static class CoderSignInCopy
{
    /// <summary>
    /// Deletes <paramref name="copy"/> when it lies in <paramref name="home"/>; answers empty when there is no copy now (it was deleted or never made), else the sentence that says why it could not be. <paramref name="mountPointOnly"/>: the file is the empty place a BIND of a real
    /// sign-in file left behind in the home when its unit ended (systemd makes the target of a file bind), not a copy: it is deleted only when it is a regular file with nothing in it.
    /// </summary>
    public static string Remove(string home, string copy, bool mountPointOnly = false)
    {
        var folder = Path.GetDirectoryName(copy);
        if (string.IsNullOrEmpty(folder))
        {
            return $"the sign-in copy at '{copy}' is not in a folder";
        }

        // A folder that is not there at all: the vendor never got as far as making it (its CLI was missing, its probe refused), so there is nothing of its to delete (security verify pass 3, S21). Only a MISSING entry is this: a link (even a dangling one) or a file of
        // another type at the folder's place goes on to the checks below, and RealPath.Resolve answers OK for a path whose last names are missing, so it cannot be the one to say "absent".
        try
        {
            if (Runners.Files.RealPath.Lstat(folder).Kind == Runners.Files.EntryKind.Missing)
            {
                return string.Empty;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the folder of the sign-in copy at '{copy}' could not be looked at, so the copy was not deleted: {e.Message}";
        }

        var realHome = Runners.Files.RealPath.Resolve(home);
        var realFolder = Runners.Files.RealPath.Resolve(folder);
        if (!realHome.Ok)
        {
            return $"the job's home '{home}' could not be resolved, so a sign-in copy in it was not deleted";
        }

        if (!realFolder.Ok)
        {
            // The folder is not there (nothing was made) or is a link that leads nowhere: there is no file to leave behind through it.
            return Runners.Files.RealPath.Lstat(folder) == Runners.Files.Entry.Missing ? string.Empty : $"the folder of the sign-in copy at '{copy}' could not be resolved, so the copy was not deleted";
        }

        var inside = realFolder.Path == realHome.Path || realFolder.Path.StartsWith(realHome.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!inside)
        {
            return $"the folder of the sign-in copy at '{copy}' leads out of the job's home ({realFolder.Path}), so the copy was not deleted through it";
        }

        var target = Path.Combine(realFolder.Path, Path.GetFileName(copy));
        if (Runners.Files.RealPath.Lstat(realFolder.Path).Kind != Runners.Files.EntryKind.Directory)
        {
            return $"what stands where the folder of the sign-in copy at '{copy}' belongs is not a folder, so the copy was not deleted";
        }

        try
        {
            if (Runners.Files.RealPath.Lstat(target).Kind == Runners.Files.EntryKind.Missing)
            {
                // The folder is there and the copy is not: nothing to delete.
                return string.Empty;
            }

            if (Directory.Exists(target) && Runners.Files.RealPath.Lstat(target) == Runners.Files.Entry.Directory)
            {
                return $"a folder stands where the sign-in copy at '{copy}' belongs, so it was not deleted as a file";
            }

            if (mountPointOnly && Runners.Files.RealPath.Lstat(target) is { Kind: not Runners.Files.EntryKind.Missing } entry && (entry.Kind != Runners.Files.EntryKind.File || new FileInfo(target).Length > 0))
            {
                return $"the file at '{copy}' is not the empty place of a bind, so it was not deleted";
            }

            File.Delete(target);

            return File.Exists(target) || Runners.Files.RealPath.Lstat(target) != Runners.Files.Entry.Missing ? $"the sign-in copy at '{copy}' is still there after its deletion" : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the sign-in copy at '{copy}' could not be deleted: {e.Message}";
        }
    }
}

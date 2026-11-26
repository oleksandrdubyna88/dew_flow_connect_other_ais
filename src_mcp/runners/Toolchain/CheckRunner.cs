using System.ComponentModel;
using System.Diagnostics;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>Where and how a run of the checks happens.</summary>
/// <param name="Tree">The folder the steps run in and the repository root their paths are relative to: the coder's worktree at its head.</param>
/// <param name="LogFolder">Where each step's log is written; made if it is not there.</param>
/// <param name="Jobs">What <c>{jobs}</c> becomes in an argv: 4 for a heavy job alone, 2 when two run (server.md § 9). At least 1.</param>
public sealed record CheckRequest(string Tree, string LogFolder, int Jobs)
{
    /// <summary>
    /// Set for a CODER job: its steps then run inside the OS boundary (<see cref="CheckBoundary"/>) or the run is refused — there is no coder road without it. Absent for a tree that is not a coder's (the recorded run on a clean clone).
    /// </summary>
    public BoundaryJob? Job { get; init; }

    /// <summary>
    /// Called with each step's record the moment the step has ended, before the next step is started — also for the step that failed. A caller that keeps a record of each step (the coder's unit records) writes it here, so a cancel or a hang in a LATER step
    /// does not lose the records of the steps that already ran.
    /// </summary>
    public Action<StepRecord>? StepFinished { get; init; }
}

/// <summary>
/// Runs the step plan of a toolchain in a tree — restore, build, typecheck and test of each project, in that order — and writes down what each step did (server.md § 10, § 12).
/// </summary>
/// <remarks>
/// <para><b>A coder job runs inside an OS boundary or is refused.</b> A request that names a <see cref="BoundaryJob"/> goes through <see cref="CheckBoundary"/>: a preflight before the job, then each step in its own named <c>systemd-run --user</c> unit (the environment
/// is set by <c>env -i</c> INSIDE the unit, so the minimal list below is the unbounded road's). A runner with no boundary refuses such a request: <see cref="CheckState.BoundaryRefused"/>. A request with no job is the road a tree that is not a coder's takes — the recorded run on a clean clone.
/// <b>This is the one road: every step of a coder job, whatever its source, starts in <see cref="RunBoundedStepAsync"/>.</b></para>
/// <para><b>Every process goes through <see cref="IProcessLauncher"/></b> — the one launcher, so the deadline and the kill of the WHOLE process tree on expiry are the tested ones. The argv is started with no
/// shell: an argument with <c>;</c>, <c>$(…)</c> or a backtick reaches the program as text. The environment is the minimal list (<see cref="ProcessEnvironment.Minimal"/>) and the adapter's switches, a closed table of names with constant values; none of this product's own variables, and no vendor key, reaches a build.</para>
/// <para><b>Order and stop.</b> Projects in the plan's order, each restore, build, typecheck, test; the first step that does not pass ends the run, so a build that fails is never followed by its test (a test of a tree that
/// does not build says nothing). <b>No command to run is never green</b>: a plan with no build, typecheck or test step is <c>not_configured</c>, and a restore alone is not a check.</para>
/// <para><b>The working folder.</b> A step of the file runs from the repository root, as before. A detected or defaulted step runs from its project folder, because npm and its tools read their configuration from the current folder; the folder must be the worktree or a REAL folder inside it (no link on
/// the way, resolved by the product's one link resolver) or the step is refused before a unit is described.</para>
/// <para><b>This runs the toolchain it is GIVEN.</b> Which file, or which manifests, that is — the tree's, or for a coder job the one at the base commit — is decided before, by the caller; a coder job goes through
/// <see cref="ChecksAtBase"/>, never through a file the coder could have written.</para>
/// </remarks>
public sealed class CheckRunner(IProcessLauncher launcher, CheckBoundary? boundary = null)
{
    private const string NothingToCheck = "no project of the toolchain file names a build or a test command, so nothing was run; checks that did not run are never green";

    /// <summary>Runs the checks of <paramref name="toolchain"/> (a toolchain FILE) in <paramref name="request"/>'s tree.</summary>
    public async Task<CheckResult> RunAsync(ToolchainRead toolchain, CheckRequest request, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Jobs, 1);

        return toolchain switch
        {
            ToolchainRead.Configured configured => await RunPlanAsync(StepPlan.OfFile(configured.Toolchain, request.Jobs), request, ct),
            ToolchainRead.Refused refused => new CheckResult(CheckState.ToolchainRefused, [], $"the toolchain file is refused, so nothing was run: {refused.Sentence}"),
            _ => new CheckResult(CheckState.NotConfigured, [], $"there is no {ToolchainTables.FilePath}, so nothing was run; checks that did not run are never green"),
        };
    }

    /// <summary>Runs the checks of a RESOLVED toolchain (<see cref="ToolchainResolve"/>): the file's, the defaults of a folder, or the detected steps. <c>not_configured</c> and <c>toolchain_refused</c> plan no step.</summary>
    public async Task<CheckResult> RunResolvedAsync(ToolchainResolution resolution, CheckRequest request, CancellationToken ct = default, AffectedSelection? selection = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Jobs, 1);

        return resolution switch
        {
            ToolchainResolution.Resolved resolved => (await RunPlanAsync(StepPlan.Of(resolved.Toolchain, ToolchainDefaults.Current, request.Jobs, selection), request, ct)) with { Toolchain = resolved.Toolchain, Selection = selection },
            ToolchainResolution.Refused refused => new CheckResult(CheckState.ToolchainRefused, [], $"the toolchain is refused, so nothing was run: {refused.Sentence}"),
            ToolchainResolution.NotConfigured notConfigured => new CheckResult(CheckState.NotConfigured, [], notConfigured.Note),
            _ => throw new InvalidOperationException("a resolution is resolved, refused or not configured"),
        };
    }

    private async Task<CheckResult> RunPlanAsync(IReadOnlyList<PlannedStep> plan, CheckRequest request, CancellationToken ct)
    {
        if (!StepPlan.Checks(plan))
        {
            return new CheckResult(CheckState.NotConfigured, [], NothingToCheck);
        }

        Directory.CreateDirectory(request.LogFolder);
        var gate = await AdmitAsync(plan, request, ct);
        if (gate is Gate.Closed closed)
        {
            return closed.Result;
        }

        var done = new List<StepRecord>();
        foreach (var (step, number) in plan.Select((step, index) => (step, index + 1)))
        {
            var record = gate is Gate.Bounded bounded
                ? await RunBoundedStepAsync(step, number, request, bounded.Admitted, ct)
                : await RunStepAsync(step, number, request, ct);
            done.Add(record);
            request.StepFinished?.Invoke(record);
            if (!record.Passed)
            {
                return new CheckResult(CheckState.Failed, done, $"{Describe(record)}; the steps after it were not run");
            }
        }

        return new CheckResult(CheckState.Green, done, $"{done.Count} step(s) ran and every one exited 0");
    }

    /// <summary>Whether the run may start, and inside what.</summary>
    private abstract record Gate
    {
        /// <summary>Not a coder job's run: the steps start as they always did.</summary>
        public sealed record Unbounded : Gate;

        /// <summary>A coder job whose boundary held: every step runs in a unit.</summary>
        public sealed record Bounded(BoundaryAdmission.Admitted Admitted) : Gate;

        /// <summary>A coder job that is refused: nothing runs, and this is the answer.</summary>
        public sealed record Closed(CheckResult Result) : Gate;
    }

    /// <summary>
    /// The preflight, once before the job: a coder job (one whose request names a <see cref="BoundaryJob"/>) runs only inside the boundary, and a runner with none refuses it — there is no road for a coder job that skips the OS boundary.
    /// </summary>
    private async Task<Gate> AdmitAsync(IReadOnlyList<PlannedStep> plan, CheckRequest request, CancellationToken ct)
    {
        if (request.Job is not { } job)
        {
            return new Gate.Unbounded();
        }

        if (boundary is null)
        {
            return new Gate.Closed(new CheckResult(CheckState.BoundaryRefused, [], "this check runner has no OS boundary, and a coder job is never run without one; nothing was run"));
        }

        var admission = await boundary.AdmitAsync(job, [.. plan.Select(step => NetworkOf(step, job).Mode).Distinct()], ct);

        return admission switch
        {
            BoundaryAdmission.Admitted admitted => new Gate.Bounded(admitted),
            BoundaryAdmission.Refused refused => new Gate.Closed(new CheckResult(CheckState.BoundaryRefused, [], refused.Sentence) { Boundary = refused.Record }),
            _ => throw new InvalidOperationException("a boundary admission is admitted or refused"),
        };
    }

    /// <summary>The network state of a step: the table's for a detected or defaulted step, the job's own (E2: restore and build on the host, test with a loopback device only) for a step of the file. The reason travels in the step's record either way.</summary>
    private static NetworkChoice NetworkOf(PlannedStep step, BoundaryJob job) =>
        step.Network is { } own
            ? own.Mode == DefaultNetwork.Host ? NetworkChoice.OnHost(own.Reason) : NetworkChoice.None(own.Reason)
            : job.NetworkOf(step.Step);

    private async Task<StepRecord> RunStepAsync(PlannedStep step, int number, CheckRequest request, CancellationToken ct)
    {
        var log = LogPath(step, number, request);
        var clock = Stopwatch.StartNew();
        var (folder, problem) = WorkingFolderOf(request.Tree, step.WorkingFolder);
        if (problem.Length > 0)
        {
            return await RefusedStepAsync(step, number, log, problem, clock);
        }

        var launch = new ProcessRequest(Executable(step.Argv[0], request.Tree), [.. step.Argv.Skip(1)], folder)
        {
            Timeout = step.Timeout,
            InheritsEnvironment = false,
            Passthrough = ProcessEnvironment.Minimal,
            Environment = step.Switches,
            MaxOutputChars = CheckLimits.LogBytes,
        };

        ProcessResult result;
        try
        {
            result = await launcher.RunAsync(launch, ct);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
        {
            // A step that cannot start is a failed step with its reason in the log, not a fault of the run: the plan named a program this machine does not have.
            var empty = new ProcessResult(StepRecord.NotStartedExitCode, string.Empty, string.Empty, TimedOut: false);
            await CheckLog.WriteAsync(log, empty, $"[coai: the process could not be started: {e.Message}]\n");

            return Record(step, number, step.Argv, Started: false, StepRecord.NotStartedExitCode, TimedOut: false, clock.Elapsed, log, LogCut: false);
        }

        ct.ThrowIfCancellationRequested();
        var trailer = result.TimedOut ? $"\n[coai: the step ran over its timeout of {step.Timeout} and its whole process tree was killed]\n" : string.Empty;
        var cut = await CheckLog.WriteAsync(log, result, trailer);

        return Record(step, number, step.Argv, Started: true, result.ExitCode, result.TimedOut, clock.Elapsed, log, cut);
    }

    /// <summary>
    /// One step of a coder job, in its own named unit. The log, the exit code and the timeout read as they do for an unbounded step; the record adds the boundary, its properties and the network state. A step whose unit could not be stopped
    /// never passes, whatever it exited with: a process of it may still be running. <b>A step whose program is not in the unit</b> (no <c>node</c>, no <c>dotnet</c>) is a FAILED step whose log names the program (<c>env</c> says so and exits 127), never a skipped one.
    /// </summary>
    private async Task<StepRecord> RunBoundedStepAsync(PlannedStep step, int number, CheckRequest request, BoundaryAdmission.Admitted admitted, CancellationToken ct)
    {
        var job = request.Job!;
        var log = LogPath(step, number, request);
        var clock = Stopwatch.StartNew();
        var (folder, problem) = WorkingFolderOf(job.Tree.Root, step.WorkingFolder);
        if (problem.Length > 0)
        {
            return await RefusedStepAsync(step, number, log, problem, clock);
        }

        var started = await StartBoundedAsync(job, admitted, NetworkOf(step, job), number, [Executable(step.Argv[0], job.Tree.Root), .. step.Argv.Skip(1)], step, folder, ct);
        if (started.Run is not { } run)
        {
            var empty = new ProcessResult(StepRecord.NotStartedExitCode, string.Empty, string.Empty, TimedOut: false);
            await CheckLog.WriteAsync(log, empty, $"[coai: the process could not be started: {started.Problem}]\n");

            return Record(step, number, step.Argv, Started: false, StepRecord.NotStartedExitCode, TimedOut: false, clock.Elapsed, log, LogCut: false);
        }

        ct.ThrowIfCancellationRequested();
        var cut = await CheckLog.WriteAsync(log, run.Result, Trailer(run, step));
        var survived = !run.Gone;
        var exit = survived ? StepRecord.UnitSurvivedExitCode : run.Result.ExitCode;

        return Record(step, number, step.Argv, Started: run.Result.ExitCode != StepRecord.NotStartedExitCode, exit, run.Result.TimedOut, clock.Elapsed, log, cut) with { Boundary = run.Record };
    }

    private static StepRecord Record(PlannedStep step, int number, IReadOnlyList<string> argv, bool Started, int exit, bool TimedOut, TimeSpan duration, string log, bool LogCut) =>
        new(step.Project, step.Step, argv, Started, exit, TimedOut, duration, log, LogCut) { Adapter = step.Adapter, WorkingFolder = step.WorkingFolder, Number = number };

    private static async Task<StepRecord> RefusedStepAsync(PlannedStep step, int number, string log, string problem, Stopwatch clock)
    {
        var empty = new ProcessResult(StepRecord.NotStartedExitCode, string.Empty, string.Empty, TimedOut: false);
        await CheckLog.WriteAsync(log, empty, $"[coai: the step was refused before a unit was described: {problem}]\n");

        return Record(step, number, step.Argv, Started: false, StepRecord.NotStartedExitCode, TimedOut: false, clock.Elapsed, log, LogCut: false);
    }

    private sealed record StartedStep(BoundaryRun? Run, string Problem);

    private async Task<StartedStep> StartBoundedAsync(BoundaryJob job, BoundaryAdmission.Admitted admitted, NetworkChoice network, int number, IReadOnlyList<string> command, PlannedStep step, string workingFolder, CancellationToken ct)
    {
        try
        {
            return new StartedStep(await boundary!.RunAsync(admitted, job, network, $"s{number}", command, step.Switches, step.Timeout, CheckLimits.LogBytes, ct, workingFolder), string.Empty);
        }
        catch (ArgumentException e)
        {
            // A program name the unit's env cannot carry (an '=' or a leading '-'): a failed step with its reason in the log, as a program that is not there.
            return new StartedStep(null, e.Message);
        }
    }

    /// <summary>
    /// The folder a step starts in, as a real path inside the tree: <c>.</c> is the tree; any other folder is a relative path with no <c>..</c>, resolved root-down by the product's one link resolver, which must find it at the very place it was spelled — a link anywhere on the way, or a folder that is a
    /// file or is not there, is a problem (a sentence), and the step is refused before a unit is described.
    /// </summary>
    internal static (string Path, string Problem) WorkingFolderOf(string tree, string folder)
    {
        if (folder is "" or ".")
        {
            return (tree, string.Empty);
        }

        if (!Core.RepoPaths.IsRelative(folder) || folder.Split('/').Any(segment => segment == ".."))
        {
            return (string.Empty, $"the working folder '{Shown(folder)}' is not a relative folder inside the repository");
        }

        var root = Files.RealPath.Resolve(tree);
        if (!root.Ok)
        {
            return (string.Empty, "the worktree could not be resolved to a real path");
        }

        var wanted = Path.Combine(root.Path, folder.Replace('/', Path.DirectorySeparatorChar));
        var below = Files.RealPath.ResolveBelow(root.Path, wanted, StringComparison.Ordinal);
        var real = below.Ok && string.Equals(Path.TrimEndingDirectorySeparator(below.Path), Path.TrimEndingDirectorySeparator(wanted), StringComparison.Ordinal) && Files.RealPath.Lstat(wanted).Kind == Files.EntryKind.Directory;

        return real ? (wanted, string.Empty) : (string.Empty, $"the working folder '{Shown(folder)}' is not a real folder inside the worktree (a link on the way, a file, or not there)");
    }

    private static string Shown(string folder) => folder.Length > 120 ? folder[..120] + "..." : Core.Toolchain.PathSpelling.IsSafe(folder) ? folder : "a path with unusual characters";

    private static string LogPath(PlannedStep step, int number, CheckRequest request) =>
        Path.Combine(request.LogFolder, $"{number:00}-{Slug(step.Project)}-{step.Step.Wire()}.log");

    private static string Trailer(BoundaryRun run, PlannedStep step) =>
        (run.Result.TimedOut ? $"\n[coai: the step ran over its timeout of {step.Timeout} and its unit {run.Unit} was stopped]\n" : string.Empty)
        + (run.Gone ? string.Empty : $"\n[coai: the unit {run.Unit} could not be stopped; a process of the step may still be running, so the step does not pass]\n");

    /// <summary>A program named with a folder is relative to the repository root, the way the file writes it; a bare name is looked up on PATH by the launcher.</summary>
    private static string Executable(string first, string tree) =>
        first.Contains('/') || first.Contains(Path.DirectorySeparatorChar) ? Path.GetFullPath(Path.Combine(tree, first)) : first;

    /// <summary>A project path as a file-name part: letters and digits, everything else a dash; the repository root is <c>root</c>.</summary>
    private static string Slug(string path)
    {
        var slug = new string([.. path.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');

        return slug.Length == 0 ? "root" : slug;
    }

    private static string Describe(StepRecord record)
    {
        var what = $"the {record.Step.Wire()} step of {Shown(record.Project)}";
        if (!record.Started)
        {
            return $"{what} could not be started ({Shown(record.Argv[0])})";
        }

        return record.ExitCode == StepRecord.UnitSurvivedExitCode ? $"{what} left a process running that could not be stopped"
            : record.TimedOut ? $"{what} ran over its timeout and was stopped" : $"{what} exited {record.ExitCode}";
    }
}

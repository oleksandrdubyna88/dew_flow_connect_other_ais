namespace CoaiMcp.Core.Toolchain;

/// <summary>Where a run of the checks ended. Only <see cref="Green"/> is green: a repository with nothing to check, or with a file nobody can read, has not passed anything.</summary>
public enum CheckState
{
    /// <summary>No toolchain file, or a file whose projects name no command to run: <c>not_configured</c>. Never green.</summary>
    NotConfigured,

    /// <summary>The file exists and is wrong, or could not be read at the commit it was asked for: <c>toolchain_refused</c>. Never green; nothing was run.</summary>
    ToolchainRefused,

    /// <summary>A step ended with a non-zero exit code, ran over its timeout or could not start: <c>failed</c>. The steps after it were not run.</summary>
    Failed,

    /// <summary>A coder job was refused before anything ran because its OS boundary did not hold or could not be made: <c>boundary_refused</c>. Never green; nothing was run.</summary>
    BoundaryRefused,

    /// <summary>Every step ran and every one exited 0: <c>green</c>.</summary>
    Green,
}

/// <summary>The words the state has on the wire and in a notice.</summary>
public static class CheckStates
{
    public static string Wire(this CheckState state) => state switch
    {
        CheckState.NotConfigured => "not_configured",
        CheckState.ToolchainRefused => "toolchain_refused",
        CheckState.BoundaryRefused => "boundary_refused",
        CheckState.Failed => "failed",
        _ => "green",
    };
}

/// <summary>Which step of a round a record is about: the names that run in E4 are restore, build, typecheck and test.</summary>
public enum CheckStep
{
    Build,
    Test,
    Restore,
    Typecheck,
}

/// <summary>The words of a step on the wire and in a log name.</summary>
public static class CheckSteps
{
    public static string Wire(this CheckStep step) => step switch
    {
        CheckStep.Restore => "restore",
        CheckStep.Build => "build",
        CheckStep.Typecheck => "typecheck",
        _ => "test",
    };
}

/// <summary>
/// What one step did, written down whole: the argv it was started with (after <c>{jobs}</c> was replaced), how it ended, how long it took and where its output is.
/// </summary>
/// <param name="Project">The project's path in the toolchain file.</param>
/// <param name="Step">Restore, build, typecheck or test.</param>
/// <param name="Argv">What was started, executable first. Never contains a secret: the reader refuses one.</param>
/// <param name="Started">False when the process could not be started (no such file, no right to run it); <see cref="ExitCode"/> is then <see cref="NotStartedExitCode"/>.</param>
/// <param name="ExitCode">The exit code; <see cref="NotStartedExitCode"/> for a step that never started; for a step stopped at its timeout, what the killed process left.</param>
/// <param name="TimedOut">The step ran over its timeout and its whole process tree was killed.</param>
/// <param name="Duration">Wall time from the start to the end of the process tree.</param>
/// <param name="LogPath">The file holding the step's stdout, then its stderr; at most <see cref="CheckLimits.LogBytes"/> bytes of output.</param>
/// <param name="LogCut">The output was longer than the log and was cut.</param>
public sealed record StepRecord(
    string Project,
    CheckStep Step,
    IReadOnlyList<string> Argv,
    bool Started,
    int ExitCode,
    bool TimedOut,
    TimeSpan Duration,
    string LogPath,
    bool LogCut)
{
    /// <summary>The exit code recorded for a process that never started — no real exit code is negative on every platform.</summary>
    public const int NotStartedExitCode = -1;

    /// <summary>The exit code recorded for a step whose unit could not be stopped when the step ended: a process of the step may still be running, so the step never passes.</summary>
    public const int UnitSurvivedExitCode = -2;

    /// <summary>The step's number in its run, from 1, as the code that planned the run assigned it — the one the log's file name, the step's unit (<c>s&lt;n&gt;</c>) and its record carry. 0 for a record that was not made by the check runner.</summary>
    public int Number { get; init; }

    /// <summary>What the step ran inside: the boundary, its properties and the network state. <see cref="BoundaryRecord.None"/> for a run that was not a coder job's.</summary>
    public BoundaryRecord Boundary { get; init; } = BoundaryRecord.None;

    /// <summary>The adapter of the project the step belongs to (<c>dotnet</c>, <c>node</c>…): the failure digest chooses its reader by it (<see cref="FailureDigest"/>). Empty when the record was not made by the check runner.</summary>
    public string Adapter { get; init; } = string.Empty;

    /// <summary>The folder the step started in, relative to the repository root; <c>.</c> is the root (a step of the file always runs from it).</summary>
    public string WorkingFolder { get; init; } = ".";

    /// <summary>Whether the step passed: it started, was not stopped and exited 0.</summary>
    public bool Passed => Started && !TimedOut && ExitCode == 0;
}

/// <summary>What a repository's own toolchain file did between a job's base commit and its worktree.</summary>
public enum ToolchainChange
{
    /// <summary>The same text in both (line endings aside).</summary>
    Unchanged,

    /// <summary>The worktree's file differs from the base's.</summary>
    Changed,

    /// <summary>The base had no file and the worktree has one.</summary>
    Added,

    /// <summary>The base had a file and the worktree has none.</summary>
    Removed,
}

/// <summary>The fixed limits of a check run.</summary>
public static class CheckLimits
{
    /// <summary>A step's log holds at most this many bytes of the process's output: 20 MiB.</summary>
    public const int LogBytes = 20 * 1024 * 1024;
}

/// <summary>
/// The answer of a run of the checks: where it ended, one record per step that ran, a sentence for a person, and — for a coder job — what the coder did to the toolchain file.
/// </summary>
/// <param name="State">Where the run ended.</param>
/// <param name="Steps">The steps that ran, in order; empty when nothing ran.</param>
/// <param name="Note">One sentence: why nothing ran, which step failed, or that everything passed.</param>
/// <param name="ToolchainChange">What the worktree did to <c>.coai/toolchain.yaml</c> against the base — <see cref="CoaiMcp.Core.Toolchain.ToolchainChange.Unchanged"/> when the run was not asked to compare.</param>
public sealed record CheckResult(CheckState State, IReadOnlyList<StepRecord> Steps, string Note, ToolchainChange ToolchainChange = ToolchainChange.Unchanged)
{
    /// <summary>What the run was bounded by. For a refused coder job this carries the preflight rows that said no; for a run that happened, each step's own record is the one to read.</summary>
    public BoundaryRecord Boundary { get; init; } = BoundaryRecord.None;

    /// <summary>Where the checks came from and what they were (E4 s3 t3): the source, the projects with their facts and steps, the notes, and what differs between the base and the head. Null for a run that was given a toolchain FILE and no resolution (the recorded run on a clean clone).</summary>
    public ResolvedToolchain? Toolchain { get; init; }

    /// <summary>What level one of the selection decided for the round: which projects ran and what was left out, with the reason (E4 s3 t6). Null when no selection was made (a file that states every command, a run with no coder job).</summary>
    public AffectedSelection? Selection { get; init; }

    /// <summary>Only a run that ran every step and saw every one pass.</summary>
    public bool Green => State == CheckState.Green;

    /// <summary>The first step that did not pass, if the run failed.</summary>
    public StepRecord? FailedStep => Steps.FirstOrDefault(step => !step.Passed);
}

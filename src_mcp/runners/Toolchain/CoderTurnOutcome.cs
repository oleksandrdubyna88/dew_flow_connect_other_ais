using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Toolchain;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// What coai knows about the MAIN checkout across one coder launch: two snapshots of it, and what differs. EVIDENCE ONLY (<c>v2/architecture/server.md</c> § 16, decision 8): the snapshots cannot tell the coder's write from a person's edit, and the
/// coder cannot write the checkout at all (the OS layer, <see cref="CoderBoundary"/>) — so a change here is recorded for a human to read and never fails the job, unlike the tree of a read-only role.
/// </summary>
/// <param name="Taken">False when a snapshot could not be taken (git did not answer); the note says why, and the job is not affected.</param>
/// <param name="Changes">The paths that differ between the snapshot before the launch and the one after, each with its verb. Empty: nothing observable changed.</param>
/// <param name="Note">One sentence for a reader of the result.</param>
public sealed record CoderMainEvidence(bool Taken, IReadOnlyList<TreeChange> Changes, string Note)
{
    /// <summary>Whether the two snapshots differ.</summary>
    public bool Changed => Changes.Count > 0;

    /// <summary>No snapshot was asked for.</summary>
    public static CoderMainEvidence None { get; } = new(false, [], "the main checkout was not snapshotted");

    /// <summary>The evidence from the two snapshots; a missing one means the evidence was not taken.</summary>
    internal static CoderMainEvidence Of(FilesystemSnapshot? before, FilesystemSnapshot? after, string whyNot)
    {
        if (before is null || after is null)
        {
            return new CoderMainEvidence(false, [], $"the main checkout could not be snapshotted, so there is no evidence about it ({whyNot}); the job is not affected");
        }

        var changes = FilesystemSnapshot.Compare(before, after);

        return changes.Count == 0
            ? new CoderMainEvidence(true, [], "the main checkout looked the same before and after the coder ran")
            : new CoderMainEvidence(true, changes, "evidence only, the job did not fail on it: the main checkout changed while the coder ran, and two snapshots cannot say who wrote (the coder cannot, the OS layer holds it read-only; a person's edit can): "
                + string.Join("; ", changes.Select(change => $"{change.Path} ({change.What})")));
    }
}

/// <summary>What one coder launch came to. Only <see cref="Finished"/> is green; every other case says why in a sentence, and none of them publishes anything.</summary>
public abstract record CoderTurnOutcome
{
    /// <summary>The coder process ended with exit 0 inside its unit, the unit is gone, and the OS refused nothing the coder said.</summary>
    public sealed record Finished(BoundaryRun Run, CoderMainEvidence Evidence) : CoderTurnOutcome;

    /// <summary>The coder did not finish: a non-zero exit, the deadline or a cancel.</summary>
    public sealed record Failed(BoundaryRun Run, string Sentence, CoderMainEvidence Evidence) : CoderTurnOutcome;

    /// <summary>
    /// The OS layer REFUSED a write the coder made — a main checkout, a folder beside the worktree, the <c>.git</c> pointer — and the coder said so. <paramref name="Lines"/> are the lines it said, each naming the path.
    /// Never green, whatever the exit code: a coder that was stopped from writing did not do what it set out to do.
    /// </summary>
    public sealed record WriteStopped(BoundaryRun Run, IReadOnlyList<string> Lines, string Sentence, CoderMainEvidence Evidence) : CoderTurnOutcome;

    /// <summary>
    /// The coder's unit could NOT be stopped (<see cref="BoundaryRun.Gone"/> is false), whatever the process's own result was — an exit 0, a crash, a deadline, a cancel: a process of it may still be alive with the worktree and the coder's home writable.
    /// This is never a model that crashed, so there is no next attempt and no failover: the job ends (the security verify, M7).
    /// </summary>
    public sealed record UnitNotStopped(BoundaryRun Run, string Sentence, CoderMainEvidence Evidence) : CoderTurnOutcome;

    /// <summary>The launch was refused BEFORE any process of the coder started: no boundary here, a preflight row that said no, a launch for another worktree. <paramref name="Record"/> carries the rows.</summary>
    public sealed record Refused(string Sentence, BoundaryRecord Record) : CoderTurnOutcome
    {
        /// <summary>True when the preflight refused ONLY because every red row says the model cannot be used here now (a tool list that could not be read, <see cref="PreflightRow.Unavailable"/>): the entry fails over. With any boundary row red too, false: the boundary row wins.</summary>
        public bool ModelUnavailable => Record.Preflight.Any(row => !row.Held) && Record.Preflight.Where(row => !row.Held).All(row => row.Unavailable);

        /// <summary>The names outside the known set of tools that a red row carries, as a person may read them (<see cref="PreflightRow.Tools"/>); empty when no row carries any.</summary>
        public string UnknownTools => Record.Preflight.FirstOrDefault(row => !row.Held && row.Tools.Length > 0)?.Tools ?? string.Empty;

        /// <summary>Why the notice <c>coder-tool-unknown</c> is raised for this refusal (<see cref="PreflightRow.ToolReason"/>), or empty when no red row is about the tool inventory.</summary>
        public string ToolReason => Record.Preflight.FirstOrDefault(row => !row.Held && row.ToolReason.Length > 0)?.ToolReason ?? string.Empty;
    }

    /// <summary>What the launch was bounded by and what the preflight said: the record of the unit it ran in, or the one of the refusal. Null for none.</summary>
    public BoundaryRecord? Boundary => this switch
    {
        Finished finished => finished.Run.Record,
        Failed failed => failed.Run.Record,
        WriteStopped stopped => stopped.Run.Record,
        UnitNotStopped notStopped => notStopped.Run.Record,
        Refused refused => refused.Record,
        _ => null,
    };

    /// <summary>Whether a unit of the coder was started: false for a launch the preflight or the job's checks refused first.</summary>
    public bool UnitStarted => this is not Refused;

    /// <summary>True only when the coder ran to its end inside the boundary and was not stopped.</summary>
    public bool Green => this is Finished;

    private CoderTurnOutcome() { }
}

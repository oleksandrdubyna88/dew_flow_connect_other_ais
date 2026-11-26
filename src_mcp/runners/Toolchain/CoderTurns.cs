using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// One launch of the claude coder, start to end, the one road (epoch 3 E2 s2 t5): the launch is checked against the job, the boundary is asked (<see cref="CoderBoundary.AdmitAsync"/>) and the job REFUSED before any process of the coder
/// starts when it does not hold, the main checkout is snapshotted before and after as evidence, the coder runs inside its OS layer, and what it did is named.
/// </summary>
/// <remarks>
/// <para><b>The snapshot is evidence, never a verdict</b> (decision 8). A person editing the main checkout during the launch does not fail it (<see cref="CoderMainEvidence"/>): the coder cannot write there, so a change is somebody else's, and the tree of
/// a read-only role is the case where a change DOES fail (the consultant's invariant). This class has no code path from a snapshot difference to a failure.</para>
/// <para><b>A refused write is named.</b> The unit makes the OS answer a write outside the worktree with EROFS; the coder's own stderr carries that line, with the path, and it is quoted back (<see cref="StoppedLines"/>). A coder whose stderr does not say
/// it (the real CLI reports a tool's failure inside its transcript, not on stderr — not measured here) is not seen as stopped by this; the checkout is still unwritten, because the OS refused.</para>
/// </remarks>
public sealed class CoderTurns(CoderBoundary boundary, FilesystemInvariant invariant)
{
    private const int MostLines = 8;

    private const int MostCharacters = 300;

    /// <summary>The words of the OS's answer to a write into a read-only bind (<c>EROFS</c>), as C, node and .NET each spell it.</summary>
    private static readonly string[] ReadOnlySigns = ["read-only file system", "EROFS"];

    /// <summary>The boundary of the checks of a coder job on this machine (<see cref="CoderBoundary.ForChecks"/>): what runs the build and the test after a turn is bounded by what bounded the turn.</summary>
    public CheckBoundary ChecksBoundary() => boundary.ForChecks();

    /// <summary>Launches the coder for <paramref name="job"/> and reports what came of it.</summary>
    /// <param name="job">The job: the worktree the coder stands in, the main checkout it must not write, the folders of its unit.</param>
    /// <param name="request">The launch as <c>ClaudeConsultant.Build</c> made it for a <c>LaunchConfinement.Coder</c>.</param>
    /// <param name="part">The unit's part name — <c>c1</c>, <c>c2</c> — unique within the job.</param>
    /// <param name="ct">The caller's cancel.</param>
    public async Task<CoderTurnOutcome> RunAsync(BoundaryJob job, ProcessRequest request, string part, CancellationToken ct = default)
    {
        if (WrongWorktree(boundary.Vendor.WorkingDirectory(job), job, request) is { Length: > 0 } wrong)
        {
            return new CoderTurnOutcome.Refused(wrong, new BoundaryRecord(BoundaryRecord.UnitBoundary, [], "host", CoderBoundary.NetworkReason, []));
        }

        var admission = await boundary.AdmitAsync(job, request.Executable, ct, CoderBoundary.CarriesKey(request));
        if (admission is BoundaryAdmission.Refused refused)
        {
            return new CoderTurnOutcome.Refused(refused.Sentence, refused.Record);
        }

        var before = await SnapshotAsync(job.Tree.MainCheckout, ct);
        var run = await boundary.RunAsync((BoundaryAdmission.Admitted)admission, job, request, part, ct);
        var after = await SnapshotAsync(job.Tree.MainCheckout, ct);
        var evidence = CoderMainEvidence.Of(before.Snapshot, after.Snapshot, before.Why.Length > 0 ? before.Why : after.Why);

        return Classify(run, evidence);
    }

    /// <summary>The sentence that says the launch does not stand where the vendor's unit starts (the job's worktree for claude; <see cref="ICoderVendor.WorkingDirectory"/>), or empty. The unit starts there whatever the request says, so a request for another folder is refused rather than quietly moved.</summary>
    private static string WrongWorktree(string expected, BoundaryJob job, ProcessRequest request)
    {
        var asked = RealPath.Resolve(request.WorkingDirectory);

        return asked.Ok && string.Equals(asked.Path, expected, StringComparison.Ordinal)
            ? string.Empty
            : expected == job.Tree.Root
                ? $"The coder launch stands in '{request.WorkingDirectory}', which is not the worktree of job {job.JobId} ({job.Tree.Root}); it was not started."
                : $"The coder launch stands in '{request.WorkingDirectory}', which is not where the coder of job {job.JobId} starts ({expected}); it was not started.";
    }

    private sealed record Snapped(FilesystemSnapshot? Snapshot, string Why);

    /// <summary>A snapshot of the main checkout, or why there is none. Evidence is best effort and never the reason a job fails; only a cancel passes through.</summary>
    private async Task<Snapped> SnapshotAsync(string main, CancellationToken ct)
    {
        try
        {
            return new Snapped(await invariant.SnapshotAsync(main, ct), string.Empty);
        }
        catch (ContextException e)
        {
            return new Snapped(null, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Snapped(null, e.Message);
        }
    }

    private static CoderTurnOutcome Classify(BoundaryRun run, CoderMainEvidence evidence)
    {
        var lines = StoppedLines(run.Result.StdErr);
        if (lines.Count > 0)
        {
            return new CoderTurnOutcome.WriteStopped(run, lines, $"The OS layer refused a write of the coder, so this launch is not a result: {string.Join(" | ", lines)}", evidence);
        }

        var why = run.Result switch
        {
            { Cancelled: true } => "the coder launch was cancelled",
            { TimedOut: true } => "the coder launch ran out of time and was stopped",
            { ExitCode: not 0 } result => $"the coder exited {result.ExitCode}",
            _ => string.Empty,
        };

        return why.Length > 0
            ? new CoderTurnOutcome.Failed(run, why, evidence)
            : !run.Gone
                ? new CoderTurnOutcome.Failed(run, $"the coder's unit {run.Unit} could not be stopped; its result is not trusted", evidence)
                : new CoderTurnOutcome.Finished(run, evidence);
    }

    /// <summary>The lines of the coder's stderr that carry the OS's refusal of a write, each cut to a bounded length and at most a few of them — each names the path it was refused for.</summary>
    internal static IReadOnlyList<string> StoppedLines(string stderr) =>
    [
        .. stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => ReadOnlySigns.Any(sign => line.Contains(sign, StringComparison.OrdinalIgnoreCase)))
            .Select(line => line.Length <= MostCharacters ? line : line[..MostCharacters] + "…")
            .Distinct(StringComparer.Ordinal)
            .Take(MostLines),
    ];
}

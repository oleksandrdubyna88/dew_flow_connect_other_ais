namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The run summary of a test step, apart from the bounded digest (E4 s4 t5; SPEC C, read by the re-run of section F and never the digest): the tool's own counts, the name of EVERY failure (not cut at the 40 items of the digest; a hard cap of
/// <see cref="MaxNames"/>), the names of the passed tests when the tool prints them, and whether the list is <see cref="Complete"/>.
/// </summary>
/// <param name="Tool">The reader that made it: <c>vstest</c>, <c>mtp</c>, <c>jest</c>, <c>vitest</c> or <c>node-test</c>.</param>
/// <param name="SummaryFound">The tool's own summary line was found.</param>
/// <param name="Total">The tool's total; 0 when no summary line was found.</param>
/// <param name="Failed">The tool's failed count.</param>
/// <param name="Passed">The tool's passed count.</param>
/// <param name="Failures">The name of every failure the reader could name, in output order.</param>
/// <param name="PassedNames">The names of the passed tests, when the tool printed them (a detailed run).</param>
/// <param name="Complete">True only when the summary line was found, <paramref name="Failed"/> equals the number of named failures, no failure is one the reader cannot name (a suite that failed to run), the names are within the cap and the step's log was not cut.</param>
public sealed record RunSummary(string Tool, bool SummaryFound, int Total, int Failed, int Passed, IReadOnlyList<string> Failures, IReadOnlyList<string> PassedNames, bool Complete)
{
    /// <summary>The most names kept of failures (and of passed tests): above it the summary says it is not complete.</summary>
    public const int MaxNames = 2000;

    /// <summary>Builds a summary and decides <see cref="Complete"/>.</summary>
    /// <param name="logCut">The step's log was cut at its limit.</param>
    /// <param name="unnamed">A failure the reader sees but cannot name (a test suite that failed to run, a header it could not parse): never complete.</param>
    internal static RunSummary Make(string tool, bool summaryFound, int total, int failed, int passed, IReadOnlyList<string> failures, IReadOnlyList<string> passedNames, bool logCut, bool unnamed)
    {
        var withinCap = failures.Count <= MaxNames;

        return new RunSummary(tool, summaryFound, total, failed, passed, [.. failures.Take(MaxNames)], [.. passedNames.Take(MaxNames)],
            summaryFound && failed == failures.Count && withinCap && !logCut && !unnamed);
    }
}

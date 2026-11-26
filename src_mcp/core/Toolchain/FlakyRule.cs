namespace CoaiMcp.Core.Toolchain;

/// <summary>How the re-run goes: <c>narrowed</c> (the file's <c>testFor</c> on the failed tests' files), <c>whole</c> (the whole step again) or <c>none</c>.</summary>
public enum RerunMode
{
    None,
    Narrowed,
    Whole,
}

/// <summary>A failed test as the digest names it: the name and the file its failure names (empty when the output named none).</summary>
public sealed record FailedTest(string Name, string File);

/// <summary>Everything the rule reads: the step's record, its run summary and digest names, what the job changed, and what the job already excused. A pure value.</summary>
/// <param name="Step">The step that failed.</param>
/// <param name="Scope"><c>full</c> or <c>selected</c>: only a failed FULL test step is re-run.</param>
/// <param name="Started">The process started.</param>
/// <param name="TimedOut">The step ran over its time.</param>
/// <param name="LeftUnit">The step's unit could not be stopped.</param>
/// <param name="FirstDuration">How long the first run took.</param>
/// <param name="Summary">The run summary of the first run (s4 t5), or null.</param>
/// <param name="Failed">The failed tests with the file each failure names.</param>
/// <param name="HasTestFor">The project's file names <c>testFor</c> and <c>testFiles</c>.</param>
/// <param name="JobChangedFiles">The files the job added or changed, base to head.</param>
/// <param name="ExcusedInJob">How many tests this job has excused already.</param>
/// <param name="AlreadyExcused">Their names.</param>
/// <param name="Enabled"><c>COAI_FLAKY_RERUNS</c> is 1.</param>
public sealed record RerunInput(CheckStep Step, string Scope, bool Started, bool TimedOut, bool LeftUnit, TimeSpan FirstDuration, RunSummary? Summary, IReadOnlyList<FailedTest> Failed, bool HasTestFor, IReadOnlySet<string> JobChangedFiles, int ExcusedInJob, IReadOnlySet<string> AlreadyExcused, bool Enabled)
{
    /// <summary>The exit code the first run ended with.</summary>
    public int FirstExit { get; init; }

    /// <summary>The exit codes the tool of the first run ends with when tests FAILED (a row of <c>shared/toolchains.json</c>): only then may a narrowed re-run turn the step green (M2 of the s5 verify).</summary>
    public IReadOnlyList<int> TestsFailedCodes { get; init; } = [];
}

/// <summary>The rule's answer: the mode and, for none, the reason (one sentence for the record).</summary>
public sealed record RerunDecision(RerunMode Mode, string Why, IReadOnlyList<FailedTest> Tests);

/// <summary>What the re-run did.</summary>
/// <param name="Started">The re-run's process started.</param>
/// <param name="TimedOut">It ran over its time.</param>
/// <param name="LeftUnit">Its unit could not be stopped.</param>
/// <param name="ExitCode">Its exit code.</param>
/// <param name="Summary">Its run summary (s4 t5), or null.</param>
public sealed record RerunOutcome(bool Started, bool TimedOut, bool LeftUnit, int ExitCode, RunSummary? Summary);

/// <summary>A test the re-run excused: the proof is <c>named</c> (it is exactly once among the passed tests the re-run names) or <c>totals</c> (a whole re-run with the same total and zero failed).</summary>
public sealed record ExcusedTest(string Name, string File, string Proof);

/// <summary>
/// <b>The rule of the re-run, pure</b> (E4 s5 t4; SPEC F): only from a complete list of failures, never for the job's own tests, and an excuse only with proof that the test ran again and passed — and, since the plan re-review (F0), only when the re-run ITSELF completes with exit 0, no new failure and no execution error.
/// </summary>
public static class FlakyRule
{
    public const int MaxFailed = 5;

    /// <summary>The longest name of a test or a file kept in a record (S3 of the s5 verify).</summary>
    public const int NameLimit = 200;

    /// <summary>A test's name as it is written to the envelope and to the job's file: redacted and bounded, because it is the output's text.</summary>
    public static string SafeName(string name) => CoaiMcp.Core.Notices.Redaction.SafeText(name, NameLimit);

    public const int MaxExcusedPerJob = 2;

    public static readonly TimeSpan WholeLimit = TimeSpan.FromSeconds(300);

    /// <summary>Whether, and how, the failed step may be run once more.</summary>
    public static RerunDecision Decide(RerunInput input)
    {
        if (Never(input) is { Length: > 0 } why)
        {
            return new RerunDecision(RerunMode.None, why, []);
        }

        var summary = input.Summary!;
        var tests = new List<FailedTest>();
        foreach (var name in summary.Failures)
        {
            var test = input.Failed.FirstOrDefault(failed => failed.Name == name || name.EndsWith("> " + failed.Name, StringComparison.Ordinal));
            if (test is null || test.File.Length == 0)
            {
                return new RerunDecision(RerunMode.None, "a failure is named without the file its failure names, so it cannot be shown not to be the job's own", []);
            }

            if (input.JobChangedFiles.Contains(test.File) || input.JobChangedFiles.Any(changed => changed.EndsWith("/" + test.File, StringComparison.Ordinal) || test.File.EndsWith("/" + changed, StringComparison.Ordinal)))
            {
                return new RerunDecision(RerunMode.None, "a failed test is one whose file the job added or changed", []);
            }

            if (input.AlreadyExcused.Contains(SafeName(name)))
            {
                return new RerunDecision(RerunMode.None, "a failed test was already excused once in this job", []);
            }

            tests.Add(test with { Name = name });
        }

        if (input.ExcusedInJob + tests.Count > MaxExcusedPerJob)
        {
            return new RerunDecision(RerunMode.None, "a third test would be excused in one job", []);
        }

        // M2: a narrowed re-run shows that the named tests pass, not that the step would; it may excuse only a step whose first exit code is the tool's "tests failed" code — any other code (a coverage gate, a crash) needs the whole step again.
        var narrowed = input.HasTestFor && input.TestsFailedCodes.Contains(input.FirstExit);
        var mode = narrowed ? RerunMode.Narrowed : input.FirstDuration <= WholeLimit ? RerunMode.Whole : RerunMode.None;

        return new RerunDecision(mode, mode == RerunMode.None ? "the first run took over 300 seconds and the file names no testFor" : string.Empty, mode == RerunMode.None ? [] : tests);
    }

    private static string Never(RerunInput input)
    {
        if (!input.Enabled)
        {
            return "the re-run is switched off (COAI_FLAKY_RERUNS=0)";
        }

        if (input.Step != CheckStep.Test || input.Scope != "full")
        {
            return "only a failed full test step is re-run";
        }

        if (!input.Started || input.TimedOut || input.LeftUnit)
        {
            return "a step that could not start, ran over its time or left a unit is never re-run";
        }

        return input.Summary switch
        {
            null => "no run summary: the failures were not named",
            { Complete: false } => "the run summary is not complete (a failure the reader cannot name, no summary line, a cut log or too many names)",
            { Failures.Count: 0 } => "no failure is named",
            { Failures.Count: > MaxFailed } => $"more than {MaxFailed} tests failed: that is not flakiness",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The tests the re-run excuses — all of them or none. F0: the re-run must itself exit 0 with no new failure and no execution error. Proof: a test is excused only when the re-run SHOWS it ran and passed: its name exactly once among the passed tests the re-run names, or, for a whole re-run of a tool that names no
    /// passed test, a complete summary with the same total as the first run and zero failed. A re-run in which no test ran (a total of 0), a name that matches twice, or a total that differs excuses nothing.
    /// </summary>
    public static IReadOnlyList<ExcusedTest> Excused(RerunDecision decision, RunSummary first, RerunOutcome rerun)
    {
        var summary = rerun.Summary;
        if (decision.Mode == RerunMode.None || !rerun.Started || rerun.TimedOut || rerun.LeftUnit || rerun.ExitCode != 0 || summary is null || !summary.SummaryFound || summary.Failed != 0 || summary.Failures.Count != 0 || summary.Total == 0)
        {
            return [];
        }

        var excused = new List<ExcusedTest>();
        foreach (var test in decision.Tests)
        {
            var named = summary.PassedNames.Count(name => name == test.Name || name.EndsWith("> " + test.Name, StringComparison.Ordinal));
            if (named == 1 && summary.Complete)
            {
                excused.Add(new ExcusedTest(test.Name, test.File, "named"));
            }
            else if (named == 0 && decision.Mode == RerunMode.Whole && summary.Complete && summary.Total == first.Total && summary.PassedNames.Count == 0)
            {
                excused.Add(new ExcusedTest(test.Name, test.File, "totals"));
            }
            else
            {
                return [];
            }
        }

        return excused;
    }
}

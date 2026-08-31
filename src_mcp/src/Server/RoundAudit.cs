using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// The audit trail of one round: who was asked, with what, how long each took, what it consumed,
/// and — when a reviewer failed — the CLI's own last words.
/// </summary>
/// <remarks>
/// <para>Written because the round summary is deliberately one sentence, and one sentence cannot
/// answer "why did codex exit 1". That question was asked twice at a real gate and could not be
/// answered either time: the executor had the stderr, the summary dropped it, and the log held a
/// single line per ROUND. A review gate that cannot say why a reviewer did not review is a gate
/// nobody can trust with a verdict.</para>
/// <para>It rides the existing per-run log file (one file per host run, per the family logging
/// rule) rather than a store of its own: the round lines and the reviewer lines belong in the
/// same chronological trail, and a second sink would be a second thing to find.</para>
/// </remarks>
public sealed class RoundAudit(Serilog.ILogger log, string stage, int number)
{
    private readonly Serilog.ILogger _log = log.ForContext("Stage", stage).ForContext("Round", number);

    /// <summary>What the round is about to ask, before the first CLI starts.</summary>
    public void Opening(IReadOnlyList<ReviewerWork> work, string workingDir, TimeSpan timeout)
    {
        _log.Information(
            "round {Round} {Stage} opening: {Count} reviewer(s) — {Reviewers}; working dir {WorkingDir}, timeout {Timeout}",
            number, stage, work.Count,
            string.Join(", ", work.Select(w => $"{w.Invocation.Provider}/{w.Invocation.Role}")),
            workingDir, Humanised(timeout));

        foreach (var w in work)
        {
            // The full argv at Debug: it is the difference between "codex was asked" and being
            // able to paste the exact command into a terminal and watch it fail the same way.
            _log.Debug(
                "reviewer {Provider}/{Role} argv: {Executable} {Arguments} (prompt {PromptBytes} bytes on stdin)",
                w.Invocation.Provider, w.Invocation.Role.ToString(),
                w.Invocation.Request.Executable,
                string.Join(' ', w.Invocation.Request.Arguments),
                w.Invocation.Request.StdIn.Length);
        }
    }

    /// <summary>One reviewer moved. Failures are logged as WARNINGS with their reason attached.</summary>
    public void Moved(ReviewerProgress progress)
    {
        switch (progress.Status)
        {
            case ReviewerState.Running:
                _log.Information("reviewer {Provider}/{Role} started", progress.Provider, progress.Role.ToString());
                break;

            case ReviewerState.Done when progress.Outcome is ReviewerOutcome.Ok ok:
                _log.Information(
                    "reviewer {Provider}/{Role} answered in {Seconds:0.0}s: {Findings} finding(s), {TokensIn} in / {TokensOut} out tokens{Cost}{Repaired}",
                    progress.Provider, progress.Role.ToString(), progress.Elapsed.TotalSeconds,
                    ok.Review.Findings.Count(), ok.Usage.TokensIn, ok.Usage.TokensOut,
                    ok.Usage.CostUsd is { } usd ? $", ${usd:0.0000}" : string.Empty,
                    ok.Repaired ? " (after one repair)" : string.Empty);
                break;

            case ReviewerState.Failed when progress.Outcome is { } outcome:
                // A reviewer that did not review is the thing worth finding in a log later, so it
                // is a warning even though the round survives it.
                _log.Warning(
                    "reviewer {Provider}/{Role} FAILED after {Seconds:0.0}s: {Reason}",
                    progress.Provider, progress.Role.ToString(), progress.Elapsed.TotalSeconds,
                    ReviewerSummaryFactory.Describe(outcome));
                break;
        }
    }

    /// <summary>The round's own line: verdict, gate, and what the whole fan-out consumed.</summary>
    public void Closing(string verdict, int gatingCount, string reviewers, RoundRecord record)
    {
        _log.Information(
            "round {Round} {Stage} {Verdict}: {Gating} gating finding(s); {Reviewers}; {TokensIn} in / {TokensOut} out tokens{Cost} over {Seconds:0.0}s",
            number, stage, verdict, gatingCount, reviewers,
            record.TokensIn, record.TokensOut,
            record.CostUsd is { } usd ? $", ${usd:0.0000}" : " (no cost reported)",
            (record.CompletedUtc - record.StartedUtc).TotalSeconds);
    }

    /// <summary>
    /// Where a finding points, or nothing at all.
    /// </summary>
    /// <remarks>
    /// A plan-stage finding has no file and no line — a plan is a document, and the reviewer is
    /// judging prose. Printing the empty pair anyway produced <c>finding [Major/Security] :0 —</c>,
    /// which reads as a value that got lost rather than one that was never there.
    /// </remarks>
    private static string Where(Finding f) =>
        f.File.Length == 0 ? string.Empty : $"{f.File}:{f.Line} — ";

    /// <summary>A timeout a person reads, not a quoted TimeSpan.</summary>
    private static string Humanised(TimeSpan span) =>
        span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0} min" : $"{span.TotalSeconds:0}s";

    /// <summary>Every finding the round produced, so a later dispute has the original text.</summary>
    public void Findings(IReadOnlyList<Finding> findings)
    {
        foreach (var f in findings)
        {
            _log.Information(
                "finding [{Severity}/{Category}] {Where}{Title} (from {Providers}){Gating}",
                f.Severity.ToString(), f.Category.ToString(), Where(f), f.Title,
                string.Join('+', f.Providers), f.IsGating ? " [gating]" : string.Empty);
        }
    }
}

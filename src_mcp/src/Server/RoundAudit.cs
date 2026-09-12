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
    /// <param name="excluded">
    /// Reviewers the operator ENABLED for this stage that this round cannot run. Logged here, beside
    /// the ones that were asked, because the two lines this file writes used to contradict each other
    /// in silence: the startup line said <c>codex,gemini,local,remsoftdev-claude enabled</c> and this
    /// one said <c>3 reviewer(s)</c>, eleven seconds apart on 2026-09-07, and nothing named the
    /// fourth or said why. A log that cannot say why a reviewer did not review is the thing this
    /// class was written to stop.
    /// </param>
    public void Opening(
        IReadOnlyList<ReviewerWork> work,
        string workingDir,
        TimeSpan timeout,
        IReadOnlyList<string>? excluded = null)
    {
        _log.Information(
            "round {Round} {Stage} opening: {Count} reviewer(s) — {Reviewers}; working dir {WorkingDir}, timeout {Timeout}",
            number, stage, work.Count,
            // The prompt's SIZE beside its name. The round line above it says what was assembled;
            // this says what each reviewer was handed, and the two are only the same while nothing
            // between them is broken. On 2026-09-08 that distinction had to be reconstructed from a
            // ledger, because nothing anywhere recorded it.
            string.Join(", ", work.Select(Describe)),
            workingDir, Humanised(timeout));

        // A separate line rather than a longer one: this is the exceptional case, and folding it into
        // the sentence above would make every ordinary round pay for it in width.
        if (excluded is { Count: > 0 })
        {
            _log.Warning(
                "round {Round} {Stage} left out {Count} enabled reviewer(s): {Excluded}",
                number, stage, excluded.Count, string.Join("; ", excluded));
        }

        foreach (var w in work)
        {
            // The full argv at Debug: it is the difference between "codex was asked" and being
            // able to paste the exact command into a terminal and watch it fail the same way.
            _log.Debug(
                "reviewer {Provider}/{Role} argv: {Executable} {Arguments} (prompt {PromptBytes} bytes on stdin)",
                w.Invocation.Provider, w.Invocation.Role,
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
                _log.Information("reviewer {Provider}/{Role} started", progress.Provider, progress.Role);
                break;

            case ReviewerState.Done when progress.Outcome is ReviewerOutcome.Ok ok:
                _log.Information(
                    "reviewer {Provider}/{Role} answered in {Seconds:0.0}s: {Findings} finding(s), {TokensIn} in / {TokensOut} out tokens{Cost}{Repaired}{Evidence}",
                    progress.Provider, progress.Role, progress.Elapsed.TotalSeconds,
                    ok.Review.Findings.Count(), ok.Usage.TokensIn, ok.Usage.TokensOut,
                    ok.Usage.CostUsd is { } usd ? $", ${usd:0.0000}" : string.Empty,
                    ok.Repaired ? " (after one repair)" : string.Empty,
                    // Only ever present on a reviewer that found NOTHING, which is the one an
                    // operator reading a silent round is looking for. Named here rather than in the
                    // round's reply: every clean round would carry that sentence, and a sentence on
                    // every clean round is one nobody reads on the round that matters.
                    ok.Evidence.Length > 0 ? $" (its answer was kept at '{ok.Evidence}')" : string.Empty);
                break;

            case ReviewerState.Failed when progress.Outcome is { } outcome:
                // A reviewer that did not review is the thing worth finding in a log later, so it
                // is a warning even though the round survives it.
                _log.Warning(
                    "reviewer {Provider}/{Role} FAILED after {Seconds:0.0}s: {Reason}",
                    progress.Provider, progress.Role, progress.Elapsed.TotalSeconds,
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
    /// <summary>One reviewer in the opening line: what it will run, and how big what it was handed is.</summary>
    /// <remarks>
    /// The size is omitted rather than printed as zero when nobody measured it. `0 bytes` reads as a
    /// claim that this reviewer was sent an empty prompt, and the whole reason the number is here is
    /// to be believed on exactly that question.
    /// </remarks>
    private static string Describe(ReviewerWork w)
    {
        // The model and the effort lead the bracket when there are any, because this line is what a
        // person reads when the panel is closed, and two local runs of one model at different
        // efforts were writing the same sentence (issue #129, raised on its plan round).
        //
        // Each part is CONDITIONAL rather than interpolated empty: `[, promptId, 900 bytes]` is a
        // descriptor with a hole in it, which is what a later reader of these lines misparses.
        var parts = new List<string>(4);
        if (w.Invocation.Model.Length > 0)
        {
            parts.Add(w.Invocation.Model);
        }
        if (w.Invocation.Effort.Length > 0)
        {
            parts.Add($"effort {w.Invocation.Effort}");
        }
        parts.Add(w.Prompt);
        if (w.PromptBytes is { } bytes)
        {
            parts.Add($"{bytes} bytes");
        }

        return $"{w.Invocation.Provider}/{w.Invocation.Role}[{string.Join(", ", parts)}]";
    }

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

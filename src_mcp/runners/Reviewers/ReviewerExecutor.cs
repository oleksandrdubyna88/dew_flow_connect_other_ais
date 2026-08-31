using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The five ways one reviewer ends — exhaustive and closed, so a sixth cannot appear silently.
/// Never a silent zero: a round that ran with four of six reviewers says so by name.
/// </summary>
public abstract record ReviewerOutcome
{
    public sealed record Ok(NormalisedReview Review, bool Repaired) : ReviewerOutcome;

    public sealed record NonZeroExit(int ExitCode, string StdErrTail) : ReviewerOutcome;

    public sealed record TimedOut : ReviewerOutcome;

    public sealed record Unparseable(string Reason) : ReviewerOutcome;

    /// <summary>Distinct from a timeout in the log AND the result — they demand different cures.</summary>
    public sealed record RateLimited : ReviewerOutcome;

    /// <summary>
    /// The process never ran: the CLI is not installed, or the configured path is wrong. Its own
    /// outcome because it is neither a failure of the model nor of the network — found when a
    /// test pointed one provider at a missing binary and the exception took the whole ROUND down
    /// instead of one reviewer.
    /// </summary>
    public sealed record NotStarted(string Reason) : ReviewerOutcome;

    private ReviewerOutcome() { }
}

/// <summary>Recognising a vendor quota answer in a CLI's output. Pure, so it is a table test.</summary>
public static class RateLimit
{
    /// <summary>
    /// The phrases vendors actually use. <c>usage limit</c> is here because Codex says
    /// "You've hit your usage limit" and never the words "rate limit" or "429" — observed in the
    /// first real run, where a quota exhaustion was misreported as a plain non-zero exit and so
    /// was never retried.
    /// </summary>
    private static readonly string[] Phrases = ["429", "rate limit", "usage limit", "quota"];

    public static bool Hit(ProcessResult result) =>
        result.ExitCode != 0 &&
        Phrases.Any(p => Contains(result.StdErr, p) || Contains(result.StdOut, p));

    private static bool Contains(string text, string needle) =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Runs ONE reviewer to a <see cref="ReviewerOutcome"/>: launch → classify the exit → read the
/// answer (codex: the <c>-o</c> file; gemini: stdout through <see cref="GeminiPayload"/>) →
/// parse. An unparseable answer gets exactly one repair launch; a second failure is a named
/// outcome, never a retry loop.
/// </summary>
public sealed class ReviewerExecutor(IProcessLauncher launcher)
{
    private const int StdErrTail = 400;

    public async Task<ReviewerOutcome> RunAsync(
        ReviewerInvocation invocation,
        ReviewerInvocation? repair = null,
        CancellationToken ct = default)
    {
        var (outcome, review) = await RunOnceAsync(invocation, ct);
        if (outcome is not null)
        {
            return outcome;
        }

        if (review is { } parsed)
        {
            return new ReviewerOutcome.Ok(parsed, Repaired: false);
        }

        if (repair is null)
        {
            return new ReviewerOutcome.Unparseable("the answer was not the schema's JSON, and no repair was configured");
        }

        var (repairOutcome, repaired) = await RunOnceAsync(repair, ct);
        return repairOutcome
               ?? (repaired is { } fixedReview
                   ? new ReviewerOutcome.Ok(fixedReview, Repaired: true)
                   : new ReviewerOutcome.Unparseable("still not the schema's JSON after one repair attempt"));
    }

    /// <summary>One launch. A non-null outcome is terminal; a null review means "unparseable".</summary>
    private async Task<(ReviewerOutcome?, NormalisedReview?)> RunOnceAsync(ReviewerInvocation invocation, CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            result = await launcher.RunAsync(invocation.Request, ct);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // One reviewer that cannot start is one reviewer's failure, never the round's.
            return (new ReviewerOutcome.NotStarted($"'{invocation.Request.Executable}' could not be started: {e.Message}"), null);
        }

        if (result.TimedOut)
        {
            return (new ReviewerOutcome.TimedOut(), null);
        }

        if (RateLimit.Hit(result))
        {
            return (new ReviewerOutcome.RateLimited(), null);
        }

        if (result.ExitCode != 0)
        {
            var tail = result.StdErr.Length <= StdErrTail ? result.StdErr : result.StdErr[^StdErrTail..];
            return (new ReviewerOutcome.NonZeroExit(result.ExitCode, tail.Trim()), null);
        }

        return (null, Parse(invocation, result));
    }

    private static NormalisedReview? Parse(ReviewerInvocation invocation, ProcessResult result)
    {
        var raw = invocation.OutputFile.Length > 0
            ? ReadOutputFile(invocation.OutputFile)
            : result.StdOut;
        if (raw is null)
        {
            return null;
        }

        // Gemini answers through its envelope and its habits; codex's -o file is schema-bound
        // already, but the same balanced extraction costs nothing and forgives a stray banner.
        if (GeminiPayload.Extract(raw) is not ExtractOutcome.Payload payload)
        {
            return null;
        }

        return ReviewParser.Parse(payload.Json, invocation.Provider) is ParseOutcome.Success success
            ? success.Review
            : null;
    }

    private static string? ReadOutputFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null; // the CLI exited 0 but wrote nothing — unparseable, by name
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

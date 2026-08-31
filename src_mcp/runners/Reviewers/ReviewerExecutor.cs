using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The five ways one reviewer ends — exhaustive and closed, so a sixth cannot appear silently.
/// Never a silent zero: a round that ran with four of six reviewers says so by name.
/// </summary>
public abstract record ReviewerOutcome
{
    /// <param name="Usage">What the vendor said the run consumed. Zeroes mean it said nothing.</param>
    public sealed record Ok(NormalisedReview Review, bool Repaired, Usage Usage) : ReviewerOutcome
    {
        public Ok(NormalisedReview Review, bool Repaired) : this(Review, Repaired, Usage.None) { }
    }

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

/// <summary>
/// Recognising "the vendor could not serve this right now, try again" in a CLI's output — the one
/// failure worth a second attempt. Pure, so it is a table test.
/// </summary>
public static class RateLimit
{
    /// <summary>
    /// The phrases vendors actually use, all of them observed rather than imagined:
    /// <list type="bullet">
    /// <item>Codex says "You've hit your usage limit" — never "rate limit", never "429", which is
    /// why a quota exhaustion was first misreported as a plain non-zero exit and never retried.</item>
    /// <item>Gemini answers <c>503 UNAVAILABLE</c> "This model is currently experiencing high
    /// demand" — transient by its own description, and so exactly what one retry is for.</item>
    /// </list>
    /// </summary>
    private static readonly string[] Phrases =
        ["429", "rate limit", "usage limit", "quota", "503", "unavailable", "high demand"];

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
        var (outcome, review, usage) = await RunOnceAsync(invocation, ct);
        if (outcome is not null)
        {
            return outcome;
        }

        if (review is { } parsed)
        {
            return new ReviewerOutcome.Ok(parsed, Repaired: false, usage);
        }

        if (repair is null)
        {
            return new ReviewerOutcome.Unparseable("the answer was not the schema's JSON, and no repair was configured");
        }

        var (repairOutcome, repaired, repairUsage) = await RunOnceAsync(repair, ct);
        return repairOutcome
               ?? (repaired is { } fixedReview
                   // Both launches are billed, so both are counted — a repaired reviewer that
                   // reported only its second attempt would under-report every time.
                   ? new ReviewerOutcome.Ok(fixedReview, Repaired: true, usage.Add(repairUsage))
                   : new ReviewerOutcome.Unparseable("still not the schema's JSON after one repair attempt"));
    }

    /// <summary>One launch. A non-null outcome is terminal; a null review means "unparseable".</summary>
    private async Task<(ReviewerOutcome?, NormalisedReview?, Usage)> RunOnceAsync(ReviewerInvocation invocation, CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            result = await launcher.RunAsync(invocation.Request, ct);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // One reviewer that cannot start is one reviewer's failure, never the round's.
            return (new ReviewerOutcome.NotStarted($"'{invocation.Request.Executable}' could not be started: {e.Message}"), null, Usage.None);
        }

        if (result.TimedOut)
        {
            return (new ReviewerOutcome.TimedOut(), null, Usage.None);
        }

        if (RateLimit.Hit(result))
        {
            return (new ReviewerOutcome.RateLimited(), null, Usage.None);
        }

        if (result.ExitCode != 0)
        {
            var tail = result.StdErr.Length <= StdErrTail ? result.StdErr : result.StdErr[^StdErrTail..];
            return (new ReviewerOutcome.NonZeroExit(result.ExitCode, tail.Trim()), null, Usage.None);
        }

        // Both reads go through the vendor's own adapter: where the answer lands and how the run
        // is billed are vendor knowledge, and keeping them here would have made every new vendor
        // an edit to this class.
        var usage = invocation.Adapter?.ReadUsage(invocation, result) ?? UsageParser.Parse(result.StdOut);
        return (null, Parse(invocation, result), usage);
    }

    private static NormalisedReview? Parse(ReviewerInvocation invocation, ProcessResult result)
    {
        var raw = invocation.Adapter is { } adapter
            ? adapter.ReadAnswer(invocation, result)
            : ReviewerOutput.FileOrStdout(invocation, result);
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
}

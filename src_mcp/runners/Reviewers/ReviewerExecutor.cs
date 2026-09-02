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

    /// <param name="Usage">
    /// What the run consumed anyway. An unparseable answer is the one FAILURE whose process
    /// completed and reported its usage, and dropping it under-reported a round by roughly half
    /// whenever a reviewer fell over — measured: two failed reviewers ran 107 and 128 seconds
    /// beside a sibling that cost 210k input tokens, and the round recorded them as free.
    /// </param>
    public sealed record Unparseable(string Reason, Usage Usage) : ReviewerOutcome
    {
        public Unparseable(string Reason) : this(Reason, Usage.None) { }
    }

    /// <summary>
    /// Distinct from a timeout in the log AND the result — they demand different cures.
    /// </summary>
    /// <param name="Reason">
    /// The vendor's own words. Carried because a bare "rate limited" repeats the mistake that a
    /// bare "exit 1" made: a per-minute throttle a retry clears and a DAILY quota that no retry
    /// can clear read identically, and only one of them is worth waiting for.
    /// </param>
    public sealed record RateLimited(string Reason = "") : ReviewerOutcome;

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

    /// <summary>
    /// Whether waiting is pointless: a DAILY allowance, not a per-minute throttle.
    /// </summary>
    /// <remarks>
    /// Measured cost of not asking: gemini answered "You have exhausted your daily quota on this
    /// model", the scheduler waited its backoff and launched a second doomed reviewer, and that
    /// round took 157 seconds instead of 19. The retry exists for "this model is currently
    /// experiencing high demand", which clears in seconds; a daily quota clears at midnight in
    /// someone else's timezone.
    /// </remarks>
    public static bool Hopeless(string reason) =>
        Contains(reason, "daily") || Contains(reason, "exhausted");

    /// <summary>
    /// The line that says WHICH limit was hit, so a person can tell a per-minute throttle from a
    /// daily quota — and so <see cref="Hopeless"/> has something to read.
    /// </summary>
    public static string Reason(ProcessResult result)
    {
        var lines = (result.StdErr + '\n' + result.StdOut)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.FirstOrDefault(l => Phrases.Any(p => Contains(l, p))) ?? string.Empty;
    }

    private static bool Contains(string text, string needle) =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Runs ONE reviewer to a <see cref="ReviewerOutcome"/>: launch → classify the exit → read the
/// answer (codex: the <c>-o</c> file; gemini: stdout through <see cref="GeminiPayload"/>) →
/// parse. An unparseable answer gets exactly one repair launch; a second failure is a named
/// outcome, never a retry loop.
/// </summary>
public sealed class ReviewerExecutor(IProcessLauncher launcher, string? keepUnparseableIn = null)
{
    private const int StdErrTail = 400;

    /// <summary>
    /// Where an answer that would not parse is KEPT, so the next person can read what the vendor
    /// actually said.
    /// </summary>
    /// <remarks>
    /// The raw text is PASSED IN rather than remembered on this instance. One executor serves the
    /// whole fan-out — six reviewers at once — so an instance field would hand a failed reviewer
    /// whichever answer happened to finish last. Both vendors caught that in the round that
    /// reviewed this file.
    /// </remarks>
    /// <remarks>
    /// The first real code round lost a reviewer to "unparseable: still not the schema's JSON
    /// after one repair attempt" and the evidence was gone — the same answer replayed by hand
    /// afterwards succeeded, so the sentence named a symptom nobody could chase. An unparseable
    /// answer is the one case where the raw text is the whole story.
    /// </remarks>
    private string? Keep(ReviewerInvocation invocation, string? raw)
    {
        if (keepUnparseableIn is null || raw is null)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(keepUnparseableIn);
            var file = Path.Combine(
                keepUnparseableIn,
                $"{invocation.Provider}-{invocation.Role}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(file, raw);
            return file;
        }
        catch (IOException)
        {
            return null; // keeping evidence must never be what fails a round
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<ReviewerOutcome> RunAsync(
        ReviewerInvocation invocation,
        ReviewerInvocation? repair = null,
        CancellationToken ct = default)
    {
        var (outcome, review, usage, answer, evidence) = await RunOnceAsync(invocation, ct);
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
            return new ReviewerOutcome.Unparseable(
                Because(Said(answer), "and no repair was configured", Keep(invocation, evidence)), usage);
        }

        var (repairOutcome, repaired, repairUsage, repairAnswer, repairEvidence) = await RunOnceAsync(repair, ct);
        return repairOutcome
               ?? (repaired is { } fixedReview
                   // Both launches are billed, so both are counted — a repaired reviewer that
                   // reported only its second attempt would under-report every time.
                   ? new ReviewerOutcome.Ok(fixedReview, Repaired: true, usage.Add(repairUsage))
                   // BOTH launches are kept, and the first one wins when the repair came back
                   // empty: a vendor whose envelope broke leaves nothing to read, and the
                   // evidence file was landing at zero bytes exactly when it was most needed.
                   : new ReviewerOutcome.Unparseable(
                       Because(Said(repairAnswer ?? answer), "after one repair attempt", Keep(repair, Longer(evidence, repairEvidence))),
                       usage.Add(repairUsage)));
    }

    /// <summary>
    /// Everything the process said, for when what it MEANT to say is missing.
    /// </summary>
    /// <remarks>
    /// Both streams, labelled. An empty answer with an empty evidence file tells nobody anything;
    /// the vendor's own stream carries its status and its error, and that is the whole diagnosis.
    /// </remarks>
    private static string Transcript(ProcessResult result) =>
        $"--- stdout ---\n{result.StdOut}\n--- stderr ---\n{result.StdErr}";

    private static string Because(string what, string when, string? keptAt) =>
        keptAt is null ? $"{what} {when}" : $"{what} {when} (the answer was kept at {keptAt})";

    /// <summary>
    /// What actually went wrong, which is not always what the old sentence claimed.
    /// </summary>
    /// <remarks>
    /// "Still not the schema's JSON" describes a syntax problem. Measured on a real round, the
    /// vendor had returned NOTHING — an empty envelope — and the sentence sent the reader looking
    /// for malformed JSON that did not exist.
    /// </remarks>
    private static string Said(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? "the vendor returned an empty answer"
            : "the answer was not the schema's JSON";

    /// <summary>The launch that actually said something — the repair is often the empty one.</summary>
    private static string? Longer(string? first, string? second) =>
        (second?.Trim().Length ?? 0) >= (first?.Trim().Length ?? 0) ? second : first;

    /// <summary>One launch. A non-null outcome is terminal; a null review means "unparseable".</summary>
    private async Task<(ReviewerOutcome? Outcome, NormalisedReview? Review, Usage Usage, string? Answer, string Evidence)> RunOnceAsync(ReviewerInvocation invocation, CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            // Labelled HERE rather than in each vendor adapter: every reviewer launch passes
            // through this one line, and a label added per adapter is a label the next adapter
            // forgets. Only reviewers are tracked — the git commands around them finish in
            // milliseconds and are never the thing left running for ten hours.
            var request = invocation.Request with
            {
                TrackAs = $"{invocation.Provider}/{invocation.Role}",
            };
            result = await launcher.RunAsync(request, ct);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // One reviewer that cannot start is one reviewer's failure, never the round's.
            return (new ReviewerOutcome.NotStarted($"'{invocation.Request.Executable}' could not be started: {e.Message}"), null, Usage.None, null, string.Empty);
        }

        if (result.TimedOut)
        {
            return (new ReviewerOutcome.TimedOut(), null, Usage.None, null, string.Empty);
        }

        if (RateLimit.Hit(result))
        {
            return (new ReviewerOutcome.RateLimited(RateLimit.Reason(result)), null, Usage.None, null, string.Empty);
        }

        if (result.ExitCode != 0)
        {
            var tail = result.StdErr.Length <= StdErrTail ? result.StdErr : result.StdErr[^StdErrTail..];
            return (new ReviewerOutcome.NonZeroExit(result.ExitCode, tail.Trim()), null, Usage.None, null, string.Empty);
        }

        // Both reads go through the vendor's own adapter: where the answer lands and how the run
        // is billed are vendor knowledge, and keeping them here would have made every new vendor
        // an edit to this class.
        var usage = invocation.Adapter?.ReadUsage(invocation, result) ?? UsageParser.Parse(result.StdOut);
        var (review, answer, evidence) = Parse(invocation, result);
        return (null, review, usage, answer, evidence);
    }

    private static (NormalisedReview? Review, string? Answer, string Evidence) Parse(ReviewerInvocation invocation, ProcessResult result)
    {
        var raw = invocation.Adapter is { } adapter
            ? adapter.ReadAnswer(invocation, result)
            : ReviewerOutput.FileOrStdout(invocation, result);
        // The EVIDENCE is not always the answer. When a vendor's envelope comes back empty there
        // is nothing in the field the adapter reads, and the diagnosis — its status, its error,
        // its own event stream — is sitting in stdout, which was being thrown away. A kept file
        // of zero bytes is what that looked like from outside.
        var evidence = string.IsNullOrWhiteSpace(raw) ? Transcript(result) : raw;
        if (raw is null)
        {
            return (null, null, evidence);
        }

        // Gemini answers through its envelope and its habits; codex's -o file is schema-bound
        // already, but the same balanced extraction costs nothing and forgives a stray banner.
        if (GeminiPayload.Extract(raw) is not ExtractOutcome.Payload payload)
        {
            return (null, raw, evidence);
        }

        return ReviewParser.Parse(payload.Json, invocation.Provider) is ParseOutcome.Success success
            ? (success.Review, raw, evidence)
            : (null, raw, evidence);
    }
}

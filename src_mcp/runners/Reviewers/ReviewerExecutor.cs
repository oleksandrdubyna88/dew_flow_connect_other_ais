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
    /// <param name="Attempts">
    /// How many launches it actually took before this was the answer — one when the limit was
    /// hopeless from the first word, up to one more than the ladder has steps.
    /// </param>
    /// <remarks>
    /// It travels ON the outcome because the summary is built from outcomes alone, and the number
    /// is known only inside the scheduler's retry loop. Without it the round said "after one retry"
    /// however many launches there had been — a sentence that was true when there was one step and
    /// became a confident wrong number the moment there were four.
    /// </remarks>
    public sealed record RateLimited(string Reason = "", int Attempts = 1) : ReviewerOutcome;

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
        ["rate limit", "usage limit", "quota", "unavailable", "high demand"];

    /// <summary>
    /// The status codes, matched as CODES rather than as three digits anywhere in the output.
    /// </summary>
    /// <remarks>
    /// <para><b>`429` and `503` used to be in the phrase list above</b>, matched as bare substrings
    /// of stdout and stderr — and a Cloudflare ray id is hexadecimal, a token count is a number, and
    /// a duration in milliseconds is a number. Measured 2026-09-03: a codex reviewer handed
    /// <c>unexpected status 404 Not Found ... cf-ray: a3…</c> was reported to the person as
    /// <i>"rate limited (after one retry)"</i>, so they were told to wait for a quota that was never
    /// the problem, and the reviewer was retried against a route that answers 404.</para>
    /// <para>A code counts when something says it IS a status — `HTTP 429`, `status: 503`,
    /// `code 429` — or when the reason phrase follows it, which is how the CLIs actually print one:
    /// `429 Too Many Requests`, `503 UNAVAILABLE`, `503 Service Unavailable`.</para>
    /// </remarks>
    /// <remarks>
    /// <para>The alternatives are exactly the shapes in the table below and nothing else. The first
    /// version of this regex also took <c>code</c>, <c>status_code</c> and <c>error</c> as labels
    /// and a bare <c>rate</c> as a reason — none of them observed from a vendor, and the same class
    /// of guess as the bare <c>429</c> it replaced. Raised in this change's own code round: a rule
    /// that says every code is observed is not kept by a pattern that accepts four more.</para>
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex StatusCode = new(
        @"(?:\bhttps?\s*[:/]?\s*(?:429|503)\b)"
            + @"|(?:\bstatus\b\W{0,4}(?:429|503)\b)"
            + @"|(?:\b429\b\s*[:\-]?\s*too\s+many)"
            + @"|(?:\b503\b\s*[:\-]?\s*(?:service\s+)?unavailable)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool Hit(ProcessResult result) =>
        result.ExitCode != 0 && (Marked(result.StdErr) || Marked(result.StdOut));

    private static bool Marked(string text) =>
        Phrases.Any(p => Contains(text, p)) || StatusCode.IsMatch(text);

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
        return lines.FirstOrDefault(Marked) ?? string.Empty;
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
        var launch = await LaunchAsync(invocation, ct);

        return launch.Terminal is not null
            ? (launch.Terminal, null, launch.Usage, null, string.Empty)
            : (null, ParseAnswer(launch.Answer, invocation.Provider), launch.Usage, launch.Answer, launch.Evidence);
    }

    /// <summary>
    /// One launch, as far as "what the process said" — and no further.
    /// </summary>
    /// <remarks>
    /// <para><b>Public because a second binary needs exactly this half.</b> The Team server runs the
    /// same vendor CLIs through the same adapters and hands the vendor's RAW answer back over HTTP;
    /// parsing, the repair launch and de-duplication stay with the client that asked for the review,
    /// which already does all three for a local reviewer. A copy of this classification in the
    /// server is how the vendor-set drift in this repository happened twice.</para>
    /// <para><b><c>Terminal == null</c> means the process ran and exited zero</b> — it is not a
    /// promise that there is an answer. An adapter whose output file never appeared says so with a
    /// null <see cref="ReviewerLaunch.Answer"/>, and what that means is the caller's judgement.</para>
    /// <para>Cancellation and the kill belong to <see cref="IProcessLauncher"/>, which takes the
    /// token and terminates the whole process tree; a cancelled launch throws out of here and
    /// <c>BoundedScheduler</c> reports the reviewer as abandoned. Nothing about that changes here.
    /// A vendor adapter that THROWS out of its own read is a defect in that adapter — every shipped
    /// one catches its IO and JSON failures and answers with a null answer or
    /// <see cref="Usage.None"/>.</para>
    /// </remarks>
    public async Task<ReviewerLaunch> LaunchAsync(ReviewerInvocation invocation, CancellationToken ct)
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
            return new ReviewerLaunch(
                new ReviewerOutcome.NotStarted($"'{invocation.Request.Executable}' could not be started: {e.Message}"),
                null,
                Usage.None,
                string.Empty);
        }

        if (result.TimedOut)
        {
            return new ReviewerLaunch(new ReviewerOutcome.TimedOut(), null, Usage.None, string.Empty);
        }

        if (RateLimit.Hit(result))
        {
            return new ReviewerLaunch(new ReviewerOutcome.RateLimited(RateLimit.Reason(result)), null, Usage.None, string.Empty);
        }

        if (result.ExitCode != 0)
        {
            var tail = result.StdErr.Length <= StdErrTail ? result.StdErr : result.StdErr[^StdErrTail..];
            return new ReviewerLaunch(new ReviewerOutcome.NonZeroExit(result.ExitCode, tail.Trim()), null, Usage.None, string.Empty);
        }

        // Both reads go through the vendor's own adapter: where the answer lands and how the run
        // is billed are vendor knowledge, and keeping them here would have made every new vendor
        // an edit to this class.
        var usage = invocation.Adapter?.ReadUsage(invocation, result) ?? UsageParser.Parse(result.StdOut);
        var (answer, evidence) = Read(invocation, result);

        return new ReviewerLaunch(null, answer, usage, evidence);
    }

    /// <summary>What the vendor said, and what is left to show when it said nothing.</summary>
    private static (string? Answer, string Evidence) Read(ReviewerInvocation invocation, ProcessResult result)
    {
        var raw = invocation.Adapter is { } adapter
            ? adapter.ReadAnswer(invocation, result)
            : ReviewerOutput.FileOrStdout(invocation, result);
        // The EVIDENCE is not always the answer. When a vendor's envelope comes back empty there
        // is nothing in the field the adapter reads, and the diagnosis — its status, its error,
        // its own event stream — is sitting in stdout, which was being thrown away. A kept file
        // of zero bytes is what that looked like from outside. An EMPTY answer takes the same path
        // as a missing one, which is why the test is `IsNullOrWhiteSpace` and not `is null`.
        return (raw, string.IsNullOrWhiteSpace(raw) ? Transcript(result) : raw);
    }

    /// <summary>
    /// The vendor's answer as findings, or null when it is not findings at all.
    /// </summary>
    /// <remarks>
    /// Pure, and it takes exactly what it reads: the text the adapter produced, and the provider,
    /// which stamps each finding with where it came from. Every vendor-specific decision — which
    /// file, which envelope, which NDJSON event — has already happened in the adapter's own
    /// <c>ReadAnswer</c>, which is why nothing else from the invocation reaches here.
    /// </remarks>
    internal static NormalisedReview? ParseAnswer(string? raw, string provider)
    {
        if (raw is null)
        {
            return null;
        }

        // Gemini answers through its envelope and its habits; codex's -o file is schema-bound
        // already, but the same balanced extraction costs nothing and forgives a stray banner.
        return GeminiPayload.Extract(raw) is ExtractOutcome.Payload payload
               && ReviewParser.Parse(payload.Json, provider) is ParseOutcome.Success success
            ? success.Review
            : null;
    }
}

/// <summary>
/// What ONE launch produced, before anyone reads it as findings.
/// </summary>
/// <param name="Terminal">
/// The outcome when the launch itself decided the answer — <c>NotStarted</c>, <c>TimedOut</c>,
/// <c>RateLimited</c>, <c>NonZeroExit</c> — and null when the process ran and exited zero.
/// </param>
/// <param name="Answer">
/// The vendor's own answer, as ITS adapter extracts it, unparsed. Null when there was nothing where
/// this vendor puts one; that is a fact about the run, not a verdict on it.
/// </param>
/// <param name="Usage">What the run consumed, from the vendor's own reporting. Never null: an
/// adapter that cannot read its own numbers answers <see cref="Usage.None"/>.</param>
/// <param name="Evidence">
/// The answer, or the process transcript when the envelope came back empty — the diagnosis, for the
/// one failure whose raw text is the whole story.
/// </param>
public sealed record ReviewerLaunch(
    ReviewerOutcome? Terminal,
    string? Answer,
    Usage Usage,
    string Evidence);

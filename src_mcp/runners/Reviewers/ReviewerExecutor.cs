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
    /// <param name="Evidence">
    /// Where this reviewer's raw answer was kept, or empty for the ordinary case where it was not.
    /// <para>Only a review with NO findings is kept. A reviewer that answers `{"findings": []}` has
    /// succeeded by every measure the round can take — an `ok` outcome, its tokens counted — and on
    /// 2026-09-08 eight of them did that on a diff a ninth reviewer found eleven things in. Nothing
    /// was kept, so the question "did the model answer emptily, or was it handed a prompt that
    /// deserved an empty answer" had no artefact behind it at all.</para>
    /// <para>It travels ON the outcome rather than being logged where it is written, because the
    /// sentence that needs it is the reviewer's own audit line — which is written by the caller,
    /// from the outcome, and is where somebody chasing a silent round is already reading.</para>
    /// </param>
    public sealed record Ok(NormalisedReview Review, bool Repaired, Usage Usage, string Evidence = "")
        : ReviewerOutcome
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
public sealed class ReviewerExecutor(
    IProcessLauncher launcher,
    string? keepUnparseableIn = null,
    string? keepEmptyIn = null,
    Action<string>? note = null)
{
    private const int StdErrTail = 400;

    /// <summary>
    /// How much of a failing CLI's stderr travels with its exit code — in WHOLE LINES.
    /// </summary>
    /// <remarks>
    /// <para>It used to be the last <see cref="StdErrTail"/> characters, full stop, and the cut
    /// landed wherever it landed. Measured on a real gate, 2026-09-08:
    /// <c>local/Conventions FAILED after 290.0s: exit 69: l Qwen3.5-35B-A3B-Q5_vk128:latest,
    /// pid 52068)</c> — a sentence that begins mid-word, because the reason-picker takes the first
    /// line of this tail and the first line of this tail was half of one.</para>
    /// <para><b>Whole lines by CONSTRUCTION, not by a flag.</b> The plan proposed telling the picker
    /// that the tail had been truncated so it could drop the first line; all three plan reviewers
    /// found the same two holes in that, from three directions. A 400-character cut can land exactly
    /// on a newline, and dropping the first line then discards a complete diagnostic; and a stderr
    /// with no newline inside the budget has no first line to drop, so dropping it leaves a real
    /// failure reported as blank. Trimming here — where the whole stderr is in hand and the boundary
    /// is knowable — costs the picker no new parameter and leaves it no case to get wrong.</para>
    /// <para>The one thing this cannot do is fit a line longer than the whole budget. Its OPENING is
    /// kept, which is the half <c>Because</c> shows anyway.</para>
    /// </remarks>
    public static string TailOf(string stdErr)
    {
        var written = stdErr.Trim();
        if (written.Length <= StdErrTail)
        {
            return written;
        }

        var kept = WholeLinesFrom(written);

        return kept.Length > 0 ? kept : OpeningOfLastLine(written);
    }

    /// <summary>As many complete trailing lines as the budget holds, and never half of one.</summary>
    private static string WholeLinesFrom(string written)
    {
        var cut = written[^StdErrTail..];

        // The cut begins a line only when what it followed was a newline. Anything else means it
        // landed inside one, and everything up to the next break is a fragment.
        if (written[^(StdErrTail + 1)] == '\n')
        {
            return cut.Trim();
        }

        var firstBreak = cut.IndexOf('\n');

        return firstBreak < 0 ? string.Empty : cut[(firstBreak + 1)..].Trim();
    }

    /// <summary>One diagnostic longer than the whole budget: its beginning, where the reason is.</summary>
    private static string OpeningOfLastLine(string written)
    {
        var lastBreak = written.LastIndexOf('\n');
        var line = (lastBreak < 0 ? written : written[(lastBreak + 1)..]).TrimStart();

        return (line.Length <= StdErrTail ? line : line[..StdErrTail]).Trim();
    }

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
    private string? Keep(ReviewerInvocation invocation, string? raw) =>
        KeepIn(keepUnparseableIn, invocation, raw);

    /// <summary>
    /// One reviewer's raw answer, on disk, under whichever directory the question belongs to.
    /// </summary>
    /// <remarks>
    /// <para><b>The name identifies the author.</b> `provider-Role-timestamp` is unique within a
    /// round by construction — one reviewer per provider and role — which matters because a person
    /// reading a silent round has twelve files and needs the one belonging to codex/Architecture.
    /// Nothing inside the file says who wrote it.</para>
    /// <para><b>Written to a sibling and renamed.</b> `File.WriteAllText` truncates first and writes
    /// second, so a process killed between those steps leaves a file that exists and holds nothing —
    /// which, in THIS directory, reads exactly like a vendor that answered with nothing. That is the
    /// one confusion the evidence exists to prevent. `RemoteRuntime.Claim` documents the same trap
    /// and the same cure; it is not shared code because that one also retries a `Replace` against a
    /// reader holding the destination, and here the destination is a name nothing else knows.</para>
    /// </remarks>
    private string? KeepIn(string? directory, ReviewerInvocation invocation, string? raw)
    {
        if (directory is null || raw is null)
        {
            return null;
        }

        var pending = string.Empty;
        try
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(
                directory,
                $"{FileSafe.Part(invocation.Provider)}-{FileSafe.Part(invocation.Role)}"
                    + $"-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt");
            pending = file + ".writing";
            File.WriteAllText(pending, Bounded(raw));
            File.Move(pending, file, overwrite: true);
            return file;
        }
        // The whole expected filesystem set, not the two that were obvious. Keeping evidence may
        // never be what fails a round, and `Directory.CreateDirectory` on a path that is really a
        // file, an invalid character reaching `Path.Combine`, or a policy refusing the write are
        // each a way to turn a review the vendor answered perfectly into a failed one. Raised by
        // four reviewers on the code round; `IOException` already covered the disk-full and
        // path-too-long cases, and these are the rest.
        catch (Exception e) when (e is IOException
                                      or UnauthorizedAccessException
                                      or System.Security.SecurityException
                                      or ArgumentException
                                      or NotSupportedException)
        {
            // Never silently: a round that could not keep its evidence must say so, or the empty
            // directory later reads as "nothing was ever silent here". The note goes to the caller
            // because this class has no logger of its own and should not grow one.
            note?.Invoke($"the answer could not be kept in {directory}: {e.Message}");

            // The sibling, if the write got that far. A `.writing` file left in an evidence
            // directory is neither an answer nor an absence, and repeated failures accumulate them.
            Discard(pending);
            return null;
        }
    }

    /// <summary>
    /// The cap on ONE kept answer.
    /// </summary>
    /// <remarks>
    /// The answers this directory collects are 40 to 90 output tokens — a few hundred bytes — so the
    /// cap never fires on the case it was written for. It exists for the case it was NOT: a vendor
    /// that returns a megabyte of prose around an empty findings array is a different animal, and
    /// evidence is for reading rather than for archiving whatever arrives.
    /// </remarks>
    private const int EvidenceCap = 64 * 1024;

    private static string Bounded(string raw) =>
        raw.Length <= EvidenceCap
            ? raw
            : raw[..EvidenceCap] + $"{Environment.NewLine}{Environment.NewLine}"
                + $"[truncated at {EvidenceCap} characters]";

    private static void Discard(string pending)
    {
        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            File.Delete(pending);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing further to do: this is the cleanup path of a failure that is already reported.
        }
    }

    /// <summary>
    /// Why there is no second launch to make, or null when there is one.
    /// </summary>
    /// <remarks>
    /// <para>Two questions with one answer — nobody configured a repair, or the deadline is spent —
    /// and they were two `if` blocks returning nearly the same object until the code round counted
    /// `RunAsync`'s branches. Asking once puts the two REASONS beside each other, which is where
    /// they belong: they differ only in the sentence a person reads.</para>
    /// <para>A spent deadline matters more than it looks. `ProcessLauncher` calls
    /// `CancelAfter(request.Timeout)`, so a repair handed zero would start a process and cancel it
    /// in the same breath, then report a timeout describing the repair rather than the answer that
    /// needed one — hiding the diagnosis somebody is actually looking for.</para>
    /// </remarks>
    /// <summary>
    /// A reviewer that answered — and, when it answered with NOTHING, the file that says so.
    /// </summary>
    /// <remarks>
    /// <para>A review with no findings is a success by every measure the round can take: an `ok`
    /// outcome, its tokens counted, its seconds recorded. On 2026-09-08 eight remote reviewers
    /// returned `{"findings": []}` in four to twenty-five seconds each, on a diff the local
    /// reviewer found eleven things in — and there was nothing to read afterwards, because the raw
    /// text of a parsed answer is dropped. Only a PARSE failure reached `unparseable/`.</para>
    /// <para>Kept in its own directory rather than beside those, because "it said nothing" and
    /// "it said something I could not read" are different questions and a person chasing one must
    /// not wade through the other.</para>
    /// <para>An answer WITH findings is not kept. A directory that also collected the healthy case
    /// would be a directory whose name lies, and the one file that matters would be one of
    /// hundreds.</para>
    /// </remarks>
    private ReviewerOutcome.Ok Answered(
        NormalisedReview review,
        bool Repaired,
        Usage usage,
        ReviewerInvocation invocation,
        string? raw) =>
        new(review,
            Repaired,
            usage,
            review.Findings.IsEmpty ? KeepIn(keepEmptyIn, invocation, raw) ?? string.Empty : string.Empty);

    private static string? NoRepairToRun(ReviewerInvocation? repair, TimeSpan left) => repair switch
    {
        null => "and no repair was configured",
        _ when left <= TimeSpan.Zero => "and the reviewer's deadline was spent before it could be repaired",
        _ => null,
    };

    public async Task<ReviewerOutcome> RunAsync(
        ReviewerInvocation invocation,
        ReviewerInvocation? repair = null,
        CancellationToken ct = default)
    {
        // The reviewer's deadline bounds the REVIEWER, and this method makes up to two launches.
        // The clock starts here so the second one gets what the first left, exactly as the
        // rate-limit ladder already does — same helper, same arithmetic.
        var spent = System.Diagnostics.Stopwatch.StartNew();
        var budget = invocation.Request.Timeout;
        var (outcome, review, usage, answer, evidence) = await RunOnceAsync(invocation, ct);
        if (outcome is not null)
        {
            return outcome;
        }

        if (review is { } parsed)
        {
            return Answered(parsed, Repaired: false, usage, invocation, answer);
        }

        // Measured in the ledger before it was fixed: one reviewer at 668.8 s against a ten-minute
        // deadline, reporting `ok`. `reviewerTimeoutMinutes` bounded each LAUNCH, so a reviewer that
        // needed a repair could take twice what it was given — and the operator who reported "the
        // limit did not work again" was reading a real number.
        var left = RetryLadder.Remaining(spent.Elapsed, budget);
        if (NoRepairToRun(repair, left) is { } why)
        {
            return new ReviewerOutcome.Unparseable(
                Because(Said(answer, Complaint(evidence)), why, Keep(invocation, evidence)), usage);
        }

        // The LESSER of the two, not whichever was computed last. The scheduler may already have
        // shortened this repair on a retry, and overwriting that with the executor's own remainder
        // would hand it back time the ladder had taken away. Raised on the code round.
        var repairBudget = left < repair!.Request.Timeout ? left : repair.Request.Timeout;
        var (repairOutcome, repaired, repairUsage, repairAnswer, repairEvidence) =
            await RunOnceAsync(repair with { Request = repair.Request with { Timeout = repairBudget } }, ct);
        return repairOutcome
               ?? (repaired is { } fixedReview
                   // Both launches are billed, so both are counted — a repaired reviewer that
                   // reported only its second attempt would under-report every time.
                   ? Answered(fixedReview, Repaired: true, usage.Add(repairUsage), repair, repairAnswer)
                   // BOTH launches are kept, and the first one wins when the repair came back
                   // empty: a vendor whose envelope broke leaves nothing to read, and the
                   // evidence file was landing at zero bytes exactly when it was most needed.
                   : new ReviewerOutcome.Unparseable(
                       Because(
                           // The complaint comes from whichever launch actually explained itself,
                           // starting with the repair — its answer is the one being described. Picking
                           // by SIZE was wrong and the gate said so: a first attempt that returned a
                           // large malformed answer beats a repair that returned nothing plus a
                           // permission refusal on stderr, and the refusal is the whole point.
                           Said(repairAnswer ?? answer, FirstComplaint(repairEvidence, evidence)),
                           "after one repair attempt",
                           Keep(repair, Longer(evidence, repairEvidence))),
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
    private static string Said(string? raw, string complaint) =>
        string.IsNullOrWhiteSpace(raw)
            ? Explained("the vendor returned an empty answer", complaint)
            : "the answer was not the schema's JSON";

    /// <summary>The first transcript that carries an explanation, in the order given.</summary>
    /// <remarks>
    /// The KEEP path still saves the LONGER transcript deliberately — a repair whose envelope broke
    /// leaves a zero-byte file, and that was landing exactly when it was most needed. Which file to
    /// save and which stream explained the failure are two different questions, and answering them
    /// with one rule dropped the explanation.
    /// </remarks>
    internal static string FirstComplaint(params string?[] transcripts)
    {
        foreach (var transcript in transcripts)
        {
            var said = Complaint(transcript);
            if (said.Length > 0)
            {
                return said;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The CLI's own sentence, when it produced nothing and said why.
    /// </summary>
    /// <remarks>
    /// Measured on a real round, 2026-09-06: three antigravity reviewers reported "the vendor
    /// returned an empty answer" and the panel showed nothing more, while the CLI's stderr said
    /// exactly what happened — a tool wanted the "command" permission, headless mode cannot prompt
    /// for one, so it auto-denied itself and printed nothing. That sentence was only in the kept
    /// evidence file, which nobody knows to open; finding it took a quarter of an hour, and reading
    /// the note should have taken seconds. The transcript is already here — this reads the stderr
    /// half of it.
    /// </remarks>
    internal static string Complaint(string? evidence)
    {
        const string marker = "--- stderr ---";
        var at = evidence?.LastIndexOf(marker, StringComparison.Ordinal) ?? -1;
        if (at < 0)
        {
            return string.Empty;
        }

        var said = evidence![(at + marker.Length)..]
            .Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

        // One line, capped: a note is read in a table cell, and a vendor's stack trace is not the
        // sentence a person needs.
        return said.Length <= ComplaintCap ? said : said[..ComplaintCap] + "…";
    }

    private static string Explained(string what, string why) =>
        why.Length == 0 ? what : $"{what} — it said: {why}";

    private const int ComplaintCap = 240;

    /// <summary>The launch that actually said something — the repair is often the empty one.</summary>
    private static string? Longer(string? first, string? second) =>
        (second?.Trim().Length ?? 0) >= (first?.Trim().Length ?? 0) ? second : first;

    /// <summary>One launch. A non-null outcome is terminal; a null review means "unparseable".</summary>
    /// <summary>
    /// Let the adapter clean up after a run we killed — and never let that cleanup fail the round.
    /// </summary>
    /// <remarks>
    /// It runs on a path where something has ALREADY gone wrong, so an exception here would replace a
    /// reviewer's real failure with a cleanup's. The token is deliberately NOT the round's: the round's
    /// is usually the reason we are here, and a cancelled token would abort the very request that stops
    /// the work.
    /// </remarks>
    private static async Task AbandonAsync(ReviewerInvocation invocation)
    {
        if (invocation.Adapter is null)
        {
            return;
        }

        try
        {
            using var budget = new CancellationTokenSource(AbandonBudget);
            await invocation.Adapter.AbandonAsync(invocation, budget.Token);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Deliberately broad: this is a courtesy call on a failure path, and the vendor's own
            // failure is the one the caller must still see.
        }
    }

    /// <summary>How long a courtesy cleanup may take before the round stops waiting for it.</summary>
    private static readonly TimeSpan AbandonBudget = TimeSpan.FromSeconds(10);

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
        catch (OperationCanceledException)
        {
            // The round was abandoned and the tree was killed. A killed process runs no cleanup of
            // its own, so anything it started ELSEWHERE is still running — for a Team server
            // reviewer, on the company's subscription, for an answer nobody will now collect. This
            // is the only moment anybody still knows the job existed.
            await AbandonAsync(invocation);

            throw;
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
            // Same reasoning as the cancellation above: the tree was killed on our deadline, so the
            // shim never reached its own polite DELETE.
            await AbandonAsync(invocation);

            return new ReviewerLaunch(new ReviewerOutcome.TimedOut(), null, Usage.None, string.Empty);
        }

        if (RateLimit.Hit(result))
        {
            return new ReviewerLaunch(new ReviewerOutcome.RateLimited(RateLimit.Reason(result)), null, Usage.None, string.Empty);
        }

        if (result.ExitCode != 0)
        {
            return new ReviewerLaunch(new ReviewerOutcome.NonZeroExit(result.ExitCode, TailOf(result.StdErr)), null, Usage.None, string.Empty);
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

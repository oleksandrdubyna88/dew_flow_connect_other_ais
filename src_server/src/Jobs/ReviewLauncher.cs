using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>
/// What one attempt at one vendor, on one account, came back with: an answer, or the reason there
/// is none.
/// </summary>
/// <remarks>
/// <para><b>Two shapes, because the one-shape version could not express a success.</b> It used to
/// be a single record carrying a <see cref="ReviewerOutcome"/>, and the executor says a good run by
/// returning NO outcome — <c>ReviewerLaunch.Terminal</c> is null exactly when the process ran and
/// exited zero. There was nothing to put in the field, so the launcher put
/// <c>NotStarted("the executor returned no verdict")</c> there, and every successful review on this
/// server was recorded as a review that never began. The runner's success arm was unreachable for
/// as long as the type made success unsayable.</para>
/// <para>It survived 171 green tests because the only thing that ever produced the success shape
/// was the suite's own fake launcher, handing the runner a <c>ReviewerOutcome.Ok(null!, …)</c> that
/// the real launcher cannot construct — it holds raw text, not a parsed review. Each side was
/// tested against itself. `usage.jsonl` on the live server held two lines, weeks apart, both
/// <c>NotStarted</c>, with the vendor's own transcript beside them showing a perfect answer.</para>
/// </remarks>
public abstract record ReviewAttempt
{
    /// <summary>Closed: these two shapes are the whole vocabulary, and nobody adds a third.</summary>
    private ReviewAttempt()
    {
    }

    /// <param name="Raw">
    /// The vendor's RAW text. Parsing, repair and de-duplication stay in the client, so the same
    /// parser does not exist twice and drift.
    /// </param>
    public sealed record Answered(string Raw, long TokensIn, long TokensOut) : ReviewAttempt;

    /// <param name="Outcome">
    /// The executor's own verdict, unchanged — this server maps it to a wire vocabulary and invents
    /// none of its own.
    /// </param>
    public sealed record Failed(ReviewerOutcome Outcome) : ReviewAttempt;
}

/// <summary>
/// The seam between the job runner and an actual vendor CLI.
/// </summary>
/// <remarks>
/// It exists so the runner's rules — slot rotation, cooldowns, deadlines, the retry ladder — are
/// tested against every outcome without a vendor CLI on the machine. The plan round asked for an
/// end-to-end test through the real HTTP surface; this is what makes one possible in CI, where
/// launching `codex` is not.
/// </remarks>
public interface IReviewLauncher
{
    Task<ReviewAttempt> RunAsync(
        VendorConfig vendor,
        AccountSlot slot,
        JobRecord job,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken ct);
}

/// <summary>Runs the vendor's real CLI through the executor both binaries share.</summary>
public sealed class ReviewLauncher(IProcessLauncher launcher, Action<string, Exception>? onFailure = null) : IReviewLauncher
{
    public async Task<ReviewAttempt> RunAsync(
        VendorConfig vendor,
        AccountSlot slot,
        JobRecord job,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken ct)
    {
        var runtime = RuntimeResolution.For(new VendorIdentity(vendor.Id, vendor.Runtime, string.Empty));
        if (runtime is null)
        {
            return new ReviewAttempt.Failed(
                new ReviewerOutcome.NotStarted($"no runtime adapter for '{vendor.Runtime}'"));
        }

        // Its own directory per job, deleted by the runner in a finally. The vendor writes its answer
        // here and nowhere near another job's.
        var work = Directory.CreateTempSubdirectory("coai-server-job-").FullName;
        try
        {
            // WRITTEN, not merely named. Every adapter but claude's passes this path straight to its
            // CLI — `--json-schema` for antigravity, `--output-schema` for codex — and a path to a
            // file nobody wrote is a CLI that refuses before it reads the prompt. Measured against
            // the live server: `failed to read schema file "/tmp/coai-server-job-…/…"`, for both
            // vendors, on the first day either of them could be tried at all.
            var built = runtime.Build(
                RoleOf(job.Role),
                job.Prompt,
                work,
                SchemaFile.Ensure(work),
                work,
                new ReviewerSettings(vendor.Id) { Model = job.Model, Timeout = job.RunBudget });

            // The slot's HOME goes into the REQUEST's environment, never argv and never a log line —
            // the rule every runtime here already keeps for credentials. The adapter built the command;
            // this decides which account it runs as.
            var invocation = built with { Request = built.Request with { Environment = environment } };

            return Read(await new ReviewerExecutor(launcher).LaunchAsync(invocation, ct));
        }
        finally
        {
            Delete(work);
        }
    }

    /// <summary>What one launch means to this server.</summary>
    /// <remarks>
    /// <para>Pure, and the only place the executor's contract is read: a <c>Terminal</c> outcome is
    /// the launch deciding for itself; a null one means the process RAN and exited zero, which is
    /// what a good review looks like from here. Reading that null as a failure is the defect this
    /// method exists to make impossible.</para>
    /// <para>An empty answer after a clean exit is still a failure — but
    /// <see cref="ReviewerOutcome.Unparseable"/>, not <c>NotStarted</c>. The CLI started; it
    /// produced nothing usable, and telling a person their review "never started" sends them to
    /// look at accounts and executables instead of at the vendor's own empty envelope.</para>
    /// <para><b>"Empty" means whitespace, and nothing cleverer — this server still has no parser.</b>
    /// The vendor's own adapter has already taken the answer out of its envelope by the time this
    /// runs (<c>ClaudeRuntime.ReadAnswer</c> lifts the <c>result</c> field, and its siblings do the
    /// equivalent), so what arrives here is the vendor's raw ANSWER text and this only asks whether
    /// there is any. A well-formed answer with no findings — <c>{"findings":[]}</c> — is a real
    /// answer and travels as one; whether it parses, and whether an empty list is what the caller
    /// wanted, belongs to the client that asked. Raised as a contradiction on the plan round by
    /// gemini and by local; there is none, but the boundary is worth naming where it lives.</para>
    /// </remarks>
    private static ReviewAttempt Read(ReviewerLaunch launch) =>
        launch.Terminal is { } terminal
            ? new ReviewAttempt.Failed(terminal)
            : string.IsNullOrWhiteSpace(launch.Answer)
                ? new ReviewAttempt.Failed(new ReviewerOutcome.Unparseable(
                    "the vendor exited cleanly without writing an answer", launch.Usage))
                : new ReviewAttempt.Answered(launch.Answer, launch.Usage.TokensIn, launch.Usage.TokensOut);

    /// <summary>The role a client named. Validated at the endpoint, so a bad one cannot arrive here.</summary>
    private static ReviewRole RoleOf(string role) =>
        Enum.TryParse<ReviewRole>(role, ignoreCase: true, out var parsed) ? parsed : default;
    private void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A vendor helper still holding a file must not fail a review that already succeeded —
            // but it is REPORTED, naming the directory. A machine quietly filling with job
            // directories is the kind of thing nobody notices until the disk is full.
            onFailure?.Invoke($"the working directory {directory} could not be removed", e);
        }
    }
}

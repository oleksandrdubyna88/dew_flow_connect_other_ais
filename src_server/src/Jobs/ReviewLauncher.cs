using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>What one attempt at one vendor, on one account, came back with.</summary>
/// <param name="Outcome">
/// The executor's own verdict, unchanged. Six shapes, already classified and already tested where
/// they are defined — this server maps them to a wire vocabulary and invents none of its own.
/// </param>
/// <param name="Answer">
/// The vendor's RAW text. Parsing, repair and de-duplication stay in the client, so the same parser
/// does not exist twice and drift.
/// </param>
public sealed record ReviewAttempt(ReviewerOutcome Outcome, string Answer, long TokensIn, long TokensOut);

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
public sealed class ReviewLauncher(IProcessLauncher launcher) : IReviewLauncher
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
            return new ReviewAttempt(
                new ReviewerOutcome.NotStarted($"no runtime adapter for '{vendor.Runtime}'"), string.Empty, 0, 0);
        }

        // Its own directory per job, deleted by the runner in a finally. The vendor writes its answer
        // here and nowhere near another job's.
        var work = Directory.CreateTempSubdirectory("coai-server-job-").FullName;
        try
        {
            var built = runtime.Build(
                RoleOf(job.Role),
                job.Prompt,
                work,
                Path.Combine(work, "schema.json"),
                work,
                new ReviewerSettings(vendor.Id) { Model = job.Model, Timeout = job.RunBudget });

            // The slot's HOME goes into the REQUEST's environment, never argv and never a log line —
            // the rule every runtime here already keeps for credentials. The adapter built the command;
            // this decides which account it runs as.
            var invocation = built with { Request = built.Request with { Environment = environment } };

            var launch = await new ReviewerExecutor(launcher).LaunchAsync(invocation, ct);

            return new ReviewAttempt(
                launch.Terminal ?? new ReviewerOutcome.NotStarted("the executor returned no verdict"),
                launch.Answer ?? string.Empty,
                launch.Usage.TokensIn,
                launch.Usage.TokensOut);
        }
        finally
        {
            Delete(work);
        }
    }

    /// <summary>The role a client named, or the general one.</summary>
    /// <remarks>
    /// An unknown role is not refused: the roles are the CLIENT's vocabulary and it may gain one
    /// before this server does, and refusing would make a Team server the thing that has to be
    /// upgraded first. The prompt carries the actual instruction either way.
    /// </remarks>
    private static ReviewRole RoleOf(string role) =>
        Enum.TryParse<ReviewRole>(role, ignoreCase: true, out var parsed) ? parsed : default;

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A vendor helper still holding a file here must not fail a review that already
            // succeeded. The directory is under the system temp root and is swept by the OS.
        }
    }
}

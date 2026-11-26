using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server;

/// <summary>What a caller sent as THE PLAN: its text, the path of a committed file, or the folder of a committed work plan (and the commit it expects).</summary>
internal sealed record PlanInput(string Text, string Committed, string ExpectedCommit)
{
    /// <summary>The plan item the call named in <c>workItem</c>, as sent — checked by <see cref="WorkItems"/> before any round is spent; empty when none was named.</summary>
    public string WorkItem { get; init; } = string.Empty;

    /// <summary>The repository-relative folder of a committed work plan the server reads, validates and packs itself (epoch 3 E3 s3); empty when the call named a text or a file.</summary>
    public string FeatureFolder { get; init; } = string.Empty;
}

internal abstract record PlanInputOutcome
{
    private protected PlanInputOutcome()
    {
    }

    /// <param name="Text">What the reviewers read.</param>
    /// <param name="Head">The one commit the round runs on; empty for a pasted plan (the engine resolves the branch).</param>
    /// <param name="Source">What was read; <c>null</c> for a pasted plan.</param>
    /// <param name="SourceLine">The line under the prompt's heading that says where the text came from; empty for a pasted plan.</param>
    /// <param name="Unreviewed">What the round did not compare, ready to append to the reviewer line (<c>; …</c>); empty when nothing.</param>
    /// <param name="TextId">The id of <paramref name="Text"/> — computed ONCE here (the reader's, or the pasted text's) and used by the prompt and the log line.</param>
    public sealed record Ready(string Text, string Head, PlanSource? Source, string SourceLine, string Unreviewed, string TextId) : PlanInputOutcome;

    public sealed record Refused(string Sentence) : PlanInputOutcome;
}

/// <summary>
/// Turns what a caller sent into the plan a round reviews — or the sentence that refuses it, BEFORE any round exists.
/// </summary>
/// <remarks>
/// <para>The decisions, in order: two inputs and none are refused, each pair with a sentence of its own (a guess about intent is not an input); an
/// <c>expectedCommit</c> without <c>committedPlan</c> or <c>featureFolder</c> is refused (an argument that would be silently ignored is a trap);
/// a whitespace-only <c>committedPlan</c>, <c>featureFolder</c> or <c>expectedCommit</c> counts as absent. A pasted plan takes no resolve and
/// no git, exactly as before. A <c>featureFolder</c> is read, validated and packed by <see cref="FeatureFolderPlan"/>.</para>
/// <para>For a committed plan the branch is resolved ONCE here and that commit is the round's <c>Head</c>: the plan and the
/// record are one commit even if the branch moves during the call.
/// <c>ResolveShaAsync</c> throws a <see cref="WorktreeException"/>; it sits outside the engine's <c>try</c>, so it is
/// caught here and answered, as <c>open</c> does. <c>rev-parse --verify &lt;branch&gt;^{commit}</c> answers a COMMIT id —
/// only that goes on to the reader, which would accept any 40-hex object id.</para>
/// </remarks>
internal static class PlanInputs
{
    /// <summary>
    /// How long the whole preparation of a committed plan may take, however many git calls it makes. Each call has its own
    /// 30-second budget; without this the worst case — resolve, root, entry, blob, status in a row — is minutes with no round yet.
    /// </summary>
    public static readonly TimeSpan PreparationDeadline = TimeSpan.FromSeconds(45);

    private const string TooSlow = "git did not answer in time — the round was not started; run the call again when the machine is less busy";

    /// <param name="limits">The work-method limits a folder is validated with; the shipped defaults when none is given (a test that names none reads no settings).</param>
    public static async Task<PlanInputOutcome> PrepareAsync(
        PlanInput input, string repoPath, string branch, Func<string, string, Task<string>> resolve, IProcessLauncher launcher, CancellationToken ct, TimeSpan? deadline = null,
        Func<DeviceLimits>? limits = null)
    {
        var committed = input.Committed.Trim();
        var folder = input.FeatureFolder.Trim();
        var expected = input.ExpectedCommit.Trim();
        var within = deadline ?? PreparationDeadline;

        return Shape(input, committed, folder, expected) is { Length: > 0 } refusal ? new PlanInputOutcome.Refused(refusal)
            : folder.Length > 0 ? await WithinTheDeadlineAsync(
                limit => FeatureFolderPlan.ReadAsync(new FolderCall(folder, input.WorkItem, expected, repoPath, branch), resolve, launcher, limits ?? DeviceLimits.Shipped, limit), within, ct)
            : committed.Length == 0 ? new PlanInputOutcome.Ready(input.Text, string.Empty, null, string.Empty, string.Empty, DocumentRules.ArtifactIdOf(input.Text))
            : await WithinTheDeadlineAsync(limit => ReadCommittedAsync(committed, expected, repoPath, branch, resolve, launcher, limit), within, ct);
    }

    /// <summary>
    /// A read of the plan under ONE overall deadline. A caller that cancels still cancels (that is not a refusal); the
    /// deadline firing is, and it answers before any round exists.
    /// </summary>
    private static async Task<PlanInputOutcome> WithinTheDeadlineAsync(Func<CancellationToken, Task<PlanInputOutcome>> read, TimeSpan deadline, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(deadline);
        try
        {
            var outcome = await read(limit.Token);

            return Expired(limit, ct) ? new PlanInputOutcome.Refused(TooSlow) : outcome;
        }
        catch (OperationCanceledException) when (Expired(limit, ct))
        {
            return new PlanInputOutcome.Refused(TooSlow);
        }
    }

    private static bool Expired(CancellationTokenSource limit, CancellationToken caller) =>
        limit.IsCancellationRequested && !caller.IsCancellationRequested;

    /// <summary>The refusal the arguments themselves earn, or empty — nothing read, nothing resolved.</summary>
    /// <remarks>One small predicate per refusal, asked in this order — both, expectedCommit alone, neither, malformed — and the first sentence wins.</remarks>
    private static string Shape(PlanInput input, string committed, string folder, string expected)
    {
        var (text, path, tree) = (input.Text.Length > 0, committed.Length > 0, folder.Length > 0);

        return new[] { All(text, path, tree), Both(text, path), TextAndFolder(text, tree), FileAndFolder(path, tree), PinWithoutPath(path || tree, expected), Neither(text, path, tree), Malformed(path || tree, expected) }
            .FirstOrDefault(sentence => sentence.Length > 0) ?? string.Empty;
    }

    private static string All(bool text, bool path, bool tree) =>
        text && path && tree
            ? "pass exactly ONE of planText, committedPlan and featureFolder — all three were sent"
            : string.Empty;

    private static string Both(bool text, bool path) =>
        text && path
            ? "pass planText OR committedPlan, not both — planText is the plan verbatim, committedPlan is the path of a committed plan file the server reads itself"
            : string.Empty;

    private static string TextAndFolder(bool text, bool tree) =>
        text && tree
            ? "pass planText OR featureFolder, not both — planText is the plan verbatim, featureFolder is the folder of a committed work plan the server validates and packs itself"
            : string.Empty;

    private static string FileAndFolder(bool path, bool tree) =>
        path && tree
            ? "pass committedPlan OR featureFolder, not both — committedPlan is ONE committed plan file, featureFolder is the folder of a committed work plan (name the part to review with workItem)"
            : string.Empty;

    private static string PinWithoutPath(bool anyPath, string expected) =>
        !anyPath && expected.Length > 0
            ? "expectedCommit pins the commit committedPlan (or featureFolder) is read at — it means nothing without committedPlan or featureFolder; pass both, or neither"
            : string.Empty;

    private static string Neither(bool text, bool path, bool tree) =>
        !text && !path && !tree
            ? "pass the plan: planText (the plan verbatim), committedPlan (the repo-relative path of a committed plan file) or featureFolder (the repo-relative folder of a committed work plan) — a reviewer that cannot see the plan reviews its own guess"
            : string.Empty;

    private static string Malformed(bool anyPath, string expected) =>
        anyPath && expected.Length > 0 && !GitHistory.IsCommitish(expected)
            ? $"expectedCommit must be a full 40-hex commit id — '{expected}' is not one"
            : string.Empty;

    private static async Task<PlanInputOutcome> ReadCommittedAsync(
        string path, string expected, string repoPath, string branch, Func<string, string, Task<string>> resolve, IProcessLauncher launcher, CancellationToken ct)
    {
        var (tip, refusal) = await TipAsync(expected, repoPath, branch, resolve, ct);

        return refusal.Length > 0
            ? new PlanInputOutcome.Refused(refusal)
            : Ready(await CommittedPlanReader.ReadGuardedAsync(new GitHistory(launcher), repoPath, tip, path, ct));
    }

    /// <summary>
    /// The commit the branch is at — resolved ONCE, the commit of the round — or the sentence that refuses the round: a branch that does not resolve, or a tip that is not the <paramref name="expected"/> commit.
    /// One road for a committed file and a folder, so the two refuse a moved branch in the same words.
    /// </summary>
    internal static async Task<(string Tip, string Refusal)> TipAsync(string expected, string repoPath, string branch, Func<string, string, Task<string>> resolve, CancellationToken ct)
    {
        string tip;
        try
        {
            tip = await resolve(repoPath, branch).WaitAsync(ct);
        }
        catch (WorktreeException e)
        {
            return (string.Empty, e.Message);
        }

        return expected.Length > 0 && !expected.Equals(tip, StringComparison.OrdinalIgnoreCase)
            ? (string.Empty,
                $"branch '{branch}' is at commit {GitIds.Short(tip)}, not the expectedCommit {GitIds.Short(expected)} — a commit landed after the plan was written; "
                + $"check what landed (git log {branch}), then pass expectedCommit = {GitIds.Short(tip)} or re-read the plan")
            : (tip, string.Empty);
    }

    private static PlanInputOutcome Ready(PlanRead read) => read switch
    {
        PlanRead.Accepted a => new PlanInputOutcome.Ready(a.Text, a.Source.Commit, a.Source, SourceLine(a.Source), a.Note.Length > 0 ? "; " + a.Note : string.Empty, a.Source.TextId),
        PlanRead.Refused r => new PlanInputOutcome.Refused(r.Sentence),
        _ => throw new InvalidOperationException("a plan read is Accepted or Refused"),
    };

    /// <summary>The line the prompt carries under its heading — where the text came from, so a reviewer can tell a plan from a pointer.</summary>
    internal static string SourceLine(PlanSource source) =>
        $"(read by the server from {source.Path} at commit {GitIds.Short(source.Commit)}, blob {GitIds.Short(source.Blob)}, {source.Bytes} bytes)";
}

using CoaiMcp.Core.Feature;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>The two commits a feature review compares — both resolved to full ids before anything is built.</summary>
/// <param name="Base">The commit before the first epic; empty when the round is skipped before it mattered (D17).</param>
/// <param name="Head">The checkout's HEAD — what the review reads and records.</param>
internal sealed record FeatureRange(string Base, string Head);

/// <summary>
/// The git half of <c>review_feature</c>'s refusals (plan §4.5, item 4): the repository's top level
/// (asked first, before the plan is read), its HEAD, a base that resolves, is not the head, is an ancestor of it, and leaves something to review.
/// </summary>
/// <remarks>
/// <para><b>The head is the checkout's HEAD</b> (S2.2b, the coordinator's shape of D2): the caller runs
/// the review from the tree the feature is on, and a head it would have to name is a head it could
/// mistype. Resolved here to a full id and handed to the engine as that id, so the commit these checks
/// passed is the commit the round reads — not whatever HEAD has become by the time it resolves again.</para>
/// <para><b>The base ref is guarded before git sees it.</b> A value opening with <c>-</c> would be read as
/// an option; whitespace or a control character is never a ref a caller meant. Each refusal names what
/// was wrong and what to pass instead.</para>
/// <para>Every git call goes through the product's one launcher with its own deadline.</para>
/// </remarks>
internal sealed class FeatureRefs(IProcessLauncher launcher)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The range, or the sentence refusing it. With <paramref name="skipping"/> only the head is needed —
    /// a skipped round records the head it would have read — so the base is not asked about at all.
    /// </summary>
    public async Task<FeatureInput<FeatureRange>> ResolveAsync(string repoPath, string baseRef, bool skipping, CancellationToken ct)
    {
        var head = await CommitAsync(repoPath, "HEAD", ct);
        return head.Length == 0 ? Refused($"'{repoPath}' has no commit at HEAD — a feature review reads a committed head")
            : skipping ? new FeatureInput<FeatureRange>.Accepted(new FeatureRange(string.Empty, head))
            : await WithBaseAsync(repoPath, baseRef.Trim(), head, ct);
    }

    private async Task<FeatureInput<FeatureRange>> WithBaseAsync(string repoPath, string baseRef, string head, CancellationToken ct)
    {
        var problem = BaseRefProblem(baseRef);
        var baseSha = problem.Length == 0 ? await CommitAsync(repoPath, baseRef, ct) : string.Empty;

        return problem.Length > 0 ? Refused(problem)
            : baseSha.Length == 0 ? Refused($"baseRef '{baseRef}' does not resolve to a commit in '{repoPath}' — pass the commit before the first epic (a SHA, a tag, or a branch that still points at it)")
            : string.Equals(baseSha, head, StringComparison.OrdinalIgnoreCase) ? Refused($"baseRef '{baseRef}' is HEAD itself ({head}) — there is nothing between them to review; pass the commit before the first epic")
            : await AncestryAsync(repoPath, baseRef, new FeatureRange(baseSha, head), ct);
    }

    private async Task<FeatureInput<FeatureRange>> AncestryAsync(string repoPath, string baseRef, FeatureRange range, CancellationToken ct)
    {
        var ancestor = await GitAsync(repoPath, ["merge-base", "--is-ancestor", range.Base, range.Head], ct);

        return ancestor.ExitCode == 0
            ? await ReviewableAsync(repoPath, baseRef, range, ct)
            : Refused($"baseRef '{baseRef}' ({range.Base}) is not an ancestor of HEAD ({range.Head}) — a feature review compares the commit before the first epic with the head the epics led to; check out the branch the feature is on, or pass the right base");
    }

    /// <summary>The range, when it changes a file a reviewer is shown — lock files and build output are not (<c>DiffExclusions</c>).</summary>
    private async Task<FeatureInput<FeatureRange>> ReviewableAsync(string repoPath, string baseRef, FeatureRange range, CancellationToken ct)
    {
        var change = await new ContextAssembler(launcher).ReviewableAsync(repoPath, range.Base, range.Head, ct: ct);

        return change.IsEmpty
            ? Refused(NothingBetween(baseRef, range, change.ChangedButExcluded))
            : new FeatureInput<FeatureRange>.Accepted(range);
    }

    private static string NothingBetween(string baseRef, FeatureRange range, IReadOnlyList<string> excluded) =>
        $"nothing reviewable changed between baseRef '{baseRef}' ({range.Base}) and HEAD ({range.Head})"
        + (excluded.Count == 0 ? string.Empty : $" — only files a reviewer is never shown: {string.Join(", ", excluded.Take(10))}{(excluded.Count > 10 ? ", …" : string.Empty)}");

    /// <summary>Why <paramref name="baseRef"/> cannot be put to git, or empty when it can.</summary>
    internal static string BaseRefProblem(string baseRef) =>
        baseRef.Length == 0 ? "baseRef was not given — pass the commit before the first epic (the plan's merge base with main before epic 1)"
        : baseRef.StartsWith('-') ? $"baseRef '{baseRef}' starts with '-', which git would read as an option — pass a commit, a tag or a branch"
        : baseRef.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ? $"baseRef '{baseRef}' contains whitespace or a control character — pass a commit, a tag or a branch"
        : string.Empty;

    /// <summary>Why <paramref name="repoPath"/> is not a repository's top level, or empty when it is — asked before the plan is read from it.</summary>
    internal async Task<string> TopLevelProblemAsync(string repoPath, CancellationToken ct)
    {
        var top = Directory.Exists(repoPath) ? await GitAsync(repoPath, ["rev-parse", "--show-toplevel"], ct) : null;

        return top is null ? $"'{repoPath}' is not a directory on this machine"
            : top.ExitCode != 0 ? $"'{repoPath}' is not a git repository: {top.StdErr.Trim()}"
            : SamePath(top.StdOut.Trim(), repoPath) ? string.Empty
            : $"'{repoPath}' is inside a repository whose top level is '{top.StdOut.Trim()}' — pass the top level (git rev-parse --show-toplevel), which is what the review's paths are read from";
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalised(a), Normalised(b), Core.Rounds.DocumentId.Comparison);

    private static string Normalised(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');

    /// <summary>The full id of <paramref name="rev"/>'s commit, or empty when git cannot resolve one.</summary>
    private async Task<string> CommitAsync(string repoPath, string rev, CancellationToken ct)
    {
        var result = await GitAsync(repoPath, ["rev-parse", "--verify", "--quiet", $"{rev}^{{commit}}"], ct);

        return result.ExitCode == 0 ? result.StdOut.Trim() : string.Empty;
    }

    private async Task<ProcessResult> GitAsync(string repoPath, IReadOnlyList<string> args, CancellationToken ct) =>
        await launcher.RunAsync(new ProcessRequest("git", args, repoPath) { Timeout = Deadline }, ct);

    private static FeatureInput<FeatureRange>.Refused Refused(string sentence) => new(sentence);
}

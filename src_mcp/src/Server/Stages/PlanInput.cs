using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server;

/// <summary>What a caller sent as THE PLAN: its text, or the path of a committed file (and the commit it expects).</summary>
internal sealed record PlanInput(string Text, string Committed, string ExpectedCommit);

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
    public sealed record Ready(string Text, string Head, PlanSource? Source, string SourceLine, string Unreviewed) : PlanInputOutcome;

    public sealed record Refused(string Sentence) : PlanInputOutcome;
}

/// <summary>
/// Turns what a caller sent into the plan a round reviews — or the sentence that refuses it, BEFORE any round exists.
/// </summary>
/// <remarks>
/// <para>The decisions, in order: both inputs and neither are refused (a guess about intent is not an input); an
/// <c>expectedCommit</c> without <c>committedPlan</c> is refused (an argument that would be silently ignored is a trap);
/// a whitespace-only <c>committedPlan</c> or <c>expectedCommit</c> counts as absent. A pasted plan takes no resolve and
/// no git, exactly as before.</para>
/// <para>For a committed plan the branch is resolved ONCE here and that commit is the round's <c>Head</c>: the plan, the
/// cadence outline read under the claim and the record are one commit even if the branch moves during the call.
/// <c>ResolveShaAsync</c> throws a <see cref="WorktreeException"/>; it sits outside the engine's <c>try</c>, so it is
/// caught here and answered, as <c>open</c> does. <c>rev-parse --verify &lt;branch&gt;^{commit}</c> answers a COMMIT id —
/// only that goes on to the reader, which would accept any 40-hex object id.</para>
/// </remarks>
internal static class PlanInputs
{
    public static Task<PlanInputOutcome> PrepareAsync(
        PlanInput input, string repoPath, string branch, Func<string, string, Task<string>> resolve, IProcessLauncher launcher, CancellationToken ct)
    {
        var committed = input.Committed.Trim();
        var expected = input.ExpectedCommit.Trim();

        return Shape(input, committed, expected) is { Length: > 0 } refusal
            ? Task.FromResult<PlanInputOutcome>(new PlanInputOutcome.Refused(refusal))
            : committed.Length == 0
                ? Task.FromResult<PlanInputOutcome>(new PlanInputOutcome.Ready(input.Text, string.Empty, null, string.Empty, string.Empty))
                : ReadCommittedAsync(committed, expected, repoPath, branch, resolve, launcher, ct);
    }

    /// <summary>The refusal the arguments themselves earn, or empty — nothing read, nothing resolved.</summary>
    private static string Shape(PlanInput input, string committed, string expected) =>
        (input.Text.Length > 0, committed.Length > 0, expected.Length > 0) switch
        {
            (true, true, _) => "pass planText OR committedPlan, not both — planText is the plan verbatim, committedPlan is the path of a committed plan file the server reads itself",
            (false, false, true) or (true, false, true) => "expectedCommit pins the commit committedPlan is read at — it means nothing without committedPlan; pass both, or neither",
            (false, false, false) => "pass the plan: planText (the plan verbatim) or committedPlan (the repo-relative path of a committed plan file) — a reviewer that cannot see the plan reviews its own guess",
            (_, true, true) when !GitHistory.IsCommitish(expected) => $"expectedCommit must be a full 40-hex commit id — '{expected}' is not one",
            _ => string.Empty,
        };

    private static async Task<PlanInputOutcome> ReadCommittedAsync(
        string path, string expected, string repoPath, string branch, Func<string, string, Task<string>> resolve, IProcessLauncher launcher, CancellationToken ct)
    {
        string tip;
        try
        {
            tip = await resolve(repoPath, branch);
        }
        catch (WorktreeException e)
        {
            return new PlanInputOutcome.Refused(e.Message);
        }

        return expected.Length > 0 && !expected.Equals(tip, StringComparison.OrdinalIgnoreCase)
            ? new PlanInputOutcome.Refused(
                $"branch '{branch}' is at commit {Short(tip)}, not the expectedCommit {Short(expected)} — a commit landed after the plan was written; "
                + "review the tip (leave expectedCommit out), or pass the new commit")
            : Ready(await CommittedPlanReader.ReadGuardedAsync(new GitHistory(launcher), repoPath, tip, path, ct));
    }

    private static PlanInputOutcome Ready(PlanRead read) => read switch
    {
        PlanRead.Accepted a => new PlanInputOutcome.Ready(a.Text, a.Source.Commit, a.Source, SourceLine(a.Source), a.Note.Length > 0 ? "; " + a.Note : string.Empty),
        PlanRead.Refused r => new PlanInputOutcome.Refused(r.Sentence),
        _ => throw new InvalidOperationException("a plan read is Accepted or Refused"),
    };

    /// <summary>The line the prompt carries under its heading — where the text came from, so a reviewer can tell a plan from a pointer.</summary>
    internal static string SourceLine(PlanSource source) =>
        $"(read by the server from {source.Path} at commit {Short(source.Commit)}, blob {Short(source.Blob)}, {source.Bytes} bytes)";

    private static string Short(string id) => id[..Math.Min(12, id.Length)];
}

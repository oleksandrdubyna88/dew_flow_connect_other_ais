namespace CoaiMcp.Server;

/// <summary>A repository the walk found and git confirmed as the top of a work tree, with what git said about it.</summary>
internal sealed record ConfirmedRepository(FoundRepository Repository, RepositoryFacts Facts);

/// <summary>What is left to copy of one root's candidates, and how many linked worktrees were left out.</summary>
internal sealed record WorktreeSelection(IReadOnlyList<ConfirmedRepository> Kept, IReadOnlyList<ConfirmedRepository> Left);

/// <summary>
/// D9 of <c>research/PLAN_vibe_reads_a_folder_of_repositories.md</c>: a parent folder of a person's projects holds, beside each main
/// checkout, every linked worktree an agent made of it (392 of the 405 candidates under the owner's real root) — and each is a full copy
/// of the same project. A LINKED worktree found beneath a granted parent is skipped when its main checkout is itself copied in the
/// same row; it stays when no main is, and when git could not say. A worktree granted by name is never asked about here.
/// </summary>
internal static class LinkedWorktrees
{
    /// <summary>
    /// The candidates to copy: every one that is not a linked worktree with a main checkout in the row, in the order given.
    /// </summary>
    /// <param name="candidates">One root's confirmed candidates, in the walk's order.</param>
    /// <param name="mains">The common-dir keys of every main checkout the row copies — directly granted ones, and those found in any root.</param>
    public static WorktreeSelection Select(IReadOnlyList<ConfirmedRepository> candidates, IReadOnlySet<string> mains)
    {
        var byLeftOut = candidates.ToLookup(candidate => HasItsMainCopied(candidate, mains));

        return new WorktreeSelection([.. byLeftOut[false]], [.. byLeftOut[true]]);
    }

    private static bool HasItsMainCopied(ConfirmedRepository candidate, IReadOnlySet<string> mains) =>
        candidate.Facts.IsLinked && mains.Contains(candidate.Facts.CommonDir);

    /// <summary>The one note, and the sentence the model reads about its copies, when <paramref name="skipped"/> linked worktrees were left out; empty when none was.</summary>
    public static string Sentence(int skipped) => skipped switch
    {
        0 => string.Empty,
        1 => "skipped 1 linked worktree whose main checkout is copied — its branch and uncommitted files were not read; grant a worktree directly to include it",
        _ => $"skipped {skipped} linked worktrees whose main checkouts are copied — their branches and uncommitted files were not read; grant a worktree directly to include it",
    };
}

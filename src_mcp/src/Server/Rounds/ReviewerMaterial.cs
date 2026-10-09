using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What ONE reviewer is told it holds: the checkout only when one is mounted AND this reviewer can read it; otherwise
/// what the stage gives every reviewer without one (research/PLAN_one_model_catalog.md, epic 2, story 1).
/// </summary>
/// <remarks>
/// Decided per reviewer, where the runtime is known. It was decided once per round, so a code round with a worktree
/// told an api row, a local model and a Team server "you have the checkout read-only" — and their findings cited
/// files they had never seen.
/// </remarks>
internal static class ReviewerMaterial
{
    public static ReaderMaterial For(bool hasCheckout, IReviewerRuntime runtime, ReaderMaterial stageReads) =>
        hasCheckout && runtime.ReadsTheCheckout ? ReaderMaterial.Checkout : stageReads;
}

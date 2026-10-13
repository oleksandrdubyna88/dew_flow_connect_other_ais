using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What one round's roster is built from — fixed before the first reviewer is added. It was the set of locals
/// <c>RosterBuilder.BuildWork</c>'s local functions closed over; a record of its own since the method was taken down to
/// the doctrine's cyclomatic complexity of 4 (S2 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>, a pure refactor).
/// </summary>
/// <param name="LaunchDir">Where a review launch starts — the worktree, or an empty folder for the <c>none</c> workspace.</param>
/// <param name="HasCheckout">Whether a reviewer stands in a mounted checkout — not the same question as which folder it was pointed at.</param>
internal sealed record RosterFrame(
    Stage Stage,
    StageDescriptor Row,
    string Context,
    string SchemaFile,
    string OutputDir,
    string RepairDir,
    string LaunchDir,
    bool HasCheckout,
    Runners.Feature.SourceTurns Turns,
    CodexTiers Tiers,
    RoundScratch Scratch)
{
    /// <summary>How many follow-up turns a reviewer of this round may take — the feature stage's source turns, else none.</summary>
    public int FollowUps => Turns is Runners.Feature.SourceTurns.On on ? on.FollowUps : 0;

    /// <summary>
    /// Where the REVIEW launch starts, and whether that is a checkout. <c>none</c> launches a code reviewer in an empty
    /// folder: the prompt is identical either way — the diff came from the repository and the rules from the worktree —
    /// so it removes only the exploring, which is what makes a hosted CLI cost 200k input tokens where a local reviewer
    /// costs 25k. A plan round's <paramref name="worktreePath"/> is an empty scratch folder, so only a MOUNTED worktree is
    /// a checkout — a flag derived from the folder alone would tell a plan reviewer a repository was there.
    /// </summary>
    public static (string LaunchDir, bool HasCheckout) Directories(string codeWorkspace, string worktreePath, bool readsCheckout)
    {
        var fastCode = codeWorkspace == "none" && readsCheckout;

        return (fastCode ? Directory.CreateTempSubdirectory("coai-noworkspace-").FullName : worktreePath, readsCheckout && !fastCode);
    }

    /// <summary>The feature stage's source conversation for a reviewer, or a single turn for every other stage (S3.2).</summary>
    public IReviewerContinuation ContinueFor(Func<string, ReviewerWork> turn) =>
        Turns is Runners.Feature.SourceTurns.On conversation && Stage == Stage.FeatureReview
            ? new Runners.Feature.SourceConversation(conversation, turn)
            : ReviewerContinuation.None;

    /// <summary>
    /// The whole-review limit (the operator's twenty minutes, 2026-09-27): an api reviewer's conversation on the feature
    /// stage, every turn of it — the row's own minutes, else the environment's, else the module's calibrated default. A
    /// CLI reviewer and every other stage keep the derived cap — null — because the limit was measured on hosted models.
    /// </summary>
    public TimeSpan? CapFor(IReviewerRuntime runtime, ApiRowView api) =>
        Stage == Stage.FeatureReview && runtime is ApiRuntime ? TimeSpan.FromMinutes(api.Effective.ReviewMinutes) : null;
}

/// <summary>
/// What one round's roster collects as it is dealt: the reviewers, the roles nobody was asked, the (vendor, role) pairs
/// that could not be given — each said ONCE.
/// </summary>
internal sealed class RosterCollector
{
    private readonly List<ReviewerWork> _work = [];
    private readonly List<SkippedRole> _notAsked = [];
    private readonly List<ExcludedRole> _excluded = [];

    // Sets beside the lists rather than a scan of them: one round can carry a role per vendor per lens, and the scan was
    // the round's own quadratic. (codex, story B2's code round.)
    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    private readonly HashSet<(string Provider, string Role)> _refused = [];

    public void Add(ReviewerWork work) => _work.Add(work);

    /// <summary>
    /// One sentence per ROLE however many vendors would have carried it: a person reading a round needs to know the role
    /// did not run, not that four vendors each did not run it.
    /// </summary>
    public void Skip(string role, string reason)
    {
        if (_skipped.Add(role))
        {
            _notAsked.Add(new SkippedRole(role, reason));
        }
    }

    /// <summary>
    /// And one per (vendor, role), for the same reason one step down: a role dealt four lenses was refused four times in
    /// identical words. (gemini, story B2's code round.)
    /// </summary>
    public void Exclude(string provider, string role, string reason)
    {
        if (_refused.Add((provider, role)))
        {
            _excluded.Add(new ExcludedRole(provider, role, reason));
        }
    }

    /// <summary>The round's work: every local row first (<see cref="RosterBuilder.LocalRowsFirst"/>), the scratch with it.</summary>
    public RoundWork Work(bool ordinaryDue, RoundScratch scratch) =>
        new(RosterBuilder.LocalRowsFirst(_work), _notAsked, _excluded) { OrdinaryDue = ordinaryDue, Scratch = scratch };
}

using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What a reviewer that did not answer looks like written down.
/// </summary>
/// <remarks>
/// <para><b>The page reads the ledger; the round's own note does not survive.</b>
/// <c>ReviewerSummaryFactory.Describe</c> puts a sentence into <c>ReviewerState.Note</c>, which lives
/// in the session file, is rewritten on every tick and is swept. So a person looking at a round with
/// four findings had no way to learn that two reviewers never answered.</para>
///
/// <para><b><c>Describe</c> is USED and never edited.</b> The parent plan forbids it and the reason
/// is an assembly boundary: it lives in <c>runners</c>, and instrumenting it would make that assembly
/// know about the ledger. This class observes the outcome where <see cref="LiveRound"/> already has
/// it and borrows the sentence.</para>
/// </remarks>
internal static class ReviewerNotices
{
    /// <summary>What a reviewer failure is, as a row on the page.</summary>
    private const string Class = "failure";

    /// <summary>Which half of the product said it.</summary>
    private const string Source = "coai-mcp";

    /// <summary>
    /// Every ending that is not <c>Ok</c>, and the code that names it — no switch, no default arm.
    /// </summary>
    /// <remarks>
    /// <para><b>A C# switch over a class hierarchy is NOT exhaustive-checked, and the repository said
    /// otherwise in two places.</b> <c>ReviewerExecutor</c>'s own docstring claims a sixth ending
    /// "cannot appear silently" and the parent plan repeated it; adding a sealed subtype compiles
    /// perfectly well, and a switch would then throw at runtime or fold it into "unknown" — losing
    /// the notice for exactly the ending nobody had thought about. (codex, on story 2.3's plan
    /// round.)</para>
    /// <para>So the map is DATA, and <c>TheReviewerFailuresAreWrittenDownTests</c> compares its keys
    /// against the sealed subtypes reflection finds — the same census
    /// <c>TheRefusalRoadsAreCountedTests</c> already computes for
    /// <c>shared/refusal-sites.json</c>. A sixth subtype is red on the day it is added.</para>
    /// <para>Rejected alternatives, written down: an abstract code-bearing member on
    /// <see cref="ReviewerOutcome"/>, or an enum discriminator beside it. Both edit the type in
    /// <c>runners</c> and make that assembly name a ledger code, which is the boundary this whole
    /// story is arranged around.</para>
    /// </remarks>
    internal static IReadOnlyDictionary<Type, string> ByType { get; } =
        new Dictionary<Type, string>
        {
            [typeof(ReviewerOutcome.TimedOut)] = ServerNoticeCodes.ReviewerTimedOut,
            [typeof(ReviewerOutcome.RateLimited)] = ServerNoticeCodes.ReviewerRateLimited,
            [typeof(ReviewerOutcome.NonZeroExit)] = ServerNoticeCodes.ReviewerExit,
            [typeof(ReviewerOutcome.NotStarted)] = ServerNoticeCodes.ReviewerNotStarted,
            [typeof(ReviewerOutcome.Unparseable)] = ServerNoticeCodes.ReviewerUnparseable,
        };

    /// <summary>Whether this ending is one that gets written down at all.</summary>
    internal static bool IsAFailure(ReviewerOutcome outcome) => ByType.ContainsKey(outcome.GetType());

    /// <summary>
    /// The notice one failed reviewer makes.
    /// </summary>
    /// <remarks>
    /// <para><c>Subject</c> is <c>provider/role</c> — the key <see cref="LiveRound"/> already builds
    /// for its own states. The extension groups repeats on <c>(code, subject)</c>, so the same
    /// reviewer timing out across four rounds is ONE row with a count, and two different reviewers
    /// are two rows. That is the grouping the key was designed for and the first place in this
    /// product where it earns itself.</para>
    /// <para>The title is cut where the record is BUILT, not only where the line is written: a notice
    /// waits in the writer's queue until then, and 256 of them each holding a megabyte is 256 MB of
    /// process held because a share stopped answering. (CodeRabbit, on story 2.2.)</para>
    /// </remarks>
    internal static ServerNotice Of(string provider, string role, ReviewerOutcome outcome) => new()
    {
        Utc = ServerNotice.Iso(DateTimeOffset.UtcNow),
        Class = Class,
        Source = Source,
        Code = ByType[outcome.GetType()],
        Subject = $"{provider}/{role}",
        // The shared helper, since story 2.3.3's second code round: this file cut a megabyte of
        // stderr plainly and `StartupNotices` cut with an ellipsis, so the same overflow was
        // legible in one place and silent in the other.
        Title = ServerNotice.Shortened(ReviewerSummaryFactory.Describe(outcome)),
    };
}

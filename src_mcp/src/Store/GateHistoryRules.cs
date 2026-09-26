using System.Text.RegularExpressions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Git;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Store;

/// <summary>
/// The three rules that tie a round to a piece of work (<c>todo/PLAN_feature_review.md</c> §4.8) —
/// each one evidence, none of them proof.
/// </summary>
/// <remarks>
/// <para>Pure — but for the repository match, which follows links on disk when two spellings differ — so
/// every rule is a unit test rather than a database. The query hands each row here and
/// takes back an <see cref="Admission"/>; a row that gets <see cref="Admission.None"/> is counted as
/// NOT attached when it falls after the base commit, and ignored when it falls before.</para>
/// <para><b>The windows are not the same, on purpose.</b> Rule (a) holds from <c>T0</c>, the base
/// commit's committer time: a commit in <c>base..head</c> cannot have been reviewed before the base
/// existed. Rules (b) and (c) hold from <c>T0 − 90 days</c>: a plan is reviewed before <c>main</c>
/// reaches the base, and a rebase-merged epic is reviewed on its branch before the base commit's
/// committer time — as first written, rule (b) attached NOTHING for 3 of the 5 real features the
/// feature-pack trial ran (2026-09-26).</para>
/// </remarks>
internal static partial class GateHistoryRules
{
    /// <summary>
    /// A trunk, or no branch at all: <c>main</c>, <c>master</c> (also as <c>origin/…</c>,
    /// <c>refs/heads/…</c>, <c>refs/remotes/x/…</c>) and a detached <c>HEAD</c>.
    /// </summary>
    /// <remarks>
    /// Every piece of work in a repository passes through the trunk, so a round on it ties nothing to
    /// THIS work — the trial's first version admitted every trunk round of the window that way. The
    /// real database holds rounds on a branch recorded as <c>HEAD</c> (nine of them on 2026-09-26),
    /// which is a detached checkout rather than a branch anybody could name as an epic's.
    /// </remarks>
    [GeneratedRegex(@"^(?:(?:refs/heads/|refs/remotes/[^/]+/|origin/|upstream/)?(?:main|master)|HEAD)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Trunk { get; }

    internal static bool IsTrunk(string branch) => Trunk.IsMatch(branch.Trim());

    /// <summary>The caller's epic branches that can tie anything: trimmed, non-empty, not a trunk.</summary>
    internal static IReadOnlySet<string> EpicBranchesOf(IReadOnlyList<string> named) =>
        named.Select(b => b.Trim()).Where(b => b.Length > 0 && !IsTrunk(b)).ToHashSet(StringComparer.Ordinal);

    /// <summary>The names the caller gave that were dropped as trunks, for the sentence that says so.</summary>
    internal static IReadOnlyList<string> TrunksNamed(IReadOnlyList<string> named) =>
        [.. named.Select(b => b.Trim()).Where(b => b.Length > 0 && IsTrunk(b)).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Whether two recorded paths are one repository — through the session key's own normalisation.
    /// </summary>
    /// <remarks>
    /// <para><see cref="SessionKey.For"/> already decides what "the same repository" means for every session
    /// this gate keeps: separators unified, the trailing one dropped, case folded. A second spelling of
    /// that rule here would be the second copy that drifts.</para>
    /// <para><b>And links are followed when the spellings differ</b> (<see cref="WorktreePaths.Same"/>):
    /// a consultation records git's answer — the REAL path — while a session records what its caller
    /// typed and the review is asked with the caller's spelling. On macOS those always differ
    /// (<c>/var</c> is a link to <c>/private/var</c>), and every consultation dropped out of the history.
    /// The spelling is asked first, so the common case touches no disk.</para>
    /// </remarks>
    internal static bool SameRepository(string recorded, string asked) =>
        SessionKey.For(recorded, string.Empty) == SessionKey.For(asked, string.Empty)
        || WorktreePaths.Same(recorded, asked);

    /// <summary>
    /// The recorded repository paths that are the one asked about — each distinct spelling resolved once.
    /// </summary>
    /// <remarks>
    /// Resolving links walks the disk component by component, and a window holds many rows of a few
    /// repositories; asking per distinct path keeps that cost to the number of repositories, not rows.
    /// </remarks>
    internal static IReadOnlySet<string> SameRepositoryAmong(IEnumerable<string> recorded, string asked) =>
        recorded.Distinct(StringComparer.Ordinal).Where(path => SameRepository(path, asked)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every rule that admits this round, or <see cref="Admission.None"/>.</summary>
    internal static Admission Admit(CandidateRound row, GateHistoryWork work) =>
        ByBranch(row.Branch, work) | ByCommit(row.HeadSha, row.StartedUtc, work) | ByHeading(row, work);

    /// <summary>Every rule that admits this consultation: its branch, or the commit it was asked at.</summary>
    internal static Admission Admit(CandidateConsultation row, GateHistoryWork work) =>
        ByBranch(row.Branch, work) | ByCommit(row.HeadSha, row.StartedUtc, work);

    /// <summary>Rule (b): an epic's branch — the caller's own claim, and the strongest one there is.</summary>
    private static Admission ByBranch(string branch, GateHistoryWork work) =>
        work.EpicBranches.Contains(branch.Trim()) ? Admission.EpicBranch : Admission.None;

    /// <summary>Rule (a): the commit it read is in <c>base..head</c>, and it ran after the base existed.</summary>
    private static Admission ByCommit(string headSha, string startedUtc, GateHistoryWork work) =>
        work.Range.CommitsKnown
        && string.CompareOrdinal(startedUtc, work.Since) >= 0
        && work.Range.Commits.Contains(headSha.Trim().ToLowerInvariant())
            ? Admission.CommitInRange
            : Admission.None;

    /// <summary>Rule (c): a plan round under this plan's heading.</summary>
    private static Admission ByHeading(CandidateRound row, GateHistoryWork work) =>
        row.Stage == nameof(Stage.PlanReview) && work.Heading.Matches(row.Subject, row.PlanOpening)
            ? Admission.PlanHeading
            : Admission.None;
}

/// <summary>
/// The plan's heading, as rule (c) matches it: the first <c># </c> line, whole and as
/// <see cref="RoundSubject.From"/> would shorten it into a round's subject.
/// </summary>
/// <remarks>
/// <para><b>Looked at on the real database first</b> (2026-09-26, 434 plan rounds): 359 plan texts open
/// with a <c>#</c> line, and 304 subjects are a heading shortened to sixty characters with an
/// ellipsis — so the whole heading can be matched against a text's OPENING line, and only the
/// shortened one against a subject. A plan round for one epic is the plan PREFACED by a paragraph
/// naming the epic (§7.0), whose text therefore does not open with the heading while its subject
/// still is it.</para>
/// <para>A plan with no <c># </c> line has nothing to match by; rule (c) is then off, and said to be.</para>
/// </remarks>
internal sealed record PlanHeading(string Text, string Subject)
{
    public static readonly PlanHeading None = new(string.Empty, string.Empty);

    public bool Known => Text.Length > 0;

    public static PlanHeading Of(string planText)
    {
        var line = planText.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));

        return line is null ? None : new PlanHeading(line[2..].Trim(), RoundSubject.From(line, _ => false));
    }

    /// <summary>Whether a round's subject, or its text's opening line, is this heading.</summary>
    public bool Matches(string subject, string opening) =>
        Known && (subject.Trim() == Subject || HeadingOf(opening) == Text);

    /// <summary>The first non-empty line of a text, as a heading — or empty when it is not one.</summary>
    private static string HeadingOf(string text)
    {
        var first = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

        return first.StartsWith('#') ? first.TrimStart('#').Trim() : string.Empty;
    }
}

/// <summary>What the rules are applied against: the work's range, its epics and its plan heading.</summary>
/// <param name="Since">T0 as stored text — rule (a)'s window, and where "NOT attached" starts counting.</param>
/// <param name="WideSince">T0 − 90 days as stored text — the window of rules (b) and (c).</param>
internal sealed record GateHistoryWork(
    WorkRange Range,
    IReadOnlySet<string> EpicBranches,
    PlanHeading Heading,
    string Since,
    string WideSince);

/// <summary>A round in the window, as read — before any rule has looked at it.</summary>
internal sealed record CandidateRound(
    long Id,
    string SessionId,
    string RepoPath,
    string Branch,
    string Stage,
    int Number,
    string Subject,
    string StartedUtc,
    string HeadSha,
    string PlanOpening);

/// <summary>A consultation in the window, as read.</summary>
internal sealed record CandidateConsultation(
    string RepoPath,
    string Branch,
    string HeadSha,
    string Kind,
    string Status,
    string Outcome,
    string StartedUtc,
    string Problem,
    string Advice);

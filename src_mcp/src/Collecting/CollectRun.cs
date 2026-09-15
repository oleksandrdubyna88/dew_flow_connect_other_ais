using CoaiMcp.Core.Collecting;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Store;

namespace CoaiMcp.Collecting;

/// <summary>What one collector run did, per stage of the funnel.</summary>
/// <param name="Reasons">
/// How many candidates each reason accounted for — the funnel, and the only readable form of a skip
/// rate. A flat percentage lets `language_unsupported` mask `fix_commit_not_found`, and only the
/// second has a decision attached to it.
/// </param>
/// <param name="Lost">
/// Candidates another run had already claimed while this one was deciding them.
/// </param>
public sealed record CollectSummary(
    string RunId = "",
    int Candidates = 0,
    int Collected = 0,
    int Skipped = 0,
    int Failed = 0,
    int Lost = 0,
    IReadOnlyDictionary<string, int>? Reasons = null);

/// <summary>
/// One pass over the candidates: decide what became of each, and write it down.
/// </summary>
/// <remarks>
/// <para>The half of the story that makes the rest of it real. A classifier that never persists
/// leaves every candidate unprocessed in the shipped product however well it classifies, and the
/// plan round said so before a line of it existed. (codex.)</para>
/// <para><b>The run id is stamped on every row the run claims</b>, so a later question — which run
/// skipped these, and under which rules — is a query rather than an afternoon. It is generated here
/// and never by a caller: two runs sharing an id would be indistinguishable in exactly the case the
/// id exists for.</para>
/// <para><b>A run is additive, not a sweep.</b> `collect_state` is text rather than a flag precisely
/// so a later run with a repaired walk can revisit what an earlier one skipped; this takes the
/// unprocessed candidates, and <c>all</c> is how somebody asks for the rest.</para>
/// </remarks>
public sealed class CollectRun(Collector collector, TimeProvider time, TextWriter? progress = null)
{
    /// <summary>Collects every candidate the corpus offers, writing each outcome as it is decided.</summary>
    /// <param name="all">
    /// Revisit candidates an earlier run already handled — what makes a repaired walk worth having.
    /// </param>
    public async Task<CollectSummary> RunAsync(
        string dataDir, RoundsDb db, int limit, bool all = false, CancellationToken ct = default)
    {
        var runId = time.GetUtcNow().UtcDateTime.ToString("yyyyMMddTHHmmss")
            + "-" + Guid.NewGuid().ToString("N")[..6];
        var candidates = BugsQuery.Read(dataDir, limit, all).Candidates;
        var decided = new List<(CollectOutcome Outcome, bool Claimed)>(candidates.Count);

        Say($"collecting {candidates.Count} candidate(s), run {runId}");
        foreach (var (candidate, index) in candidates.Select((c, i) => (c, i)))
        {
            var outcome = await collector.CollectAsync(Ask(candidate), ct);

            // Written per candidate rather than at the end: a run interrupted after forty of fifty has
            // done forty candidates' work, and throwing that away because the fiftieth was still
            // running would make a long run something nobody dares start.
            var claimed = db.RecordCollect(
                candidate.Id, State(outcome.State), outcome.Reason, outcome.FixSha, runId);

            decided.Add((outcome, claimed));
            Say($"  [{index + 1}/{candidates.Count}] {Short(candidate.RepoPath)} {candidate.File}:{candidate.Line}"
                + $" — {State(outcome.State)}{Because(outcome)}{(claimed ? string.Empty : " (claimed elsewhere)")}");
        }

        return Summarise(runId, decided);
    }

    /// <summary>
    /// The tallies, counted once over the outcomes rather than accumulated into as they arrive.
    /// </summary>
    /// <remarks>
    /// The coding-style rule is that objects are created rather than mutated, and the first version
    /// kept two dictionaries and index-assigned into both as it went. Counting at the end reads as
    /// what it is — a projection of the results — and there is nothing to get out of step.
    /// (Code round, codex.)
    /// </remarks>
    private static CollectSummary Summarise(
        string runId, IReadOnlyList<(CollectOutcome Outcome, bool Claimed)> decided) =>
        new(
            runId,
            decided.Count,
            decided.Count(d => d.Outcome.State is CollectState.Collected),
            decided.Count(d => d.Outcome.State is CollectState.Skipped),
            decided.Count(d => d.Outcome.State is CollectState.Failed),
            decided.Count(d => !d.Claimed),
            decided
                .SelectMany(d => d.Outcome.Reasons)
                .GroupBy(reason => reason, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));

    /// <summary>
    /// A line per candidate, on stderr, while the run is happening.
    /// </summary>
    /// <remarks>
    /// A default run is two hundred candidates, each several git subprocesses with a thirty-second
    /// budget; without this the terminal is silent for minutes and a person cannot tell a working run
    /// from a hung one. stdout stays the JSON interface, so the progress goes to stderr — the same
    /// division every other mode here keeps. (Code round, all three reviewers.)
    /// </remarks>
    private void Say(string line) => progress?.WriteLine(line);

    private static string Because(CollectOutcome outcome) =>
        outcome.Reason.Length == 0 ? string.Empty : $": {outcome.Reason}";

    /// <summary>The last path segment — enough to tell two repositories apart in a progress line.</summary>
    private static string Short(string repoPath) =>
        repoPath.Replace('\\', '/').TrimEnd('/').Split('/')[^1];

    private static Candidate Ask(BugCandidate candidate) => new(
        candidate.RepoPath,
        candidate.Branch,
        candidate.HeadSha,
        candidate.File,
        candidate.Line,
        candidate.LaterSha);

    /// <summary>The column's vocabulary, which is lower case and stable across tools.</summary>
    private static string State(CollectState state) => state switch
    {
        CollectState.Collected => "collected",
        CollectState.Skipped => "skipped",
        CollectState.Failed => "failed",
        _ => string.Empty,
    };
}

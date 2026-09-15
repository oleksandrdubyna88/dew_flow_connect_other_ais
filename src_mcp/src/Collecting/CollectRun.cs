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
public sealed record CollectSummary(
    string RunId = "",
    int Candidates = 0,
    int Collected = 0,
    int Skipped = 0,
    int Failed = 0,
    IReadOnlyDictionary<string, int>? Reasons = null);

/// <summary>
/// One pass over the unprocessed candidates: decide what became of each, and write it down.
/// </summary>
/// <remarks>
/// <para>The half of the story that makes the rest of it real. A classifier that never persists
/// leaves every candidate unprocessed in the shipped product however well it classifies, and the
/// plan round said so before a line of it existed. (codex.)</para>
/// <para><b>The run id is stamped on every row the run touches</b>, so a later question — which run
/// skipped these, and under which rules — is a query rather than an afternoon. It is generated here
/// and never by a caller: two runs sharing an id would be indistinguishable in exactly the case the
/// id exists for.</para>
/// <para><b>A run is additive, not a sweep.</b> `collect_state` is text rather than a flag precisely
/// so a later run with a repaired walk can revisit what an earlier one skipped; this one takes the
/// unprocessed candidates, and `--all` is how somebody asks for the rest.</para>
/// </remarks>
public sealed class CollectRun(Collector collector, TimeProvider time)
{
    /// <summary>Collects every candidate the corpus offers, writing each outcome as it is decided.</summary>
    public async Task<CollectSummary> RunAsync(
        string dataDir, RoundsDb db, int limit, CancellationToken ct = default)
    {
        var runId = time.GetUtcNow().UtcDateTime.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        var corpus = BugsQuery.Read(dataDir, limit);
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var counts = new Dictionary<CollectState, int>();

        foreach (var candidate in corpus.Candidates)
        {
            var outcome = await collector.CollectAsync(Ask(candidate), ct);

            // Written per candidate rather than at the end: a run interrupted after forty of fifty
            // has done forty candidates' work, and throwing that away because the fiftieth was still
            // running would make a long run something nobody dares start.
            db.RecordCollect(candidate.Id, State(outcome.State), outcome.Reason, outcome.FixSha, runId);

            counts[outcome.State] = counts.GetValueOrDefault(outcome.State) + 1;
            foreach (var reason in outcome.Reasons)
            {
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            }
        }

        return new CollectSummary(
            runId,
            corpus.Candidates.Count,
            counts.GetValueOrDefault(CollectState.Collected),
            counts.GetValueOrDefault(CollectState.Skipped),
            counts.GetValueOrDefault(CollectState.Failed),
            reasons);
    }

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

using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>
/// Everything one run of the backfill is made of, in the order it happens (E7.S4): read the stored history, gather the facts of every selected round
/// ONCE, PLAN (which transcript bound each caller needs — no byte of a transcript read), then process the rounds gate by gate in the order the gate ran
/// them, each through <see cref="RoundBackfill"/>. The mode wraps this with the backup, the dry run and the summary; this class never opens a store for
/// writing itself — the writes are whatever <see cref="IBackfillWrites"/> it is handed.
/// </summary>
/// <remarks>
/// <para><b>Per gate session in round order</b>, because a round's delta reads its predecessor's <c>Size.Whole.From</c> and its timeline reads each
/// earlier round's caller: <see cref="RoundBackfill"/> gives the journal what it wrote before the next round is measured. Facts are taken before
/// any round is written, so each round is judged by the store as it was SELECTED, and the write validates exactly that.</para>
/// <para>The reader reads each caller session once, at the lowest bound any of its rounds needs (<see cref="CallerReads"/>).</para>
/// </remarks>
internal sealed class BackfillRun(
    PanelSettings settings,
    SessionStore store,
    SqliteConnection read,
    IBackfillWrites writes,
    BackfillCounts counts,
    StoredSizer sizer,
    Serilog.ILogger log,
    DateTime? since,
    Func<CallerTranscriptLocation, ReadBudget, DateTime?, ObservedSession>? readTranscript = null,
    Func<Core.Authors.AttributionInput, Core.Authors.WindowAssignment>? assign = null,
    Func<string, string, PersistedSession?>? load = null,
    TimeProvider? clock = null)
{
    /// <summary>The reader the run used: how many transcripts it actually read.</summary>
    public CallerReads Reads { get; } = new(readTranscript ?? CallerTranscript.Read);

    public async Task RunAsync(CancellationToken ct)
    {
        var attribution = new BackfillAttribution(settings, log, store, Reads, clock ?? TimeProvider.System, assign, load);
        var facts = new StoredFacts(read, settings, attribution);
        var selected = Selected(new StoredHistory(read, store).Gates(), facts);
        Plan(selected, attribution);
        var backfill = new RoundBackfill(read, attribution, sizer, writes, counts, since);
        foreach (var round in selected)
        {
            await backfill.ProcessAsync(round, ct);
        }
    }

    /// <summary>The facts of every settled round at or after <c>--since</c>, gate by gate in the order each gate ran them.</summary>
    private List<StoredRoundFacts> Selected(IEnumerable<StoredGate> gates, StoredFacts facts) =>
    [
        .. gates.SelectMany(gate => gate.Rounds.Where(round => BackfillDecision.IsSelected(round, since)).Select(round => facts.Of(gate, round))),
    ];

    /// <summary>Which transcript bound each caller needs: the probe records it for every round that will be computed and reads nothing.</summary>
    private void Plan(List<StoredRoundFacts> rounds, BackfillAttribution attribution)
    {
        foreach (var found in rounds.Where(round => BackfillDecision.Decide(round.Input(since)).Verb == BackfillVerb.Compute))
        {
            attribution.Compute(found.Before, found.Record, found.CallerId, found.Peers, found.Round.AuthorsIdSource, planning: true);
        }
    }
}

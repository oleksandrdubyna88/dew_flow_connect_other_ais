using System.Text.Json;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// A round that may be SKIPPED, recorded rather than refused: a <c>skipped</c> row on the trail and in
/// the log carrying its reason and the head it would have read, and a session left exactly as it was.
/// </summary>
/// <remarks>
/// <para>Moved out of <see cref="RoundEngine"/> in S2.2b, when a second reason to skip arrived — a plan of
/// fewer epics than <c>COAI_FEATURE_MIN_EPICS</c> (D17) is skipped before anything is built, beside the
/// roster that came back empty (D1). One writer for both, so the two cannot drift in what a skip leaves
/// behind; and the engine, already past the file-size rule, did not grow by the second.</para>
/// <para>No <c>AwaitingResolve</c>, no budget spent, not <c>Done</c> — so the next call, once the reason is
/// gone, runs a real round. The state saved is the one READ, not the one a begin moved to: an
/// <c>again</c> that is skipped leaves the review finished rather than half-reopened.</para>
/// </remarks>
internal sealed class RoundSkips(
    PanelSettings settings,
    Serilog.ILogger log,
    SessionStore store,
    Store.Projection projection,
    Func<Stage, IReadOnlyList<string>> excludedFrom)
{
    /// <summary>What a skipped round says ran — nothing did.</summary>
    private const string NobodyRan = "no reviewer ran";

    /// <summary>Why a stage's roster came back empty, as the sentence a person acts on (§4.4, rows 1–3).</summary>
    internal string NobodyFor(Stage stage, RoundWork work) =>
        SkipReasons.For(
            stage,
            settings.Rounds.Catalog,
            settings.Rounds.EnabledRolesOf(stage).Count,
            settings.Providers.Count(p => p.Serves(stage)),
            excludedFrom(stage),
            work);

    /// <summary>
    /// Why this round is skipped BEFORE anything is built, or empty: the stage's own reason
    /// (<see cref="StageRun.SkipBecause"/>, D17), else — for a stage that records skips — nobody to ask,
    /// read from the settings alone (rows 1–3 of §4.4: the role switch, the vendor ticks, the ticked vendors
    /// that cannot run). A roster empty for a reason only building it can find stays <see cref="NobodyFor"/>'s,
    /// after the work exists.
    /// </summary>
    internal string BeforeBuilding(StageRun stage) =>
        stage.SkipBecause.Length > 0 ? stage.SkipBecause
        : stage.WhenNobody == NobodyPolicy.RecordSkip ? NobodyConfigured(stage.Stage)
        : string.Empty;

    /// <summary>Rows 1–3 from the settings — the same counts <see cref="NobodyFor"/> reads, asked before a pack is built.</summary>
    private string NobodyConfigured(Stage stage)
    {
        var rolesOn = settings.Rounds.EnabledRolesOf(stage).Count;
        var ticked = settings.Providers.Count(p => p.Serves(stage));
        var cannotRun = excludedFrom(stage);

        return rolesOn == 0 || ticked == 0 || cannotRun.Count >= ticked
            ? SkipReasons.For(stage, settings.Rounds.Catalog, rolesOn, ticked, cannotRun, new RoundWork([], []))
            : string.Empty;
    }

    /// <summary>Records the skip and answers it — <c>verdict: skipped</c>, with the instruction that it does not block.</summary>
    /// <remarks>
    /// <b>Numbered from the journal, like every round</b> — the number was allocated under the claim —
    /// except when it COALESCES: a skip for the same reason as the round immediately before it updates
    /// that row's count and completion time instead of appending, which is what keeps a caller retrying
    /// with nobody ticked from growing the trail without an owner (§4.12).
    /// </remarks>
    internal string Record(Stage stage, PersistedSession loaded, int number, string sha, string planText, string reason)
    {
        var phrase = Stages.Of(stage).Phrase;
        log.Warning("the {Stage} was skipped: {Reason}", phrase, reason);

        var (record, rounds) = SkipRecord(stage, loaded, number, sha, reason, RoundSubject.From(planText, File.Exists));
        store.Save(loaded with { Rounds = rounds });
        // Best-effort, as every projection is; `head_sha` comes from the context because that is the
        // column the log reads, and the skip is the one round whose record and context name the same commit.
        projection.Write(db => db.RecordRound(
            loaded.State, record, [], new Store.RoundContext(planText, sha, RoundCommands.CallerFor(loaded))));

        var answer = new ReviewAnswer(
            RoundRecord.Skipped, null, 0, settings.Rounds.For(loaded.State.Stage).Threshold, reason, [], [], [],
            $"The {phrase} did not run: {reason}. This does NOT block the release — tell the person the {phrase} "
            + "did not run, and why. Nothing about this session changed; call it again once the reason is gone.");

        return JsonSerializer.Serialize(answer, ServerJsonContext.Default.ReviewAnswer);
    }

    /// <summary>The skip's record and the trail it lands on: the previous row counted up, or a new row.</summary>
    private static (RoundRecord Record, List<RoundRecord> Rounds) SkipRecord(
        Stage stage, PersistedSession loaded, int number, string sha, string reason, string subject)
    {
        var now = DateTime.UtcNow;
        if (loaded.Rounds is [.., { Verdict: RoundRecord.Skipped } last] && last.Stage == stage.ToString() && last.Note == reason)
        {
            var counted = last with { CompletedUtc = now, Repeats = last.Repeats + 1, Sha = sha };

            return (counted, [.. loaded.Rounds[..^1], counted]);
        }

        var fresh = new RoundRecord(stage.ToString(), number, RoundRecord.Skipped, 0, NobodyRan, now)
        {
            Status = RoundRecord.Done,
            StartedUtc = now,
            RunnerPid = Environment.ProcessId,
            Subject = subject,
            Sha = sha,
            Note = reason,
            Caller = loaded.Caller,
        };

        return (fresh, [.. loaded.Rounds, fresh]);
    }
}

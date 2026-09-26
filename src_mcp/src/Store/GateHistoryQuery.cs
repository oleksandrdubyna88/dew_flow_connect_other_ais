using System.Globalization;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Git;
using CoaiMcp.Runners.Processes;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// The gate's own history of one piece of work — what earlier rounds rejected and why, and what the
/// implementer asked a consultant — attached to a feature review as EVIDENCE, never as proof
/// (<c>todo/PLAN_feature_review.md</c> §4.8, story S2.3).
/// </summary>
/// <remarks>
/// <para><b>There is no work id.</b> Sessions are repository+branch, epics live on their own branches,
/// and a squash merge makes an epic's commits unreachable from <c>main</c>. So membership is argued
/// from three rules (<see cref="GateHistoryRules"/>) over a bounded window, and every item says which
/// rule admitted it. The caller's own <c>epics[].branch</c> is the strongest association and is shown
/// first; the rest is labelled CANDIDATE.</para>
/// <para><b>Read-only, and nothing is written anywhere.</b> The connection is opened
/// <c>Mode=ReadOnly</c> like <see cref="RoundsQuery"/>'s, a missing database is never created, and
/// nothing is seeded into a session's rejections: the history is context for the reviewer, not a
/// discount for the gate.</para>
/// <para><b>A failure is one sentence, never a throw.</b> The history is one section of a round's
/// context; a database that will not open, or a base commit git cannot date, costs the reviewer that
/// section and nothing else.</para>
/// </remarks>
public static class GateHistoryQuery
{
    /// <summary>The widest <c>base..head</c> rule (a) reads; past it rule (a) is off, and said to be.</summary>
    public const int MaxCommits = 5000;

    /// <summary>How far before the base commit rules (b) and (c) look — plan §4.8, widened by the trial.</summary>
    public static readonly TimeSpan PlanWindow = TimeSpan.FromDays(90);

    /// <summary>The history, rendered for the feature review's context — at most <see cref="GateHistoryText.MaxBytes"/>.</summary>
    public static async Task<string> RenderAsync(GateHistoryAsk ask, IProcessLauncher launcher, CancellationToken ct = default) =>
        GateHistoryText.Render(await ReadAsync(ask, launcher, ct));

    /// <summary>The history itself: git first (T0 and <c>base..head</c>), then the database.</summary>
    public static Task<GateHistory> ReadAsync(GateHistoryAsk ask, IProcessLauncher launcher, CancellationToken ct = default) =>
        ReadAsync(ask, launcher, MaxCommits, ct);

    internal static async Task<GateHistory> ReadAsync(
        GateHistoryAsk ask, IProcessLauncher launcher, int maxCommits, CancellationToken ct)
    {
        var range = await new WorkRangeReader(launcher).ReadAsync(ask.RepoPath, ask.BaseSha, ask.HeadSha, maxCommits, ct);

        return Query(ask, range);
    }

    /// <summary>The history for a range already read from git.</summary>
    public static GateHistory Query(GateHistoryAsk ask, WorkRange range)
    {
        if (!range.TimeKnown)
        {
            return GateHistory.Failed($"Gate history unavailable: {OneLine(range.Trouble)}");
        }

        var file = Path.Combine(ask.DataDir, RoundsDb.FileName);

        return File.Exists(file)
            ? Guarded(file, ask, range)
            : GateHistory.Failed($"Gate history unavailable: there is no rounds database at {file}.");
    }

    /// <remarks>
    /// ANY exception, as <see cref="RoundsDb.Open"/> catches any: the contract is that a history which
    /// cannot be read changes nothing about the round, and a malformed file or a locked one throws
    /// types nobody would think to list. The sentence carries the reason so it is not a silent loss.
    /// </remarks>
    private static GateHistory Guarded(string file, GateHistoryAsk ask, WorkRange range)
    {
        try
        {
            using var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
            db.Open();

            return Read(db, ask, WorkOf(ask, range));
        }
        catch (Exception e)
        {
            return GateHistory.Failed($"Gate history unavailable: the rounds database could not be read ({OneLine(e.Message)}).");
        }
    }

    private static GateHistoryWork WorkOf(GateHistoryAsk ask, WorkRange range) => new(
        range,
        GateHistoryRules.EpicBranchesOf(ask.EpicBranches),
        PlanHeading.Of(ask.PlanText),
        Stamp(range.BaseTime),
        Stamp(range.BaseTime - PlanWindow));

    private static GateHistory Read(SqliteConnection db, GateHistoryAsk ask, GateHistoryWork work)
    {
        var inWindow = Rounds(db, work.WideSince)
            .Where(r => r.SessionId != ask.ExcludeSessionId && GateHistoryRules.SameRepository(r.RepoPath, ask.RepoPath))
            .Select(r => (Row: r, Admission: GateHistoryRules.Admit(r, work)))
            .ToList();
        var attached = Ordered(inWindow.Where(r => r.Admission != Admission.None).Select(r => Attached(r.Row, r.Admission)));
        var found = attached.SelectMany(round => Rejections(db, round)).ToList();

        return new GateHistory(
            string.Empty,
            attached,
            Deduplicated(found),
            found.Count,
            Consultations(db, ask, work),
            inWindow.Count(r => r.Admission == Admission.None && string.CompareOrdinal(r.Row.StartedUtc, work.Since) >= 0),
            Notes(ask, work));
    }

    /// <summary>Epics first, then plan rounds before code, then newest first.</summary>
    private static List<AttachedRound> Ordered(IEnumerable<AttachedRound> rounds) =>
        [.. rounds
            .OrderBy(r => r.FromAnEpic ? 0 : 1)
            .ThenBy(r => r.Stage == nameof(Stage.PlanReview) ? 0 : 1)
            .ThenByDescending(r => r.StartedUtc, StringComparer.Ordinal)
            .ThenByDescending(r => r.Number)];

    private static AttachedRound Attached(CandidateRound r, Admission admission) =>
        new(r.Id, r.SessionId, r.Branch, r.Stage, r.Number, r.Subject, r.StartedUtc, admission);

    /// <summary>
    /// The same remark rejected again is shown once, counted, with its NEWEST reason.
    /// </summary>
    /// <remarks>
    /// "The same remark" is the gate's own wording test, <see cref="TextSimilarity.SameRemark"/>, over
    /// the title — within one file, because the line a remark was anchored to moves from round to round
    /// while the disagreement stays. The rejections arrive already in display order, newest first, so
    /// the first of a group is the one kept.
    /// </remarks>
    private static List<HistoryRejection> Deduplicated(List<HistoryRejection> ordered)
    {
        var kept = new List<HistoryRejection>();
        foreach (var rejection in ordered)
        {
            var at = kept.FindIndex(k => SameRejection(k, rejection));
            if (at < 0)
            {
                kept.Add(rejection);
                continue;
            }

            kept[at] = kept[at] with { Times = kept[at].Times + 1 };
        }

        return kept;
    }

    private static bool SameRejection(HistoryRejection a, HistoryRejection b) =>
        string.Equals(a.File.Replace('\\', '/'), b.File.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
        && TextSimilarity.SameRemark(a.Title, b.Title);

    private static List<string> Notes(GateHistoryAsk ask, GateHistoryWork work)
    {
        var notes = new List<string>();
        if (!work.Range.CommitsKnown)
        {
            notes.Add($"Rule (a) is off: {OneLine(work.Range.Trouble)}");
        }

        if (!work.Heading.Known)
        {
            notes.Add("Rule (c) is off: the plan has no '# ' heading to match a plan round by.");
        }

        return [.. notes, .. TrunkNote(GateHistoryRules.TrunksNamed(ask.EpicBranches))];
    }

    private static IEnumerable<string> TrunkNote(IReadOnlyList<string> trunks) => trunks.Count == 0
        ? []
        : [$"Not treated as epic branches: {string.Join(", ", trunks)} — every piece of work passes through them, so they tie nothing to this one."];

    // ----------------------------------------------------------------------------------------------
    // SQL — every value bound, every column read by name.
    // ----------------------------------------------------------------------------------------------

    private static List<CandidateRound> Rounds(SqliteConnection db, string since)
    {
        using var read = db.CreateCommand();
        // The opening of the plan text only: rule (c) reads its first line, and a plan is tens of KB.
        read.CommandText = """
            SELECT r.id, r.session_id, r.stage, r.number, r.subject, r.started_utc, r.head_sha,
                   substr(r.plan_text, 1, 400) AS plan_opening,
                   COALESCE(s.repo_path, '') AS repo_path, COALESCE(s.branch, '') AS branch
            FROM rounds r LEFT JOIN sessions s ON s.id = r.session_id
            WHERE r.started_utc >= $since
            ORDER BY r.started_utc DESC, r.id DESC
            """;
        read.Parameters.AddWithValue("$since", since);
        using var rows = read.ExecuteReader();
        var found = new List<CandidateRound>();
        while (rows.Read())
        {
            found.Add(new CandidateRound(
                Columns.Id(rows, "id"), Columns.Text(rows, "session_id"), Columns.Text(rows, "repo_path"),
                Columns.Text(rows, "branch"), Columns.Text(rows, "stage"), Columns.Number(rows, "number"),
                Columns.Text(rows, "subject"), Columns.Text(rows, "started_utc"), Columns.Text(rows, "head_sha"),
                Columns.Text(rows, "plan_opening")));
        }

        return found;
    }

    private static List<HistoryRejection> Rejections(SqliteConnection db, AttachedRound round)
    {
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT severity, category, file, line, title, reason FROM findings
            WHERE round_id = $round AND resolution = 'reject'
            ORDER BY ordinal
            """;
        read.Parameters.AddWithValue("$round", round.Id);
        using var rows = read.ExecuteReader();
        var found = new List<HistoryRejection>();
        while (rows.Read())
        {
            found.Add(new HistoryRejection(
                round, Columns.Text(rows, "severity"), Columns.Text(rows, "category"), Columns.Text(rows, "file"),
                Columns.Number(rows, "line"), Columns.Text(rows, "title"), Columns.Text(rows, "reason")));
        }

        return found;
    }

    private static List<HistoryConsultation> Consultations(SqliteConnection db, GateHistoryAsk ask, GateHistoryWork work) =>
        [.. CandidateConsultations(db, work.WideSince)
            .Where(c => GateHistoryRules.SameRepository(c.RepoPath, ask.RepoPath))
            .Select(c => (Row: c, Admission: GateHistoryRules.Admit(c, work)))
            .Where(c => c.Admission != Admission.None)
            .Select(c => new HistoryConsultation(
                c.Row.Kind, c.Row.Branch, c.Row.Status, c.Row.Outcome, c.Row.StartedUtc, c.Row.Problem, c.Row.Advice, c.Admission))
            .OrderBy(c => c.FromAnEpic ? 0 : 1)
            .ThenByDescending(c => c.StartedUtc, StringComparer.Ordinal)];

    /// <remarks>
    /// Asked of the schema first, as <see cref="RoundsQuery"/> asks: this read is read-only and cannot
    /// migrate, and a file last written by an older binary may have no consultations table, no
    /// <c>outcome</c> (step 9) or no <c>kind</c> (step 15). Absent reads as the column's own default.
    /// </remarks>
    private static List<CandidateConsultation> CandidateConsultations(SqliteConnection db, string since)
    {
        if (!RoundsQuery.HasColumn(db, "consultations", "branch"))
        {
            return [];
        }

        using var read = db.CreateCommand();
        read.CommandText = $"""
            SELECT repo_path, branch, head_sha, status, started_utc, problem, advice,
                   {(RoundsQuery.HasColumn(db, "consultations", "outcome") ? "outcome" : "'' AS outcome")},
                   {(RoundsQuery.HasColumn(db, "consultations", "kind") ? "kind" : "'stuck' AS kind")}
            FROM consultations WHERE started_utc >= $since
            """;
        read.Parameters.AddWithValue("$since", since);
        using var rows = read.ExecuteReader();
        var found = new List<CandidateConsultation>();
        while (rows.Read())
        {
            found.Add(new CandidateConsultation(
                Columns.Text(rows, "repo_path"), Columns.Text(rows, "branch"), Columns.Text(rows, "head_sha"),
                Columns.Text(rows, "kind"), Columns.Text(rows, "status"), Columns.Text(rows, "outcome"),
                Columns.Text(rows, "started_utc"), Columns.Text(rows, "problem"), Columns.Text(rows, "advice")));
        }

        return found;
    }

    /// <summary>An instant as the store writes one (<c>"O"</c>, UTC), so text comparison orders it.</summary>
    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string OneLine(string text) =>
        string.Join(' ', text.Split((char[])['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}

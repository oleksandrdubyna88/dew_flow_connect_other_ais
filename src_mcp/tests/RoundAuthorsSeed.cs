using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Tests;

/// <summary>
/// Seeds a store for the round-authors report through the REAL writers (E6.S1): <c>RoundsDb.RecordRound</c> with the record's size, gitlinks
/// and authors, and <c>RecordDecisions</c> for the resolution — never a hand-written <c>INSERT</c> (a fixture the code rejects proves nothing).
/// Hand SQL exists for exactly one thing, <see cref="OlderFile"/>: a database stopped at an OLDER schema step, found by content.
/// </summary>
internal static class RoundAuthorsSeed
{
    public static readonly DateTime T0 = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The stored spelling of an instant, the column's own.</summary>
    public static string Stamp(DateTime utc) => utc.ToString("O");

    /// <summary>One reviewer row that answered.</summary>
    public static ReviewerState Done(string provider, string model, string effort = "", string role = "Architecture") =>
        new(provider, role, ReviewerState.Done, 0, "", 1.0, model, effort);

    /// <summary>A finding of a given severity; the title is unique so two findings are never one defect.</summary>
    public static Finding Found(string title, Severity severity = Severity.Major) =>
        new(severity, Category.Architecture, "src/A.cs", 1, title, title + " - why", "fix", ["codex"]);

    public static RoundsDb Open(string dataDir) => RoundsDb.Open(dataDir, Serilog.Core.Logger.None)!;

    /// <summary>Records one round and, when asked, the caller's decisions on its findings — the order the product writes them.</summary>
    /// <param name="reRaised">The ordinals of <paramref name="findings"/> the gate discounted as repeats.</param>
    /// <param name="accept">The ordinals the caller accepted; <paramref name="reject"/> the ones it argued with. Either one makes the round resolved.</param>
    /// <param name="resolve">Resolve with no decision at all (a round with no finding the caller closed).</param>
    public static void Round(
        RoundsDb db,
        string session,
        string repo,
        string branch,
        string stage,
        int number,
        DateTime started,
        string status = RoundRecord.Done,
        string verdict = "proceed",
        IReadOnlyList<ReviewerState>? reviewers = null,
        IReadOnlyList<Finding>? findings = null,
        RoundSize? size = null,
        int? gitlinks = null,
        AuthorsSnapshot? authors = null,
        int gating = 0,
        IReadOnlyList<int>? accept = null,
        IReadOnlyList<int>? reject = null,
        IReadOnlyList<int>? reRaised = null,
        bool resolve = false)
    {
        var all = findings ?? [];
        var record = new RoundRecord(stage, number, verdict, gating, "answered", started.AddMinutes(1))
        {
            StartedUtc = started,
            Status = status,
            ReviewerStates = [.. reviewers ?? []],
            Size = size,
            Gitlinks = gitlinks,
            Authors = authors,
        };
        var context = new RoundContext(ReRaised: [.. (reRaised ?? []).Select(at => all[at])]);
        var state = new SessionState(session, repo, branch, new PanelConfig()) { Stage = Enum.Parse<Stage>(stage) };
        db.RecordRound(state, record, all, context);
        Decide(db, session, stage, number, all, accept ?? [], reject ?? [], resolve);
    }

    private static void Decide(RoundsDb db, string session, string stage, int number, IReadOnlyList<Finding> all, IReadOnlyList<int> accept, IReadOnlyList<int> reject, bool resolve)
    {
        if (accept.Count + reject.Count == 0 && !resolve)
        {
            return;
        }

        db.RecordDecisions(
            session,
            stage,
            number,
            [.. accept.Select(at => DecisionAt.Accept(all, at)!), .. reject.Select(at => DecisionAt.Reject(all, at, "no")!)]);
    }

    /// <summary>The index of a schema step found by its CONTENT, never a literal — <c>Schema.TheRoundsSize</c>, <c>ThePlanSource</c>, <c>TheRoundsAuthors</c>.</summary>
    public static int StepIndex(string step)
    {
        var at = Array.IndexOf(Schema.Steps, step);
        if (at < 0)
        {
            throw new InvalidOperationException("the step is not in Schema.Steps");
        }

        return at;
    }

    /// <summary>
    /// A database stopped BEFORE schema step <paramref name="stopBefore"/> (an index into <c>Schema.Steps</c>), with <paramref name="sql"/>
    /// run on it — the <c>RoundCountsCliScenarioTests</c> shape. Its <c>user_version</c> is the number of steps that ran.
    /// </summary>
    public static void OlderFile(string dataDir, int stopBefore, string sql)
    {
        using (var make = new SqliteConnection($"Data Source={Path.Combine(dataDir, RoundsDb.FileName)};Pooling=False"))
        {
            make.Open();
            using var run = make.CreateCommand();
            run.CommandText = string.Join(";\n", Schema.Steps[..stopBefore]) + $"; PRAGMA user_version={stopBefore};" + sql;
            run.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>A session row and one code round with one reviewer, as hand SQL an older file needs.</summary>
    public static string OlderRound(string session, string repo, string branch, DateTime started, string reviewerColumns = "", string reviewerValues = "") =>
        $"INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('{session}', '{repo}', '{branch}', '{Stamp(started)}');"
        + $"INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, '{session}', 'CodeReview', 1, 'done', 'proceed', '{Stamp(started)}', '{Stamp(started.AddMinutes(1))}');"
        + $"INSERT INTO reviewers (round_id, provider, role, status{reviewerColumns}) VALUES (1, 'codex', 'Architecture', 'done'{reviewerValues});";

    /// <summary>Sets a column of every row of a table by raw SQL — for the one case a writer refuses to write (a malformed stored state).</summary>
    public static void Raw(string dataDir, string sql)
    {
        using (var open = new SqliteConnection($"Data Source={Path.Combine(dataDir, RoundsDb.FileName)};Pooling=False"))
        {
            open.Open();
            using var run = open.CreateCommand();
            run.CommandText = sql;
            run.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>A whole-scope size with the given cells and a recorded state; the delta is unavailable as a first round's is.</summary>
    public static RoundSize Whole(params SizeCell[] cells) =>
        new(new ScopeSize(ScopeState.Recorded, "base1", [.. cells]), ScopeSize.Unavailable(ScopeState.FirstRound));

    /// <summary>A recorded whole AND a recorded delta — a corrective round's size.</summary>
    public static RoundSize WithDelta(SizeCell[] whole, params SizeCell[] delta) =>
        new(new ScopeSize(ScopeState.Recorded, "base1", [.. whole]), new ScopeSize(ScopeState.Recorded, "sha1", [.. delta]));

    public static SizeCell Cell(string coverage, string kind, int added, int removed = 0, int files = 1) => new(coverage, kind, files, added, removed);

    public static SizeCell Binary(int files) => new(ChangeCoverage.Binary, "unknown", files, null, null);

    /// <summary>An observed snapshot with the given rows for a round — the caller session is fixed so contributors line up.</summary>
    public static AuthorsSnapshot Snapshot(string stage, int number, string state = ObservationKinds.Observed, string reason = "", int uncovered = 0, params AuthorRow[] rows) =>
        new(stage, number, AuthorsSnapshotFixtures.Caller, state, reason.Length == 0 ? [] : [reason], [.. rows],
            ActivityTotals.Zero, ActivityTotals.Zero, ActivityTotals.Zero, 0, [], [], CallerSessionId.Registry, 1, 1, 0, uncovered);

    /// <summary>One author row — main (agent <c>main</c>) or a subagent, in a window, with its association and write calls.</summary>
    public static AuthorRow Author(string agent, string model, string association = Association.WroteHere, string window = WindowKind.Round, string stage = "CodeReview", int number = 1, int writeCalls = 1, string requested = "opus", string effort = "high", int launches = 1) =>
        new(window, stage, number, agent == AgentKey.MainId ? "main" : "subagent", new AgentKey(agent), agent == AgentKey.MainId ? string.Empty : "general-purpose",
            requested, model, effort, association, launches, 2, new TokenTerms(1, 1, 1, 1), writeCalls);

    public static ImmutableArray<string> NoReasons => [];
}

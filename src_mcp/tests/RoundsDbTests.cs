using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every finding, and what was decided about it, survives the round that produced it.
/// </summary>
/// <remarks>
/// <para>Until this existed, nothing on the machine kept a finding's text. The session file records
/// that codex produced four findings; the four sentences went into the reply and then nowhere. So
/// the log page could only ever show counts, and the operator's question on 2026-09-05 — "мы тут
/// пишем сами находки в бд?" — had the honest answer "no, and there is no database".</para>
/// <para>Real SQLite over a temp directory, no fakes: the point of the test is that the SQL runs.</para>
/// </remarks>
public sealed class RoundsDbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-db-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    // The `why` follows the title unless a test says otherwise: findings that all say the same
    // thing would make a search for one of them find all of them, which is a test that proves
    // nothing about the index.
    private static Finding Found(string title, string why = "", Severity severity = Severity.Major) =>
        new(severity, Category.Reliability, "src/Panel.cs", 40, title,
            why.Length > 0 ? why : title + ", and here is why", "and here is the fix for " + title, ["codex"])
        {
            Role = "SecurityReliability",
        };

    private static RoundRecord Round(int number = 1) =>
        new("CodeReview", number, "proceed", 2, "all 3 reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-4),
            Subject = "SCOPE — the cost column",
            TokensIn = 48_397,
            TokensOut = 3_296,
            ReviewerStates =
            [
                new ReviewerState("codex", "Architecture", ReviewerState.Done, 2, "", 23.4),
                new ReviewerState("gemini", "SecurityReliability", ReviewerState.Done, 1, "", 41.0),
            ],
        };

    [Fact]
    public void AFindingIsKeptWithItsWords_NotOnlyItsCount()
    {
        using var db = RoundsDb.Open(_dir, _log)!;

        db.RecordRound(Session, Round(), [Found("session file opened without FileShare"), Found("the retry never gives up")]);

        var rows = Query("SELECT title, why, fix, severity, file, line, providers, is_gating FROM findings ORDER BY ordinal");
        rows.Should().HaveCount(2);
        rows[0]["title"].Should().Be("session file opened without FileShare");
        rows[0]["why"].Should().Be("session file opened without FileShare, and here is why");
        rows[0]["fix"].Should().Be("and here is the fix for session file opened without FileShare");
        rows[0]["file"].Should().Be("src/Panel.cs");
        rows[0]["line"].Should().Be("40");
        rows[0]["providers"].Should().Be("codex");
        rows[0]["is_gating"].Should().Be("1");
    }

    [Fact]
    public void TheRoundAndItsReviewersAreThereToo()
    {
        using var db = RoundsDb.Open(_dir, _log)!;

        db.RecordRound(Session, Round(), [Found("one")]);

        var round = Query("SELECT stage, number, verdict, gating, tokens_in, subject FROM rounds").Single();
        round["stage"].Should().Be("CodeReview");
        round["verdict"].Should().Be("proceed");
        round["tokens_in"].Should().Be("48397");
        round["subject"].Should().Be("SCOPE — the cost column");
        Query("SELECT provider, seconds FROM reviewers ORDER BY provider").Should().HaveCount(2);
        Query("SELECT repo_path, branch FROM sessions").Single()["branch"].Should().Be("feat/x");
    }

    [Fact]
    public void ADecisionLandsOnTheFindingItWasAbout_ByTheNumberResolveUsed()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var findings = new[] { Found("first"), Found("second"), Found("third") };
        db.RecordRound(Session, Round(), findings);

        db.RecordDecisions("s1", "CodeReview", 1,
        [
            new DecisionAt(0, new Decision.Accepted(findings[0])),
            new DecisionAt(1, new Decision.Rejected(findings[1], "the branch is behind main; verified absent from all three refs")),
            new DecisionAt(2, new Decision.Accepted(findings[2])),
        ]);

        var rows = Query("SELECT title, resolution, reason FROM findings ORDER BY ordinal");
        rows[0]["resolution"].Should().Be("accept");
        rows[1]["resolution"].Should().Be("reject");
        rows[1]["reason"].Should().Be("the branch is behind main; verified absent from all three refs");
        rows[1]["title"].Should().Be("second", "a decision must land on the finding it was made about");
        rows[2]["resolution"].Should().Be("accept");
    }

    /// <summary>
    /// The same guarantee as the test above, for a caller that does not resolve top to bottom.
    /// </summary>
    /// <remarks>
    /// <para>`resolve` takes entries carrying a finding INDEX — <c>[{"finding": 2, ...}]</c> — and
    /// nothing anywhere requires them to arrive in the order the round listed them.
    /// <c>RoundMachine.Resolve</c> checks that rejections carry reasons and counts nothing, so an
    /// out-of-order set reaches the database exactly as an in-order one does.</para>
    /// <para>The test above passes them in order, which is why this went unnoticed: the loop index
    /// and the finding's ordinal are the same number for as long as nobody does anything else.</para>
    /// </remarks>
    [Fact]
    public void ADecisionSentOutOfOrder_StillLandsOnItsOwnFinding()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var findings = new[] { Found("first"), Found("second"), Found("third") };
        db.RecordRound(Session, Round(), findings);

        // Finding 2 decided FIRST, and finding 1 not decided at all: the position in this list and
        // the number the decision was made by disagree, which is the whole point.
        db.RecordDecisions("s1", "CodeReview", 1,
        [
            new DecisionAt(2, new Decision.Rejected(findings[2], "the third one reads the base ref, which this branch never moved")),
            new DecisionAt(0, new Decision.Accepted(findings[0])),
        ]);

        var rows = Query("SELECT title, resolution FROM findings ORDER BY ordinal");
        rows[0]["resolution"].Should().Be("accept", "the caller accepted 'first', whichever order it said so in");
        rows[1]["resolution"].Should().Be("", "the caller said nothing at all about 'second'");
        rows[2]["resolution"].Should().Be("reject", "the caller rejected 'third', whichever order it said so in");
    }

    /// <summary>
    /// A caller that decides SOME of a round's findings marks those and leaves the rest alone.
    /// </summary>
    /// <remarks>
    /// Partial sets are not hypothetical: <c>PanelService.Resolve</c> refuses an EMPTY list while
    /// findings are pending, and lets a shorter-than-pending one through. Read by position, a set of
    /// one lands on ordinal 0 whichever finding it was actually about.
    /// </remarks>
    [Fact]
    public void APartialResolve_LeavesTheFindingsNobodyMentionedUndecided()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var findings = new[] { Found("first"), Found("second"), Found("third") };
        db.RecordRound(Session, Round(), findings);

        db.RecordDecisions("s1", "CodeReview", 1, [new DecisionAt(1, new Decision.Accepted(findings[1]))]);

        var rows = Query("SELECT title, resolution FROM findings ORDER BY ordinal");
        rows[0]["resolution"].Should().Be("", "nobody decided 'first'");
        rows[1]["resolution"].Should().Be("accept", "'second' is the one the caller accepted");
        rows[2]["resolution"].Should().Be("", "nobody decided 'third'");
    }

    [Fact]
    public void AnUndecidedFindingSaysSo_RatherThanReadingAsRejected()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        db.RecordRound(Session, Round(), [Found("nobody decided this one")]);

        Query("SELECT resolution FROM findings").Single()["resolution"].Should().BeEmpty();
    }

    [Fact]
    public void TheSameRoundRecordedTwiceIsOneRound()
    {
        // A round that answered but failed to save is re-run, and the re-run must not double it.
        using var db = RoundsDb.Open(_dir, _log)!;

        db.RecordRound(Session, Round(), [Found("one"), Found("two")]);
        db.RecordRound(Session, Round() with { Verdict = "revise" }, [Found("one again")]);

        Query("SELECT id FROM rounds").Should().HaveCount(1);
        Query("SELECT verdict FROM rounds").Single()["verdict"].Should().Be("revise");
        Query("SELECT title FROM findings").Should().ContainSingle().Which["title"].Should().Be("one again");
    }

    [Fact]
    public void FindingsAreSearchableByWhatTheySay()
    {
        // The whole point of a database rather than a folder of JSON: "every finding that ever
        // mentioned FileShare" is one query.
        using var db = RoundsDb.Open(_dir, _log)!;
        db.RecordRound(Session, Round(), [Found("session file opened without FileShare"), Found("a name could be clearer")]);

        var hits = Query("SELECT title FROM findings WHERE id IN (SELECT rowid FROM findings_fts WHERE findings_fts MATCH 'FileShare')");

        hits.Should().ContainSingle().Which["title"].Should().Be("session file opened without FileShare");
    }

    [Fact]
    public void ASearchIndexStaysInStepWhenARoundIsRewritten()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        db.RecordRound(Session, Round(), [Found("session file opened without FileShare")]);

        db.RecordRound(Session, Round(), [Found("something else entirely", "about a wholly different line")]);

        Query("SELECT title FROM findings WHERE id IN (SELECT rowid FROM findings_fts WHERE findings_fts MATCH 'FileShare')")
            .Should().BeEmpty("the deleted finding must leave the index too");
    }

    [Fact]
    public void ARoundRecordedWithNoContextAtAll_StillLands()
    {
        // `default(RoundContext)` runs no field initialiser, so its strings are null while the
        // columns are NOT NULL. A caller with nothing to say about the round must still record it.
        using var db = RoundsDb.Open(_dir, _log)!;

        db.RecordRound(Session, Round(), [Found("one")]);

        Query("SELECT plan_text, head_sha, caller, accepted FROM rounds").Single()["accepted"]
            .Should().Be("-1", "nobody has closed this gate yet, which is not the same as accepting nothing");
    }

    [Fact]
    public void TheGateClosureIsCounted_AcceptedAndRejected()
    {
        // What the operator asked for: when the AI closes a gate, record how many it took and how
        // many it argued with. An accepted finding is a blind spot the AI admitted.
        using var db = RoundsDb.Open(_dir, _log)!;
        var findings = new[] { Found("a"), Found("b"), Found("c") };
        db.RecordRound(Session, Round(), findings);

        db.RecordDecisions("s1", "CodeReview", 1, Decisions.InOrder(
            new Decision.Accepted(findings[0]),
            new Decision.Rejected(findings[1], "verified absent from all three refs"),
            new Decision.Rejected(findings[2], "same subject as the one above")));

        var round = Query("SELECT accepted, rejected FROM rounds").Single();
        round["accepted"].Should().Be("1");
        round["rejected"].Should().Be("2");
    }

    [Fact]
    public void AFindingTheCallerAlreadyRejected_IsMarkedAsRaisedAgain()
    {
        // The interesting kind of disagreement: the caller rejected it with a reason, the rejection
        // still stands, and a reviewer raised it anyway.
        using var db = RoundsDb.Open(_dir, _log)!;
        var standing = Found("session file opened without FileShare");

        db.RecordRound(Session, Round(2), [standing, Found("something new")],
            new RoundContext("SCOPE — whatever", "7133c2f", "claude-code", [standing]));

        var rows = Query("SELECT title, re_raised FROM findings ORDER BY ordinal");
        rows[0]["re_raised"].Should().Be("1");
        rows[1]["re_raised"].Should().Be("0");
    }

    [Fact]
    public void TheScopeAndTheCommitAreKeptWithTheRound()
    {
        // A finding cannot be read back against the thing it was about without them.
        using var db = RoundsDb.Open(_dir, _log)!;

        db.RecordRound(Session, Round(), [Found("one")],
            new RoundContext("SCOPE — the cost column", "7133c2f", "claude-code"));

        var round = Query("SELECT plan_text, head_sha, caller FROM rounds").Single();
        round["plan_text"].Should().Be("SCOPE — the cost column");
        round["head_sha"].Should().Be("7133c2f");
        round["caller"].Should().Be("claude-code");
    }

    [Fact]
    public void WhatTheAgentWasDoingIsKeptWithTheRoundItPrecedes()
    {
        // The operator's framing: the stretch between one gate and the next belongs to the gate it
        // ends at, so a finding can be read against what was being done when it was missed.
        using var db = RoundsDb.Open(_dir, _log)!;

        db.RecordRound(Session, Round(), [Found("one")],
            new RoundContext("SCOPE", "7133c2f", "claude-code", [], "[{\"utc\":\"2026-09-05T13:20:00Z\",\"kind\":\"assistant\",\"said\":\"writing the code\"}]"));

        Query("SELECT agent_log FROM rounds").Single()["agent_log"].Should().Contain("writing the code");
    }

    [Fact]
    public void AFileFromAnEarlierBuild_IsBroughtUpToWhatThisOneNeeds()
    {
        // CREATE TABLE IF NOT EXISTS is not a migration: it creates nothing when the table is there,
        // so a column added later would be missing for ever on a file an older build made — and a
        // best-effort writer would swallow the error every time.
        using (var first = RoundsDb.Open(_dir, _log)!)
        {
            first.RecordRound(Session, Round(), [Found("one")]);
        }

        using var again = RoundsDb.Open(_dir, _log)!;
        again.RecordRound(Session, Round(2), [Found("two")]);

        Query("PRAGMA user_version").Single().Values.Single().Should().NotBe("0", "the file records how far it has come");
        Query("SELECT number FROM rounds ORDER BY number").Should().HaveCount(2);
    }

    [Fact]
    public async Task AWriterThatMeetsAnother_WaitsInsteadOfLosingTheRound()
    {
        // The five-window case, made to happen on purpose. Another server holds a write transaction;
        // without a busy timeout SQLite answers SQLITE_BUSY at once, and a best-effort write swallows
        // it — the record then silently loses exactly the rounds that were busiest.
        using (var first = RoundsDb.Open(_dir, _log)!)
        {
            first.RecordRound(Session, Round(), [Found("already here")]);
        }

        using var holder = new SqliteConnection($"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        holder.Open();
        Execute(holder, "BEGIN IMMEDIATE");

        var waiting = Task.Run(() =>
        {
            using var second = RoundsDb.Open(_dir, _log)!;
            second.RecordRound(Session, Round(2), [Found("written while the other held the file")]);
        });
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Execute(holder, "COMMIT");

        await waiting; // it must have waited, not thrown
        Query("SELECT number FROM rounds ORDER BY number").Should().HaveCount(2);
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // ---------- which AI called the round, and which model it declared (issue #174) ----------

    [Fact]
    public void WhoCalledTheRound_IsRecordedBesideWhatItFound()
    {
        using var db = RoundsDb.Open(_dir, _log)!;

        db.RecordRound(
            Session,
            Round() with { Caller = new CallerDeclaration("codex", "codex", "7.3.1", "codex-astra") },
            [Found("one")]);

        var row = Query("SELECT caller_vendor, caller_client, caller_client_version, caller_model FROM rounds").Single();
        row["caller_vendor"].Should().Be("codex");
        row["caller_client"].Should().Be("codex");
        row["caller_client_version"].Should().Be("7.3.1");
        row["caller_model"].Should().Be("codex-astra");
    }

    /// <summary>
    /// Three states, kept apart: a model, a caller that declared none, and a round never asked.
    /// </summary>
    /// <remarks>
    /// The second code round insisted on the third one. A round recorded before these columns
    /// existed was never asked the question; a caller that could not be identified is
    /// <c>unknown</c>. Collapsing them would make every historical round claim an unknown caller it
    /// never had — so the columns carry an empty vendor for "never asked" and the word for the
    /// other, and the round's own field is nullable rather than defaulted.
    /// </remarks>
    [Fact]
    public void ARoundNeverAskedWhoCalledIt_RecordsNothing_WhileAnUnidentifiedOneSaysUnknown()
    {
        using var db = RoundsDb.Open(_dir, _log)!;

        db.RecordRound(Session, Round(), [Found("one")]);
        db.RecordRound(Session, Round(2) with { Caller = new CallerDeclaration() }, [Found("two")]);

        var rows = Query("SELECT number, caller_vendor, caller_model FROM rounds ORDER BY number");
        rows[0]["caller_vendor"].Should().BeEmpty("this round was never asked, which is not a caller we failed to identify");
        rows[0]["caller_model"].Should().BeEmpty();
        rows[1]["caller_vendor"].Should().Be("unknown", "this one was asked and could not be identified");
        rows[1]["caller_model"].Should().BeEmpty("and it declared no model, which a default would have claimed for it");
    }

    /// <summary>
    /// A database written by a build that had never heard of these columns keeps working.
    /// </summary>
    /// <remarks>
    /// <para>Raised as Blocking by codex on the plan round, and it is the one thing about this
    /// change that could be silently wrong: <c>CREATE TABLE IF NOT EXISTS</c> creates nothing when
    /// the table is already there, so four columns added to that statement would be missing on
    /// every file an older build made — and the writer is best-effort, so it would swallow the
    /// error every round for ever.</para>
    /// <para>The file here is built from the DDL an older build actually wrote, spelled out rather
    /// than taken from <c>Schema</c>: a migration test that reads the current constant would follow
    /// it forward and stop testing the thing it was written for.</para>
    /// </remarks>
    [Fact]
    public void ADatabaseFromBeforeTheseColumns_GainsThem_AndItsOwnRoundsStillRead()
    {
        Directory.CreateDirectory(_dir);
        using (var older = new SqliteConnection($"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False"))
        {
            older.Open();
            Execute(older, OldSchema);
            Execute(older,
                """
                INSERT INTO sessions (id, repo_path, branch, opened_utc)
                VALUES ('s0', 'D:/repo', 'feat/old', '2026-09-01T00:00:00Z');
                INSERT INTO rounds (session_id, stage, number, status, verdict, started_utc, completed_utc)
                VALUES ('s0', 'CodeReview', 1, 'done', 'proceed', '2026-09-01T00:00:00Z', '2026-09-01T00:04:00Z');
                """);
            Execute(older, "PRAGMA user_version=2");
        }

        using var db = RoundsDb.Open(_dir, _log);

        db.Should().NotBeNull("a file this build cannot open is a log nobody can read again");
        db!.RecordRound(
            Session,
            Round(2) with { Caller = new CallerDeclaration("claude", "claude-code", "7.3.1", "claude-opus-5") },
            [Found("written by the new build")]);

        var rows = Query("SELECT number, caller_vendor, caller_model FROM rounds ORDER BY number");
        rows.Should().HaveCount(2);
        rows[0]["caller_vendor"].Should().BeEmpty("the old round never had one, and inventing it would be a lie about it");
        rows[0]["caller_model"].Should().BeEmpty();
        rows[1]["caller_model"].Should().Be("claude-opus-5");
    }

    /// <summary>
    /// A step that fails part-way leaves NEITHER its schema change nor its version bump behind.
    /// </summary>
    /// <remarks>
    /// <para>The defect-reproducing test CodeRabbit asked for on the pull request, and it was right
    /// that <c>TheMigrationRunsOnce…</c> does not detect a missing transaction — that one passes
    /// whether or not the step and the bump are atomic.</para>
    /// <para>What it protects: an <c>ALTER TABLE ADD COLUMN</c> is not idempotent the way
    /// <c>CREATE TABLE IF NOT EXISTS</c> is, and <see cref="RoundsDb.Open"/> answers null to ANY
    /// exception. A half-applied step would therefore re-run on the next open, answer
    /// <c>duplicate column name</c>, and leave a database that never opens again — with nothing in
    /// this process able to repair it.</para>
    /// <para>Proved by INJECTING a step that cannot exist in the real schema: a good alter followed
    /// by a statement that does not parse. Every real step succeeds, so the property cannot be
    /// observed any other way.</para>
    /// </remarks>
    [Fact]
    public void AStepThatFailsPartWay_LeavesNothingOfItselfBehind()
    {
        Directory.CreateDirectory(_dir);
        using var db = new SqliteConnection($"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        db.Open();

        var halfBad = new[]
        {
            "CREATE TABLE IF NOT EXISTS t (a TEXT);",
            "ALTER TABLE t ADD COLUMN b TEXT; THIS IS NOT SQL;",
        };
        var migrating = () => RoundsDb.Migrate(db, halfBad);

        migrating.Should().Throw<SqliteException>("the step is broken, and a broken step must be loud");
        Query("PRAGMA user_version").Single().Values.Single().Should().Be(
            "1", "the first step committed; the second must have left the version exactly where it was");
        Query("SELECT name FROM pragma_table_info('t')").Select(r => r["name"])
            .Should().Equal(["a"], "the column its first statement added is rolled back with it");
    }

    [Fact]
    public void TheMigrationRunsOnce_SoASecondOpenIsNotASecondAlter()
    {
        using (var first = RoundsDb.Open(_dir, _log)!)
        {
            first.RecordRound(Session, Round(), [Found("one")]);
        }

        // A repeated ALTER answers `duplicate column name`, and `Open` swallows everything and
        // returns null — which would take the whole log down on the second start, not the first.
        using var again = RoundsDb.Open(_dir, _log);

        again.Should().NotBeNull();
        // From the STEP LIST, not a literal: every schema step anybody adds moves this number, and a
        // hard-coded one turns their migration into this test's failure.
        Query("PRAGMA user_version").Single().Values.Single()
            .Should().Be(Schema.Steps.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The `rounds` and `sessions` tables exactly as the build before #174 wrote them.</summary>
    private const string OldSchema = """
        CREATE TABLE sessions (
            id          TEXT PRIMARY KEY,
            repo_path   TEXT NOT NULL,
            branch      TEXT NOT NULL,
            opened_utc  TEXT NOT NULL
        );

        CREATE TABLE rounds (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id    TEXT NOT NULL REFERENCES sessions(id),
            stage         TEXT NOT NULL,
            number        INTEGER NOT NULL,
            subject       TEXT NOT NULL DEFAULT '',
            status        TEXT NOT NULL,
            verdict       TEXT NOT NULL,
            gating        INTEGER NOT NULL DEFAULT 0,
            started_utc   TEXT NOT NULL,
            completed_utc TEXT NOT NULL,
            tokens_in     INTEGER NOT NULL DEFAULT 0,
            tokens_out    INTEGER NOT NULL DEFAULT 0,
            cost_usd      REAL,
            plan_text     TEXT NOT NULL DEFAULT '',
            head_sha      TEXT NOT NULL DEFAULT '',
            caller        TEXT NOT NULL DEFAULT '',
            accepted      INTEGER NOT NULL DEFAULT -1,
            rejected      INTEGER NOT NULL DEFAULT -1,
            agent_log     TEXT NOT NULL DEFAULT '',
            UNIQUE (session_id, stage, number)
        );

        CREATE TABLE reviewers (
            round_id  INTEGER NOT NULL REFERENCES rounds(id),
            provider  TEXT NOT NULL,
            role      TEXT NOT NULL,
            status    TEXT NOT NULL,
            findings  INTEGER NOT NULL DEFAULT 0,
            seconds   REAL NOT NULL DEFAULT 0,
            note      TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE findings (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            round_id     INTEGER NOT NULL REFERENCES rounds(id),
            ordinal      INTEGER NOT NULL,
            severity     TEXT NOT NULL DEFAULT '',
            category     TEXT NOT NULL DEFAULT '',
            file         TEXT NOT NULL DEFAULT '',
            line         INTEGER NOT NULL DEFAULT 0,
            title        TEXT NOT NULL DEFAULT '',
            why          TEXT NOT NULL DEFAULT '',
            fix          TEXT NOT NULL DEFAULT '',
            role         TEXT NOT NULL DEFAULT '',
            is_gating    INTEGER NOT NULL DEFAULT 0,
            providers    TEXT NOT NULL DEFAULT '',
            resolution   TEXT NOT NULL DEFAULT '',
            reason       TEXT NOT NULL DEFAULT '',
            resolved_utc TEXT NOT NULL DEFAULT '',
            re_raised    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (round_id, ordinal)
        );

        CREATE INDEX rounds_by_time        ON rounds (started_utc DESC);
        CREATE INDEX findings_by_round     ON findings (round_id);
        CREATE INDEX findings_re_raised    ON findings (re_raised, resolution);

        CREATE VIRTUAL TABLE findings_fts USING fts5 (
            title, why, fix, file, content='findings', content_rowid='id'
        );

        CREATE TRIGGER findings_ai AFTER INSERT ON findings BEGIN
            INSERT INTO findings_fts (rowid, title, why, fix, file)
            VALUES (new.id, new.title, new.why, new.fix, new.file);
        END;

        CREATE TRIGGER findings_ad AFTER DELETE ON findings BEGIN
            INSERT INTO findings_fts (findings_fts, rowid, title, why, fix, file)
            VALUES ('delete', old.id, old.title, old.why, old.fix, old.file);
        END;

        CREATE TRIGGER findings_au AFTER UPDATE ON findings BEGIN
            INSERT INTO findings_fts (findings_fts, rowid, title, why, fix, file)
            VALUES ('delete', old.id, old.title, old.why, old.fix, old.file);
            INSERT INTO findings_fts (rowid, title, why, fix, file)
            VALUES (new.id, new.title, new.why, new.fix, new.file);
        END;
        """;

    [Fact]
    public void ReRecordingARound_KeepsTheDecisionsAlreadyMadeAboutItsFindings()
    {
        // It deleted and re-inserted, so a round written again after its gate was closed took the
        // resolutions with it. Caught by the code gate, 2026-09-05.
        using var db = RoundsDb.Open(_dir, _log)!;
        var findings = new[] { Found("first"), Found("second") };
        db.RecordRound(Session, Round(), findings);
        db.RecordDecisions("s1", "CodeReview", 1, Decisions.InOrder(
            new Decision.Accepted(findings[0]), new Decision.Rejected(findings[1], "not this time")));

        db.RecordRound(Session, Round() with { Verdict = "revise" }, findings);

        var rows = Query("SELECT resolution, reason FROM findings ORDER BY ordinal");
        rows[0]["resolution"].Should().Be("accept");
        rows[1]["reason"].Should().Be("not this time");
    }

    [Fact]
    public void ARoundThatNowHasFewerFindings_DoesNotKeepTheOldExtraOnes()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        db.RecordRound(Session, Round(), [Found("one"), Found("two"), Found("three")]);

        db.RecordRound(Session, Round(), [Found("only one now")]);

        Query("SELECT title FROM findings").Should().ContainSingle();
    }

    [Fact]
    public void ADecisionFollowsTheDEFECT_NotThePositionItHadInOneReply()
    {
        // An ordinal is where a finding stood in one reply. If a re-run puts a different finding
        // there, the previous decision must not stay attached to it: a rejection shown against a
        // defect nobody rejected is worse than losing the record of it. Raised by the code gate.
        using var db = RoundsDb.Open(_dir, _log)!;
        var first = new[] { Found("session file opened without FileShare"), Found("the retry never gives up") };
        db.RecordRound(Session, Round(), first);
        db.RecordDecisions("s1", "CodeReview", 1, Decisions.InOrder(
            new Decision.Rejected(first[0], "already handled"), new Decision.Accepted(first[1])));

        // The same round, run again, with a different finding first.
        db.RecordRound(Session, Round(), [Found("something else entirely"), first[1]]);

        var rows = Query("SELECT title, resolution, reason FROM findings ORDER BY ordinal");
        rows[0]["resolution"].Should().BeEmpty("this is not the finding that was rejected");
        rows[0]["reason"].Should().BeEmpty();
        rows[1]["resolution"].Should().Be("accept", "and this one is still the same defect");
    }

    [Fact]
    public void ADatabaseThatCannotBeOpenedIsNotAnException()
    {
        // Every caller's correct behaviour is to carry on without one: a round is what somebody is
        // waiting for, and this is only a record of it.
        var file = Path.Combine(_dir, "in-the-way");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(file, "not a directory");

        RoundsDb.Open(Path.Combine(file, "nested"), _log).Should().BeNull();
    }

    private List<Dictionary<string, string>> Query(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_dir, RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var reader = read.ExecuteReader();
        var rows = new List<Dictionary<string, string>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, string>();
            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = reader.IsDBNull(column) ? string.Empty : reader.GetValue(column).ToString() ?? string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing test.
        }
    }
}

/// <summary>
/// What the counter is allowed to call "accepted earlier", and the column it lands in.
/// </summary>
/// <remarks>
/// Real SQLite over a temp directory, no fakes, for the reason <c>RoundsDbTests</c> states: the point
/// is that the SQL runs. The scoping is the part worth pinning — a question asked one row too wide
/// counts coincidences, and a number that counts coincidences is worse than no number at all when
/// what it decides is whether a feature should fire on its own.
/// </remarks>
public sealed class ConsultMissedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-missed-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private static SessionState Session(string id = "s1") =>
        new(id, "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static RoundRecord Round(string stage, int number) =>
        new(stage, number, "revise", 1, "all 3 reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-3),
        };

    private static Finding Found(string title, string file = "src/Parser.cs", int line = 40) =>
        new(Severity.Major, Category.Reliability, file, line, title, title + " — why", "the fix", ["codex"]);

    /// <summary>A round, its findings, and what the caller decided about each of them.</summary>
    /// <remarks>
    /// The decisions are positional — `resolve` numbers findings by their order in the round — so
    /// they are built from the findings here rather than by a caller repeating them.
    /// </remarks>
    private static void Recorded(RoundsDb db, SessionState session, RoundRecord round, Finding[] findings, string?[] reasons)
    {
        db.RecordRound(session, round, findings);
        db.RecordDecisions(
            session.SessionId, round.Stage, round.Number,
            [.. findings.Select((finding, at) => reasons[at] is { } reason
                ? new Decision.Rejected(finding, reason)
                : (Decision)new Decision.Accepted(finding))]);
    }

    [Fact]
    public void OnlyDecidedFindings_FromEARLIERRoundsOfTheSAMEStageAndSession_Travel()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var session = Session();

        // Round 1, code: one accepted, one rejected.
        Recorded(db, session, Round("CodeReview", 1),
            [Found("the parser drops the last token"), Found("the lock is late", file: "src/Lock.cs")],
            [null, "out of scope"]);

        // A PLAN round of the same session, accepted — a different stage, and its remarks have no file.
        Recorded(db, session, Round("PlanReview", 1), [Found("the plan says nothing about rollback", file: "")],
            [null]);

        // Another session entirely, accepted.
        var other = Session("s2");
        Recorded(db, other, Round("CodeReview", 1), [Found("someone else's finding")], [null]);

        var decided = db.DecidedEarlier(session.SessionId, "CodeReview", 2);

        // Both decisions of the code round travel — the rejection is what keeps a defect the caller
        // is DEFENDING out of the count when it comes back — and nothing from another stage or
        // another session does.
        decided.Select(one => (one.Finding.Title, one.Accepted, one.Round)).Should().Equal(
            [("the parser drops the last token", true, 1), ("the lock is late", false, 1)]);
    }

    /// <summary>
    /// A decision comes back as the WHOLE finding, not the fields today's predicate reads.
    /// </summary>
    /// <remarks>
    /// The undercount this guards against is silent by construction: widen
    /// <see cref="FindingDedup.SameDefect"/> to read the remark and every row out of here would
    /// compare an empty string against it, matching nothing and counting nothing, with no test
    /// failing and no line in any log. (codex, third code round.)
    /// </remarks>
    [Fact]
    public void ADecisionComesBackWithTheWholeFinding_NotOnlyWhatTheRuleReadsToday()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var session = Session();
        Recorded(db, session, Round("CodeReview", 1), [Found("the parser drops the last token")], [null]);

        var decided = db.DecidedEarlier(session.SessionId, "CodeReview", 2);

        decided.Should().ContainSingle();
        decided[0].Finding.Why.Should().Be("the parser drops the last token — why");
        decided[0].Finding.Fix.Should().Be("the fix");
    }

    /// <summary>
    /// The log reader addresses every value by NAME. No ordinal, anywhere.
    /// </summary>
    /// <remarks>
    /// <para>This defect happened rather than being imagined: a merge put four caller columns into
    /// the middle of the rounds SELECT and every read after them shifted by four, so the consult
    /// counter read a vendor string. Nothing objects — <c>GetInt32(14)</c> is valid whatever sits at
    /// 14 — and the symptom is a wrong number on a page, not an exception.</para>
    /// <para>A behavioural test cannot catch the NEXT one, because the defect is a person editing a
    /// SELECT list and the ordinals are then whatever that edit made them. What can be pinned is the
    /// property that makes such an edit harmless: nothing is read positionally. Names do not
    /// renumber. (The operator, on the architecture.)</para>
    /// <para>Asserted over the whole file and over the COUNT of name-addressed reads as well, so it
    /// cannot go green by matching nothing the day the reader is renamed or moved.</para>
    /// </remarks>
    [Fact]
    public void TheLogReaderAddressesEveryValueByName_NeverByOrdinal()
    {
        var source = File.ReadAllText(ReaderSource());

        var positional = System.Text.RegularExpressions.Regex.Matches(
            source, @"rows\.(?:Get\w+|IsDBNull)\(\s*\d").Select(m => m.Value).ToList();

        positional.Should().BeEmpty(
            "a column read by ordinal moves when somebody inserts a column, silently and with the "
            + "wrong VALUE rather than an error — read it by name instead");

        // And the file really is the reader: if this stops matching, the assertion above is vacuous.
        System.Text.RegularExpressions.Regex.Matches(source, @"(?:Text|Number|Big|Real|MaybeReal|NumberOr)\(rows, ")
            .Count.Should().BeGreaterThan(20, "this is the file that reads the log");
    }

    /// <summary>The reader's own source, found from this test assembly rather than a guessed path.</summary>
    private static string ReaderSource()
    {
        var here = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(here); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src_mcp", "src", "Store", "RoundsQuery.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"RoundsQuery.cs was not found above {here}");
    }

    [Fact]
    public void AFindingFromTheSAMERound_IsNotEarlierThanItself()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var session = Session();
        Recorded(db, session, Round("CodeReview", 2), [Found("the parser drops the last token")], [null]);

        db.DecidedEarlier(session.SessionId, "CodeReview", 2).Should().BeEmpty();
    }

    [Fact]
    public void AnUNRESOLVEDFinding_IsNotAnAcceptedOne()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var session = Session();
        // Recorded, never resolved — which is a real state: the round ran and the caller has not answered.
        db.RecordRound(session, Round("CodeReview", 1), [Found("the parser drops the last token")]);

        db.DecidedEarlier(session.SessionId, "CodeReview", 2).Should().BeEmpty();
    }

    [Fact]
    public void TheCounterLandsOnTheRound_AndIsMinusOneUntilSomethingCountsIt()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var session = Session();
        db.RecordRound(session, Round("CodeReview", 1), [Found("the parser drops the last token")]);

        Missed(1).Should().Be(-1, "a round nobody measured is not a round where nothing survived");

        db.RecordConsultMissed(session.SessionId, "CodeReview", 1, 2);

        Missed(1).Should().Be(2);
        RoundsQuery.Read(_dir).Rounds.Single().ConsultMissed.Should().Be(2, "the log carries it too");
    }

    private long Missed(int number)
    {
        using var read = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        read.Open();
        using var ask = read.CreateCommand();
        ask.CommandText = "SELECT consult_missed FROM rounds WHERE number = $number";
        ask.Parameters.AddWithValue("$number", number);

        return (long)ask.ExecuteScalar()!;
    }
}

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
            new Decision.Accepted(findings[0]),
            new Decision.Rejected(findings[1], "the branch is behind main; verified absent from all three refs"),
            new Decision.Accepted(findings[2]),
        ]);

        var rows = Query("SELECT title, resolution, reason FROM findings ORDER BY ordinal");
        rows[0]["resolution"].Should().Be("accept");
        rows[1]["resolution"].Should().Be("reject");
        rows[1]["reason"].Should().Be("the branch is behind main; verified absent from all three refs");
        rows[1]["title"].Should().Be("second", "a decision must land on the finding it was made about");
        rows[2]["resolution"].Should().Be("accept");
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

        db.RecordDecisions("s1", "CodeReview", 1,
        [
            new Decision.Accepted(findings[0]),
            new Decision.Rejected(findings[1], "verified absent from all three refs"),
            new Decision.Rejected(findings[2], "same subject as the one above"),
        ]);

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

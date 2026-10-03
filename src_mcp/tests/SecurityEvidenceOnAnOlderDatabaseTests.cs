using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The read-only history readers (<c>--log</c>, <c>--findings</c>, the batch read and the defended list)
/// keep answering for a database whose last writer predates schema step 18.
/// </summary>
/// <remarks>
/// <para>Those readers open the file <c>Mode=ReadOnly</c>, so the schema steps never run for them: a data
/// directory last written by a release before the security lane has no <c>findings.security_evidence</c>
/// column, and a query that NAMES that column fails with "no such column" for the whole page. The state is
/// MADE rather than spelled, as <see cref="ThePairsThemselvesTests"/> makes it: record a round on today's
/// schema, then drop the column and stamp the file back to sixteen steps.</para>
/// <para>The positive control comes first in each test — the same finding read back WITH its evidence
/// before the column goes — so a null afterwards is about the missing column and not about a fixture that
/// never carried any evidence.</para>
/// </remarks>
public sealed class SecurityEvidenceOnAnOlderDatabaseTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-sec-old-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private const string Title = "Tenant filter removed from the invoice query";

    /// <summary>A lane finding as the lane really produces it: attributed, capped, with one sighting.</summary>
    private static Finding LaneFinding() => SecurityEvidence.Attribute(
        new Finding(Severity.Major, Category.Security, "src/Invoices.cs", 12, Title,
            "Any caller reads another tenant's rows", "Restore the tenant predicate", ["qwen"])
        {
            AttackEvidence = new("A request naming tenant 2", "The tenant predicate is gone", "Tenant 2's invoices returned"),
        },
        "qwen", "redteam-authz");

    /// <summary>
    /// Round 2 raises the finding again over round 1's rejection and is rejected once more, so the same
    /// row is reachable through every read: one round, the batch, the inline log and the defended list.
    /// </summary>
    private void RecordDefendedLaneFinding()
    {
        var found = LaneFinding();
        Finding[] raised = [found];
        using var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!;
        db.RecordRound(Session, Round(2), raised, new RoundContext("SCOPE", "7133c2f", "claude-code", [found]));
        db.RecordDecisions("s1", "CodeReview", 2, [Decisions.Reject(raised, 0, "the predicate moved to the repository")]);
    }

    private static RoundRecord Round(int number) =>
        new("CodeReview", number, "proceed", 0, "all reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-4),
        };

    private void Execute(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var write = db.CreateCommand();
        write.CommandText = sql;
        write.ExecuteNonQuery();
    }

    private bool StillHasEvidenceColumn()
    {
        using var db = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        return RoundsQuery.HasColumn(db, "findings", "security_evidence");
    }

    [Fact]
    public void A_database_from_before_the_lane_still_reads_every_finding_with_no_security_projection()
    {
        RecordDefendedLaneFinding();
        RoundsQuery.FindingsOf(_dir, "s1", "CodeReview", 2).Findings.Should().ContainSingle()
            .Which.SecurityEvidence.Should().NotBeNull("the positive control: on today's schema the evidence is there");

        Execute("""
            ALTER TABLE findings DROP COLUMN security_evidence;
            PRAGMA user_version = 17;
            """);
        SqliteConnection.ClearAllPools();

        var one = RoundsQuery.FindingsOf(_dir, "s1", "CodeReview", 2);
        one.Known.Should().BeTrue();
        one.Findings.Should().ContainSingle().Which.Should().Match<LoggedFinding>(f =>
            f.Title == Title && f.Resolution == "reject" && f.SecurityEvidence == null);

        RoundsQuery.FindingsOfMany(_dir, [new RoundKeyAsked("s1", "CodeReview", 2)]).Rounds.Should().ContainSingle()
            .Which.Findings.Should().ContainSingle().Which.SecurityEvidence.Should().BeNull();

        var log = RoundsQuery.Read(_dir, withFindings: true);
        log.Rounds.Should().ContainSingle().Which.Findings.Should().ContainSingle()
            .Which.SecurityEvidence.Should().BeNull("the inline (pre-paging) log reads the same rows");
        log.Defended.Should().ContainSingle().Which.Should().Match<LoggedFinding>(f =>
            f.Title == Title && f.ReRaised && f.SecurityEvidence == null);

        StillHasEvidenceColumn().Should().BeFalse(
            "the readers are read-only and must not have migrated the file — otherwise this proved nothing about the old shape");
    }

    [Fact]
    public void A_damaged_security_projection_leaves_the_round_readable_and_says_the_evidence_was_lost()
    {
        RecordDefendedLaneFinding();
        RoundsQuery.FindingsOf(_dir, "s1", "CodeReview", 2).Findings.Should().ContainSingle()
            .Which.SecurityEvidence.Should().NotBeNull("the positive control: the undamaged projection reads back")
            .And.Match<SecurityFindingDetails>(d => d.Unreadable.Length == 0, "an intact projection is not reported as lost");

        Execute("UPDATE findings SET security_evidence = '{not json';");
        SqliteConnection.ClearAllPools();

        RoundsQuery.FindingsOf(_dir, "s1", "CodeReview", 2).Findings.Should().ContainSingle()
            .Which.Should().Match<LoggedFinding>(f => f.Title == Title && f.SecurityEvidence != null
                && f.SecurityEvidence.Unreadable.Contains("could not be read") && f.SecurityEvidence.Reproduction == null);
        var log = RoundsQuery.Read(_dir, withFindings: true);
        log.Rounds.Should().ContainSingle().Which.Findings.Should().ContainSingle()
            .Which.SecurityEvidence!.Unreadable.Should().Contain("could not be read");
        log.Defended.Should().ContainSingle().Which.SecurityEvidence!.Unreadable.Should().Contain("could not be read");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

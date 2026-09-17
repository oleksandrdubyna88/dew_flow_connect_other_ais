using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// <c>admin_audit</c>: exact times about administrators, nothing about contributors, bounded.
/// </summary>
/// <remarks>
/// <para><b>The privacy boundary, pinned.</b> Two providers raised it independently: this table
/// records exact times, so what may appear in <c>action</c> and <c>target</c> has to be a closed
/// list, and the wording of the promise has to scope itself to contributors honestly. <c>action</c>
/// is a verb from <see cref="AuditAction"/>, <c>target</c> is a key id, and the row carries neither
/// the note, nor the key, nor its hash. The server never holds a contributor's identity — only key
/// ids — and this is what keeps that true of the one table with a clock.</para>
/// <para><b>The bound is crossed</b>, twice: over a small bound through the seam, so exactly the
/// newest rows can be named; and over the REAL bound from a seeded table, so the constant is
/// exercised and not admired.</para>
/// </remarks>
public sealed class TheAuditTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-audit-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly FrozenClock _clock = new(new DateTimeOffset(2026, 9, 17, 12, 30, 15, TimeSpan.Zero));

    public TheAuditTests() => Directory.CreateDirectory(_dir);

    private string DbPath => Path.Combine(_dir, "coai-bugs.db");

    private Corpus Open() => Corpus.Open(DbPath);

    private Audit Cli() => Audit.By(AdminIdentity.Cli, _clock);

    private static string AnId() => Guid.NewGuid().ToString("N")[..16];

    [Fact]
    public void IssuingAndRevokingAreAuditedWithExactTimes()
    {
        using var corpus = Open();
        var id = AnId();

        corpus.Issue(id, Corpus.HashOf("the-key", "secret"), "for the tuesday workshop", Cli());
        _clock.Advance(TimeSpan.FromMinutes(7));
        corpus.Revoke(id, Cli()).Should().BeTrue();

        corpus.AuditTrail(10).Should().SatisfyRespectively(
            issued =>
            {
                issued.AdminId.Should().Be(AdminIdentity.Cli);
                issued.Action.Should().Be("issue");
                issued.Target.Should().Be(id);
                issued.AtUtc.Should().Be("2026-09-17T12:30:15.0000000Z", "an exact time — this is a log about an administrator");
            },
            revoked =>
            {
                revoked.Action.Should().Be("revoke");
                revoked.Target.Should().Be(id);
                revoked.AtUtc.Should().Be("2026-09-17T12:37:15.0000000Z");
            });
    }

    [Fact]
    public void ARevokeThatChangedNothingLeavesNoRow()
    {
        using var corpus = Open();
        var id = AnId();
        corpus.Issue(id, "hash", string.Empty, Cli());
        corpus.Revoke(id, Cli()).Should().BeTrue();

        corpus.Revoke(id, Cli()).Should().BeFalse("already revoked");
        corpus.Revoke("no-such-key", Cli()).Should().BeFalse();

        corpus.AuditCount().Should().Be(2, "there was no mutation to leave unaudited");
    }

    /// <summary>What the two free-text columns may hold, and what they must never hold.</summary>
    [Fact]
    public void TheAuditHoldsVerbsAndKeyIdsAndNeverANoteAKeyOrAHash()
    {
        const string Note = "for the tuesday workshop";
        const string Key = "a-contributor-key-that-must-not-be-written";
        var hash = Corpus.HashOf(Key, "secret");
        using var corpus = Open();
        var id = AnId();
        corpus.Issue(id, hash, Note, Cli());
        corpus.Revoke(id, Cli());

        var verbs = Enum.GetValues<AuditAction>().Select(action => action.Word()).ToArray();
        var rows = corpus.AuditTrail(10);

        rows.Should().HaveCount(2);
        foreach (var row in rows)
        {
            verbs.Should().Contain(row.Action, "action is a verb from the closed set, derived from the enum rather than retyped");
            row.Target.Should().MatchRegex("^[0-9a-f]{16}$", "target is a key id and nothing else");
            var everything = string.Join('\n', row.AdminId, row.Action, row.Target, row.AtUtc);
            everything.Should().NotContain(Note, "the note is OUR record of why a key exists, and stays on the key");
            everything.Should().NotContain(Key, "the key is printed once and stored nowhere");
            everything.Should().NotContain(hash, "nor its hash");
        }
    }

    /// <summary>Across a small bound, exactly the newest rows survive — by id, and by what they name.</summary>
    [Fact]
    public void TheSweepKeepsExactlyTheNewestRows()
    {
        using var corpus = Open();
        var ids = Enumerable.Range(0, 7).Select(_ => AnId()).ToList();
        foreach (var id in ids)
        {
            corpus.Issue(id, "hash-" + id, string.Empty, Cli());
        }

        corpus.SweepAudit(keep: 5);

        var kept = corpus.AuditTrail(10);
        kept.Should().HaveCount(5);
        kept.Select(row => row.Id).Should().Equal([3, 4, 5, 6, 7], "the two oldest went, and ids are not renumbered");
        kept.Select(row => row.Target).Should().Equal(ids.Skip(2), "the newest five actions, in order");
    }

    /// <summary>The REAL bound, crossed by the production path over a seeded table.</summary>
    /// <remarks>
    /// Fifty thousand rows seeded in one transaction through a plain connection; then two real
    /// issuances, each of which runs the sweep inside its own transaction. The table ends exactly at
    /// the bound, the two oldest seeded rows are gone, and the two newest rows are the issuances.
    /// </remarks>
    [Fact]
    public void TheRealBoundIsCrossedAndExactlyTheNewestSurvive()
    {
        using (Open())
        {
            // Migrated, so the table exists to seed.
        }

        Seed(DbPath, Corpus.MostAudit);
        using var corpus = Open();
        corpus.AuditCount().Should().Be(Corpus.MostAudit, "at the bound, nothing has been swept yet");
        var first = AnId();
        var second = AnId();

        corpus.Issue(first, "hash-1", string.Empty, Cli());
        corpus.Issue(second, "hash-2", string.Empty, Cli());

        corpus.AuditCount().Should().Be(Corpus.MostAudit, "two writes crossed the mark and two rows went");
        corpus.AuditTrail(1).Single().Id.Should().Be(3, "the two oldest seeded rows (ids 1 and 2) are the ones that went");
        corpus.AuditTrail(2, skip: Corpus.MostAudit - 2).Select(row => row.Target)
            .Should().Equal([first, second], "and the newest two are the issuances that ran the sweep");
    }

    /// <summary>A mutation that cannot be audited does not happen: the two commit together or not at all.</summary>
    /// <remarks>
    /// The audit table is dropped underneath the corpus, so the audit insert fails after the key
    /// insert succeeded. Without one transaction the key would remain — an issuance the API would
    /// have reported as success with no record of who did it.
    /// </remarks>
    [Fact]
    public void AnIssuanceThatCannotBeAuditedIsNotAnIssuance()
    {
        using var corpus = Open();
        TestSql.Run(DbPath, "DROP TABLE admin_audit");
        var id = AnId();
        var issuing = () => corpus.Issue(id, Corpus.HashOf("k", "s"), string.Empty, Cli());

        issuing.Should().Throw<SqliteException>("the audit insert has nowhere to write");

        corpus.UsageOf(id).Should().Be(new KeyUsage(0, string.Empty), "the key row was rolled back with the audit that failed");
        corpus.KeyFor("k", "s").Should().BeEmpty("and it cannot authenticate");
    }

    /// <summary>Rows an older run left, inserted directly: the shape the production path writes, at volume.</summary>
    private static void Seed(string path, int rows)
    {
        using var db = new SqliteConnection($"Data Source={path};Pooling=False");
        db.Open();
        using var transaction = db.BeginTransaction();
        using var insert = db.CreateCommand();
        insert.CommandText =
            "INSERT INTO admin_audit (admin_id, action, target, at_utc) VALUES ('cli', 'issue', $target, '2026-09-01T00:00:00.0000000Z')";
        var target = insert.Parameters.Add("$target", SqliteType.Text);
        for (var n = 0; n < rows; n++)
        {
            target.Value = n.ToString("x16", CultureInfo.InvariantCulture);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Dispose() => Scratch.Delete(_dir);
}

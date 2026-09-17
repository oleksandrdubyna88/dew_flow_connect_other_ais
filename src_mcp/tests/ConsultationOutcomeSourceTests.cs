using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// WHO said how a consultation ended, and a log that still draws for a server one release older.
/// </summary>
/// <remarks>
/// <para>Two halves of one field. "The AI said it worked", "a person marked it closed" and "the
/// clock ran out" are three different claims, and until this field existed the only trace of which
/// door a verdict came through was <c>Reason</c> — which is deliberately NOT overwritten, so a
/// consultation the sweep had already closed kept its sentence about the budget and the verdict
/// arrived beside it with no author at all. (codex Architecture, the code round of issue #309; the
/// plan promised the distinction and the first build did not keep it.)</para>
/// <para>The other half is the seam. This reader opens the database READ-ONLY, so the schema steps
/// never run for it: a data directory last written by the previous release genuinely has a
/// <c>consultations</c> table without these columns, and naming one in the SELECT threw the whole
/// log page away — rounds, blind spots and all. The rounds half already knows this and carries two
/// query texts for it; the consultations half did not.</para>
/// </remarks>
public sealed class ConsultationOutcomeSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-outcome-by-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private ConsultationStore Store() => new(
        _dir,
        warn: null,
        projected: record => new Projection(_dir, _log)
            .Write(db => db.RecordConsultation(ConsultationRows.From(record)), "the consultation"));

    private static ConsultationRecord Record() => new(
        "b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5",
        "claude:session-1",
        "claude",
        "no-session",
        "D:/repo",
        "feat/x",
        "abc1234",
        "codex",
        "gpt-5.6-luna",
        "codex",
        ConsultationMemories.VendorRemembers,
        5,
        "2026-09-17T09:00:00.0000000Z");

    private IReadOnlyList<LoggedConsultation> Listed() => RoundsQuery.Read(_dir).Consultations;

    /// <summary>
    /// The author of the verdict reaches the log, where the export that must tell them apart reads.
    /// </summary>
    /// <remarks>
    /// On the ROW rather than parsed back out of the sentence: a note a person typed can contain any
    /// words at all, and an export reading English to decide who spoke is an export that will one day
    /// be wrong about it.
    /// </remarks>
    [Fact]
    public void WhoRecordedTheOutcomeReachesTheLog()
    {
        Store().Write(Record() with
        {
            Status = ConsultationStatuses.Closed,
            Outcome = ConsultationOutcomes.Solved,
            OutcomeBy = "person",
            Reason = "all 5 of its turns are used (COAI_CONSULT_TURNS sets the cap)",
            EndedUtc = "2026-09-17T09:30:00.0000000Z",
        });

        var listed = Listed().Should().ContainSingle().Subject;
        listed.Outcome.Should().Be("solved");
        listed.OutcomeBy.Should().Be("person");
        listed.Reason.Should().Contain("turns are used", "why it stopped is not how it ended, and both are kept");
    }

    /// <summary>An outcome nobody has recorded has no author, and empty is not one.</summary>
    [Fact]
    public void AConsultationNobodyHasClosedNamesNobody()
    {
        Store().Write(Record());

        Listed().Should().ContainSingle().Subject.OutcomeBy.Should().BeEmpty();
    }

    /// <summary>
    /// A data directory written by the PREVIOUS release still draws its whole log page.
    /// </summary>
    /// <remarks>
    /// <para>The two halves of this product update separately, and this reader is read-only — the
    /// schema runs on OPEN, and opening read-only steps nothing. So a database whose last writer was
    /// the release before this one has the <c>consultations</c> table and none of the columns this
    /// story added, and a SELECT that names one answers <c>SQLite Error 1: 'no such column'</c>.
    /// That exception is not caught: the reader's guard is deliberately narrowed to "no such TABLE",
    /// because a half-written file reading as an empty one is the worse failure.</para>
    /// <para>The consequence is not a missing column. <c>RoundsQuery.Read</c> composes the WHOLE
    /// page, so the throw takes the rounds, the blind spots and the totals with it — the log tab
    /// draws nothing at all until the new server happens to write a round. The rounds half has
    /// carried two query texts for exactly this since story 6's counter column; the consultations
    /// half had one.</para>
    /// </remarks>
    [Fact]
    public void ADatabaseFromThePreviousRelease_StillDrawsItsWholeLogPage()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, RoundsDb.FileName);

        // Every step UP TO the one that adds `outcome` — the shape the release before this one left
        // behind. Found by asking which step that is, never by counting: a literal index rots the
        // moment anybody appends, and it rots into a test that passes while proving nothing.
        // `outcome TEXT` rather than `outcome`, because `outcome_by` contains the shorter one.
        var adds = Array.FindIndex(Schema.Steps, step => step.Contains("ADD COLUMN outcome TEXT", StringComparison.Ordinal));
        adds.Should().BeGreaterThan(0, "the step that adds the column is what this test stops before");

        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..adds])
                + $"; PRAGMA user_version={adds}";
            make.ExecuteNonQuery();

            // A consultation that server recorded. Every column of that shape has a default, so the
            // three that carry the assertion are the three named.
            using var insert = db.CreateCommand();
            insert.CommandText =
                "INSERT INTO consultations (id, status, started_utc, vendor) VALUES ('old1', 'closed', '2026-09-01T00:00:00Z', 'codex')";
            insert.ExecuteNonQuery();
        }

        var log = RoundsQuery.Read(_dir);

        var listed = log.Consultations.Should().ContainSingle("the row is there and the page must show it").Subject;
        listed.Vendor.Should().Be("codex");
        listed.Outcome.Should().BeEmpty("a row written before the column existed carries no verdict — and empty is not one");
        listed.OutcomeBy.Should().BeEmpty("nor an author");
        log.Rounds.Should().BeEmpty("the rest of the page is composed in the same call and must survive with it");
    }

    /// <summary>
    /// And a database with the first of the two columns but not the second is the same promise.
    /// </summary>
    /// <remarks>
    /// The intermediate shape is real: a build carrying only <c>outcome</c> shipped to this
    /// machine's own data directory before the author column was written. One guard per column, so
    /// neither of the two can be the one that is forgotten.
    /// </remarks>
    [Fact]
    public void ADatabaseWithTheOutcomeButNotItsAuthor_StillDrawsTheOutcome()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, RoundsDb.FileName);
        var adds = Array.FindIndex(Schema.Steps, step => step.Contains("ADD COLUMN outcome_by", StringComparison.Ordinal));
        adds.Should().BeGreaterThan(0, "the author column is its own step, appended rather than folded into the one before");

        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..adds])
                + $"; PRAGMA user_version={adds}";
            make.ExecuteNonQuery();

            using var insert = db.CreateCommand();
            insert.CommandText =
                "INSERT INTO consultations (id, status, started_utc, outcome) VALUES ('mid1', 'closed', '2026-09-01T00:00:00Z', 'solved')";
            insert.ExecuteNonQuery();
        }

        var listed = RoundsQuery.Read(_dir).Consultations.Should().ContainSingle().Subject;
        listed.Outcome.Should().Be("solved");
        listed.OutcomeBy.Should().BeEmpty();
    }

    /// <summary>The step runs on a database that already exists, which is how anybody will meet it.</summary>
    [Fact]
    public void AnExistingDatabaseGainsTheAuthorColumn()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, RoundsDb.FileName);
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = Schema.Tables + "; PRAGMA user_version=1";
            make.ExecuteNonQuery();
        }

        using (var stepped = RoundsDb.Open(_dir, _log))
        {
            stepped.Should().NotBeNull();
        }

        using var read = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly");
        read.Open();
        using var ask = read.CreateCommand();
        ask.CommandText = "SELECT COUNT(*) FROM pragma_table_info('consultations') WHERE name = 'outcome_by'";
        ask.ExecuteScalar().Should().Be(1L);
    }

    /// <summary>
    /// The server's own <c>lapsed</c> names the server, so no export can read it as a person's word.
    /// </summary>
    /// <remarks>
    /// This is the case the distinction was asked for: a consultation whose budget ran out is closed
    /// by nobody, and a log that showed only <c>lapsed</c> beside a verdict somebody reached would
    /// present the clock and the person as the same kind of statement.
    /// </remarks>
    [Fact]
    public void TheClockRunningOutIsRecordedAsTheServersOwnWord()
    {
        var lapsed = ConsultationClosing.Lapse(Record());

        lapsed.Outcome.Should().Be(ConsultationOutcomes.Lapsed);
        lapsed.OutcomeBy.Should().Be("server");
    }

    /// <summary>And lapsing over a verdict changes neither the word nor its author.</summary>
    [Fact]
    public void LapsingOverAVerdictKeepsWhoSaidIt()
    {
        var already = ConsultationClosing.Lapse(Record() with { Outcome = ConsultationOutcomes.Solved, OutcomeBy = "caller" });

        already.Outcome.Should().Be(ConsultationOutcomes.Solved);
        already.OutcomeBy.Should().Be("caller");
    }
}

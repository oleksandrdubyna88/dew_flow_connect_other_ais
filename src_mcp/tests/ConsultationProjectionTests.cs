using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultation reaches the log, and every state it passes through updates the same row.
/// </summary>
/// <remarks>
/// <para>Real SQLite over a temp directory, no fakes, for the reason <c>RoundsDbTests</c> states: the
/// point is that the SQL runs. The projection hangs off <see cref="ConsultationStore.Write"/> rather
/// than off the service, so the sweep's own transitions — a dead <c>asking</c> record, an idle one
/// being closed — are projected too; a projection wired in the service would have recorded the turns
/// and silently missed both, leaving consultations in the log that never ended.</para>
/// </remarks>
public sealed class ConsultationProjectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-consult-db-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
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

    private static ConsultationRecord Record(string id = "b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5") => new(
        id,
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
        DateTime.UtcNow.AddMinutes(-3).ToString("O"));

    private IReadOnlyList<LoggedConsultation> Listed() => RoundsQuery.Read(_dir).Consultations;

    [Fact]
    public void AConsultationIsProjectedTheMomentItIsAsked_BeforeAnyAnswerExists()
    {
        Store().Write(Record());

        var listed = Listed().Should().ContainSingle().Subject;
        listed.Status.Should().Be(ConsultationStatuses.Asking);
        listed.Vendor.Should().Be("codex");
        listed.CallerKind.Should().Be("claude");
        listed.Turns.Should().Be(0, "nothing has been answered yet, which is what `asking` means");
        listed.CostUsd.Should().BeNull("a consultation with no turn has not cost anything anybody can name");
    }

    /// <summary>
    /// The FIRST problem and the LAST advice, which are two different questions.
    /// </summary>
    /// <remarks>
    /// A follow-up's problem text is "I ran your check and it printed 3" — it names nothing on its
    /// own, so the list would say what the consultation was about only until somebody answered back.
    /// The advice goes the other way: an earlier one was superseded by what the caller reported.
    /// </remarks>
    [Fact]
    public void ASecondTurnUpdatesTheSameRow_KeepingTheFirstProblemAndTheLastAdvice()
    {
        var store = Store();
        var record = Record();
        store.Write(record);

        var answered = record with
        {
            Status = ConsultationStatuses.Open,
            Turns =
            [
                new(DateTime.UtcNow.ToString("O"), "The parser returns 3 where 4 is expected.", "Print the token stream.", 24.7, 31_402, 812, 0.11),
                new(DateTime.UtcNow.ToString("O"), "I printed it: the last token is dropped.", "Your loop stops one short.", 12.1, 9_000, 300, 0.04),
            ],
        };
        store.Write(answered);

        var listed = Listed().Should().ContainSingle("a consultation is ONE row that advances, not a row per turn").Subject;
        listed.Turns.Should().Be(2);
        listed.Problem.Should().Contain("returns 3 where 4");
        listed.Advice.Should().Be("Your loop stops one short.");
        listed.Seconds.Should().BeApproximately(36.8, 0.01);
        listed.TokensIn.Should().Be(40_402);
        listed.TokensOut.Should().Be(1_112);
        listed.CostUsd.Should().BeApproximately(0.15, 0.001);
    }

    [Fact]
    public void TheSweepsOwnTransitionsAreProjectedToo_SoNothingStaysOpenInTheLogForEver()
    {
        var store = Store();
        var record = Record() with
        {
            Status = ConsultationStatuses.Open,
            UpdatedUtc = DateTime.UtcNow.AddHours(-2).ToString("O"),
            Turns = [new(DateTime.UtcNow.AddHours(-2).ToString("O"), "stuck", "try this", 5, 100, 10, null)],
        };
        store.Write(record);

        // Nobody is working in a directory that does not exist, and two hours is past any idle cap.
        var changed = store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7));

        changed.Should().Be(1);
        var listed = Listed().Should().ContainSingle().Subject;
        listed.Status.Should().Be(ConsultationStatuses.Closed);
        listed.Reason.Should().Contain("idle");
        listed.EndedUtc.Should().NotBeEmpty();
    }

    [Fact]
    public void TheAlertIsCarried_BecauseItIsTheOneFieldAPersonMustRead()
    {
        Store().Write(Record() with
        {
            Status = ConsultationStatuses.Failed,
            Reason = "the working tree changed while the consultant was reading it",
            Alert = "2 files changed under D:/repo during this consultation",
            EndedUtc = DateTime.UtcNow.ToString("O"),
        });

        var listed = Listed().Should().ContainSingle().Subject;
        listed.Status.Should().Be(ConsultationStatuses.Failed);
        listed.Alert.Should().Contain("2 files changed");
    }

    /// <summary>
    /// The log catches up with records whose projection never landed.
    /// </summary>
    /// <remarks>
    /// The projection is allowed to fail — the record file is the source of truth and a database that
    /// is locked or full is a line in the log. But a TERMINAL record is never written again, so a
    /// consultation whose last write met a locked database would be missing from the log for ever.
    /// This is the pass that fixes that, and the case is simulated exactly: a record written with NO
    /// projection at all, then reconciled. (codex, story 4's plan round.)
    /// </remarks>
    [Fact]
    public void AConsultationWhoseProjectionNeverLanded_IsCarriedAcrossByTheReconciliation()
    {
        // Written with no projection wired at all: the file exists, the database has never heard of it.
        var unprojected = new ConsultationStore(_dir);
        unprojected.Write(Record() with
        {
            Status = ConsultationStatuses.Closed,
            Reason = "all 5 of its turns are used",
            EndedUtc = DateTime.UtcNow.ToString("O"),
            Turns = [new(DateTime.UtcNow.ToString("O"), "stuck", "the loop stops one short", 5, 100, 10, null)],
        });
        Listed().Should().BeEmpty("nothing has projected it yet, which is the situation being fixed");

        // What the service does at startup, over the records the store already reads for its sweep.
        var store = Store();
        foreach (var record in store.All())
        {
            store.Write(record);
        }

        var listed = Listed().Should().ContainSingle().Subject;
        listed.Status.Should().Be(ConsultationStatuses.Closed);
        listed.Advice.Should().Be("the loop stops one short");
    }

    /// <summary>
    /// Projecting the same record twice produces the same row, not doubled totals.
    /// </summary>
    /// <remarks>
    /// Which is what makes the reconciliation above safe to run on every start: the row is RECOMPUTED
    /// from the record's own turns and upserted by id, never accumulated. Raised on the plan round as
    /// a risk; it is a property of the shape, and this is the test that says so.
    /// </remarks>
    [Fact]
    public void ProjectingTheSameRecordTwice_IsTheSameRow()
    {
        var store = Store();
        var record = Record() with
        {
            Status = ConsultationStatuses.Open,
            Turns = [new(DateTime.UtcNow.ToString("O"), "stuck", "try this", 12.5, 1_000, 100, 0.02)],
        };

        store.Write(record);
        store.Write(record);
        store.Write(record);

        var listed = Listed().Should().ContainSingle().Subject;
        listed.Turns.Should().Be(1);
        listed.TokensIn.Should().Be(1_000);
        listed.Seconds.Should().BeApproximately(12.5, 0.001);
        listed.CostUsd.Should().BeApproximately(0.02, 0.0001);
    }

    /// <summary>
    /// A database written before this table existed answers an EMPTY list, never an error.
    /// </summary>
    /// <remarks>
    /// The two halves of this product update separately, and the log page must draw whatever it is
    /// given. This is the same promise <c>LoggedLog.Consultations</c> makes by defaulting.
    /// </remarks>
    [Fact]
    public void ADatabaseWithoutTheTable_ReadsAsNoConsultations()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, RoundsDb.FileName);
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            // Every step BEFORE the one that creates `consultations`, found by asking which step
            // that is rather than by counting. "All but the last" was the first answer and it rots
            // the moment anybody appends a step: the slice would then include this one, the table
            // would exist, and the test would pass while proving nothing. An index is the same trap
            // one layer along. (CodeRabbit, on the pull request.)
            var creates = Array.FindIndex(Schema.Steps, step => step.Contains("consultations", StringComparison.Ordinal));
            creates.Should().BeGreaterThan(0, "the step that creates the table is what this test excludes");
            var before = Schema.Steps[..creates];
            make.CommandText = string.Join(";\n", before)
                + $"; PRAGMA user_version={before.Length}";
            make.ExecuteNonQuery();
        }

        var log = RoundsQuery.Read(_dir);

        log.Consultations.Should().BeEmpty();
        // And the ROUNDS list still draws, which is the half that cannot answer "none": naming a
        // column that file has never had failed the whole page. Found by this test the first time
        // story 6's counter was selected.
        log.Rounds.Should().BeEmpty("an empty database has no rounds either, but asking must not throw");
    }

    /// <summary>
    /// A table that EXISTS but is missing a column is a broken file, not an empty one.
    /// </summary>
    /// <remarks>
    /// The distinction the empty answer above is allowed to make, and the one it used to destroy:
    /// the filter was `code == SqliteNoSuchTable || Missing(e)`, and that code is SQLite's generic
    /// `SQLITE_ERROR`, so "no such column" matched the first half and the page answered "no
    /// consultations" for a file that has some. A wrong answer that looks like a true one is the
    /// defect this whole reader is written against. (CodeRabbit, on the pull request.)
    /// </remarks>
    [Fact]
    public void ATableMissingAColumn_IsNotReadAsAnEmptyList()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, RoundsDb.FileName);
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            // Everything the reader needs EXCEPT one column of `consultations`: a half-stepped file,
            // which is what an interrupted migration leaves and what a corrupt one can look like.
            make.CommandText = Schema.Tables + """
                ; CREATE TABLE consultations (
                    id TEXT PRIMARY KEY, repo_path TEXT NOT NULL, branch TEXT NOT NULL
                ); PRAGMA user_version=2
                """;
            make.ExecuteNonQuery();
        }

        var act = () => RoundsQuery.Read(_dir);

        act.Should().Throw<Microsoft.Data.Sqlite.SqliteException>(
            "a file that is broken must say so rather than read as a file with nothing in it");
    }

    /// <summary>
    /// A projection that throws is a line in the log, never a failed round.
    /// </summary>
    /// <remarks>
    /// The claim that lets the consultation record and story 6's counter be written from inside the
    /// projection at all: the record files are the source of truth, and a database that is locked,
    /// full or corrupt must never take down the thing it is a view of. It was stated and not driven.
    /// (codex, story 6's plan round.)
    /// </remarks>
    [Fact]
    public void AWriterThatThrows_IsSwallowedByTheProjection()
    {
        var projection = new Projection(_dir, _log);

        var act = () => projection.Write(_ => throw new InvalidOperationException("the disk said no"), "the test");

        act.Should().NotThrow("a measurement may never be the reason a round fails");
    }

    /// <summary>
    /// The step runs on a database that already exists, which is the only way anybody will meet it.
    /// </summary>
    [Fact]
    public void AnExistingDatabaseGainsTheTableAndTheCounterColumn()
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

        // Opening is what steps it.
        using (var stepped = RoundsDb.Open(_dir, _log))
        {
            stepped.Should().NotBeNull();
        }

        using var read = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly");
        read.Open();
        using var ask = read.CreateCommand();
        ask.CommandText = "SELECT COUNT(*) FROM pragma_table_info('rounds') WHERE name = 'consult_missed'";
        ask.ExecuteScalar().Should().Be(1L, "story 6's counter has nowhere to go without the column");

        ask.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'consultations'";
        ask.ExecuteScalar().Should().Be(1L);
    }

    /// <summary>
    /// What a consultation was FOR reaches the log, so the page can tell a stuck one from the cadence's.
    /// </summary>
    /// <remarks>
    /// Epic 4 story 4.3 of <c>todo/PLAN_consult_on_a_cadence.md</c>: the log listed every consultation as
    /// if it were an agent admitting it was stuck, and a cadence consultation is the opposite — one the
    /// gate asked for while nothing was wrong.
    /// </remarks>
    [Fact]
    public void ACadenceConsultation_IsListedWithItsKindPlanAndEpics()
    {
        Store().Write(Record() with { Kind = "cadence", Plan = "todo/PLAN_x.md", Epics = "4-6" });

        var listed = Listed().Should().ContainSingle().Subject;
        listed.Kind.Should().Be("cadence");
        listed.Plan.Should().Be("todo/PLAN_x.md");
        listed.Epics.Should().Be("4-6");
    }

    /// <summary>
    /// A database from before the kinds lists its consultations as what they were: stuck ones.
    /// </summary>
    /// <remarks>
    /// Read-only opening steps nothing, so the release before this one leaves the table without the three
    /// columns — and a SELECT naming one would take the whole log page down (the reason the older shapes
    /// exist at all, <c>ConsultationOutcomeSourceTests</c>). Before kinds, every consultation WAS stuck.
    /// </remarks>
    [Fact]
    public void ADatabaseFromBeforeTheKinds_ListsItsConsultationsAsStuck()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, RoundsDb.FileName);
        var adds = Array.FindIndex(Schema.Steps, step => step.Contains("ADD COLUMN kind", StringComparison.Ordinal));
        adds.Should().BeGreaterThan(0, "the kinds are their own step, appended rather than folded into an earlier one");

        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..adds]) + $"; PRAGMA user_version={adds}";
            make.ExecuteNonQuery();

            using var insert = db.CreateCommand();
            insert.CommandText =
                "INSERT INTO consultations (id, status, started_utc, outcome) VALUES ('old1', 'closed', '2026-09-01T00:00:00Z', 'solved')";
            insert.ExecuteNonQuery();
        }

        var listed = RoundsQuery.Read(_dir).Consultations.Should().ContainSingle().Subject;
        listed.Kind.Should().Be("stuck");
        listed.Plan.Should().BeEmpty();
        listed.Epics.Should().BeEmpty();
        listed.Outcome.Should().Be("solved", "the columns older than the kinds are still read");
    }
}

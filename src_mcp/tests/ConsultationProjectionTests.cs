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
            // Step 0 only — exactly what an older binary left behind.
            make.CommandText = Schema.Tables + "; PRAGMA user_version=1";
            make.ExecuteNonQuery();
        }

        RoundsQuery.Read(_dir).Consultations.Should().BeEmpty();
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
}

using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Why a consultation failed reaches the log page — and a database written before that column still draws.
/// </summary>
/// <remarks>
/// <para>Step 19, <c>WhyAConsultationFailed</c> (17 on its own branch, renumbered when it was rebased onto <c>main</c>; PLAN_the_consultant_works_on_every_vendor.md, E2.4). Real
/// SQLite over a temp directory, as <see cref="ConsultationOutcomeSourceTests"/> does it: the point is that
/// the SQL runs.</para>
/// <para>The log reader opens READ-ONLY, so the steps never run for it: a data directory last written by the
/// previous release has the table without these columns, and naming one in the SELECT would throw the whole
/// page away. So every shape is asked for by the newest column it has.</para>
/// </remarks>
public sealed class ConsultationFailureColumnsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-failure-cols-" + Guid.NewGuid().ToString("N")[..8]);
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

    private static ConsultationRecord Failed() =>
        new("b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5", "claude:session-1", "claude", "no-session", "D:/repo", "feat/x", "abc1234",
            "antigravity", "gemini-3.1-pro-high", "antigravity", ConsultationMemories.VendorRemembers, 5, "2026-10-02T09:00:00.0000000Z")
        {
            Status = ConsultationStatuses.Failed,
            FailureKind = "command-denied",
            FailureCure = "name the files that hold the answer",
            Evidence = "D:/data/unparseable/consult-antigravity-20261002-090000-000.txt",
        };

    [Fact]
    public void AFailedConsultation_ReachesTheLog_WithItsKindCureAndEvidence()
    {
        Store().Write(Failed());

        var listed = RoundsQuery.Read(_dir).Consultations.Should().ContainSingle().Subject;
        listed.FailureKind.Should().Be("command-denied");
        listed.FailureCure.Should().Be("name the files that hold the answer");
        listed.Evidence.Should().EndWith("consult-antigravity-20261002-090000-000.txt");
    }

    [Fact]
    public void ALaterAnswer_ClearsTheRow_AsItClearsTheRecord()
    {
        var store = Store();
        store.Write(Failed() with { Status = ConsultationStatuses.Interrupted });

        store.Write(Failed() with { Status = ConsultationStatuses.Open, FailureKind = string.Empty, FailureCure = string.Empty, Evidence = string.Empty });

        RoundsQuery.Read(_dir).Consultations.Single().FailureKind.Should().BeEmpty("the upsert carries the cleared columns too");
    }

    /// <summary>Every shape a database can be in, each built by running the steps before the one that adds its newest column.</summary>
    [Theory]
    [InlineData("ADD COLUMN failure_kind")]
    [InlineData("ADD COLUMN kind ")]
    [InlineData("ADD COLUMN outcome_by")]
    [InlineData("ADD COLUMN outcome TEXT")]
    public void ADatabaseFromBeforeAColumn_StillDrawsItsConsultations_WithNoFailureClassified(string newestMissing)
    {
        Directory.CreateDirectory(_dir);
        var adds = Array.FindIndex(Schema.Steps, step => step.Contains(newestMissing, StringComparison.Ordinal));
        adds.Should().BeGreaterThan(0, $"'{newestMissing}' is added by a step of its own");
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..adds]) + $"; PRAGMA user_version={adds}";
            make.ExecuteNonQuery();

            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO consultations (id, status, started_utc) VALUES ('old1', 'failed', '2026-09-01T00:00:00Z')";
            insert.ExecuteNonQuery();
        }

        var listed = RoundsQuery.Read(_dir).Consultations.Should().ContainSingle().Subject;
        listed.FailureKind.Should().BeEmpty();
        listed.FailureCure.Should().BeEmpty();
        listed.Evidence.Should().BeEmpty();
    }

    /// <summary>By POSITION, not as the last step: the next schema step must not turn this red (the question consultant's pin learned that).</summary>
    [Fact]
    public void TheFailureColumns_AreStep19()
    {
        Schema.Steps[18].Should().Be(Schema.WhyAConsultationFailed,
            "a column is appended as its own step, never folded into an earlier one — after main's 17 (the questions) and 18 (the security evidence)");
    }
}

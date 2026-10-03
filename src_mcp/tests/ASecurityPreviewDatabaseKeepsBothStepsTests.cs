using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema steps 17 and 18 were numbered on two branches at once. The security lane's preview build
/// (2026-10-02, a Native-AOT preview run against a real data directory) stamped <c>user_version = 17</c>
/// for ITS evidence column, while <c>main</c> gave 17 to the question consultant's tables. Merged, the
/// questions are 17 and the evidence 18 — so a preview database would skip the question tables for ever
/// and then fail step 18 with <c>duplicate column name</c>, losing the rounds database entirely.
/// </summary>
public sealed class ASecurityPreviewDatabaseKeepsBothStepsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-preview-fork-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    /// <summary>The steps before the question consultant's, then the security evidence as the preview numbered it.</summary>
    private void MakeAt(IEnumerable<string> steps)
    {
        Directory.CreateDirectory(_dir);
        var list = steps.ToArray();
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = string.Join(";\n", list) + $"; PRAGMA user_version={list.Length}";
        make.ExecuteNonQuery();
    }

    private (long Version, bool Questions, bool Evidence) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";
        return ((long)ask.ExecuteScalar()!,
            RoundsQuery.HasColumn(db, "question_consults", "id"),
            RoundsQuery.HasColumn(db, "findings", "security_evidence"));
    }

    private static int QuestionsStep => Array.IndexOf(Schema.Steps, Schema.TheQuestionsAsked);

    [Fact]
    public void A_database_the_security_preview_stamped_17_gains_the_question_tables_and_keeps_opening()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.SecurityEvidence]);
        Shape().Should().Be((17L, false, true), "the fixture is the preview's database, not today's");

        using (var opened = RoundsDb.Open(_dir, Serilog.Core.Logger.None))
        {
            opened.Should().NotBeNull("a database the preview wrote must still open, not fail step 18 on a duplicate column");
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true));
    }

    [Fact]
    public void A_database_main_stamped_17_gains_the_evidence_column_as_step_18()
    {
        MakeAt(Schema.Steps[..(QuestionsStep + 1)]);
        Shape().Should().Be((17L, true, false), "the positive control: the ordinary path, main's database");

        using (var opened = RoundsDb.Open(_dir, Serilog.Core.Logger.None))
        {
            opened.Should().NotBeNull();
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true));
    }
}

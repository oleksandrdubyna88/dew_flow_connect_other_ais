using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 26: <c>role_jobs</c> and <c>role_attempts</c> (epoch 3 E1 s1) — appended after step 25, empty on every database written before it, and reached by a database that stood at step 25 with its
/// rounds intact. The jobs' FILES are the authority; these tables are a projection (<see cref="ARoleJobLeaseTests"/> holds the rows).
/// </summary>
public sealed class TheRoleJobsAreStepTwentySixTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-step26-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private static int Step => Array.IndexOf(Schema.Steps, Schema.TheRoleJobs);

    private (long Version, bool Jobs, bool Attempts) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";
        var version = (long)ask.ExecuteScalar()!;

        return (version, RoundsQuery.HasColumn(db, "role_jobs", "job_id"), RoundsQuery.HasColumn(db, "role_attempts", "ordinal"));
    }

    [Fact]
    public void TheStep_IsTheTwentySixth_AndLast()
    {
        Step.Should().Be(25, "step 26 is index 25: appended, never inserted");
        Schema.Steps.Should().HaveCountGreaterThanOrEqualTo(26);
    }

    [Fact]
    public void AFreshDatabase_HasBothTables_AtTheLastStep()
    {
        using (var opened = RoundsDb.Open(_dir.Path, Serilog.Core.Logger.None))
        {
            opened.Should().NotBeNull();
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true));
    }

    [Fact]
    public void ADatabaseAtStepTwentyFive_GainsBothTables_AndKeepsItsRounds()
    {
        var steps = Schema.Steps[..Step];
        using (var db = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, RoundsDb.FileName)};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", steps) + $"; PRAGMA user_version={steps.Length};"
                + "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');";
            make.ExecuteNonQuery();
        }

        Shape().Should().Be(((long)steps.Length, false, false));
        using (var opened = RoundsDb.Open(_dir.Path, Serilog.Core.Logger.None))
        {
            opened.Should().NotBeNull();
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true));
        using var after = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        after.Open();
        using var count = after.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM sessions";
        ((long)count.ExecuteScalar()!).Should().Be(1, "the migration added tables and touched nothing else");
    }
}

using System.Text;
using System.Text.Json;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E7.S4 — the owner's safety rules (2026-10-15/16), each a hard precondition with a test: a VERIFIED backup of the store before any write and the write
/// gated on it; a dry run that writes nothing and never opens the store for writing; nothing is ever deleted. Driven in process through
/// <see cref="BackfillAuthorsMode.RunAsync"/> with its seams, over a temp store — never a live one.
/// </summary>
public sealed class BackfillAuthorsBackupTests
{
    private const string Fixture = "resumed-agent";

    internal static async Task<(int Code, string Out, string Err)> RunAsync(BackfillWorld world, string[] flags, BackfillSeams? seams = null, TimeProvider? clock = null)
    {
        var output = new StringWriter();
        var errors = new StringBuilder();
        string? Env(string name) => name switch
        {
            "COAI_DATA_DIR" => world.DataDir,
            "COAI_AGENT_LOG_DIR" => world.Fixture.Home.ProjectsDir,
            _ => null,
        };
        var code = await BackfillAuthorsMode.RunAsync(["--backfill-authors", .. flags], Env, output, message => errors.AppendLine(message), clock ?? TimeProvider.System, seams);

        return (code, output.ToString(), errors.ToString());
    }

    private static BackfillWorld Stripped(string fixture = Fixture)
    {
        var world = BackfillWorld.Of(fixture);
        world.RecordLive();
        world.StripAuthors();

        return world;
    }

    private static string BackupsOf(BackfillWorld world) => Path.Combine(world.DataDir, BackfillBackup.FolderName);

    private static string Logical(BackfillWorld world) =>
        StoreDump.AuthorsAndSize(world.DataDir) + StoreDump.EverythingElse(world.DataDir)
        + string.Concat(Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").Order(StringComparer.Ordinal).Select(File.ReadAllText));

    private static void Exec(BackfillWorld world, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, RoundsDb.FileName)};Pooling=False");
        db.Open();
        BackfillWorld.Run(db, sql);
    }

    // ---------- the backup holds the store as it was ----------

    [Fact]
    public async Task TheBackupHoldsTheStoreAsItWas_UncheckpointedWalRowsIncluded()
    {
        using var world = Stripped();
        SqliteConnection.ClearAllPools();
        using var holder = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, RoundsDb.FileName)};Pooling=False");
        holder.Open();
        BackfillWorld.Run(holder, "PRAGMA wal_autocheckpoint = 0");
        BackfillWorld.Run(holder, "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('wal-only', 'D:/x', 'b', '2026-10-01T00:00:00.0000000Z')");
        var before = StoreDump.Rows(world.DataDir, "SELECT COUNT(*) FROM sessions");
        var files = Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);

        var run = await RunAsync(world, []);

        run.Code.Should().Be(0, run.Err);
        var summary = JsonDocument.Parse(run.Out).RootElement;
        var folder = summary.GetProperty("backupDir").GetString()!;
        folder.Should().StartWith(BackupsOf(world), "a NEW folder beside the store");
        summary.GetProperty("backupCheck").GetProperty("opened").GetBoolean().Should().BeTrue();
        summary.GetProperty("backupCheck").GetProperty("integrity").GetBoolean().Should().BeTrue();
        summary.GetProperty("backupCheck").GetProperty("rowsMatch").GetBoolean().Should().BeTrue();
        summary.GetProperty("backupCheck").GetProperty("filesMatch").GetBoolean().Should().BeTrue();
        using var copy = new SqliteConnection($"Data Source={Path.Combine(folder, RoundsDb.FileName)};Mode=ReadOnly;Pooling=False");
        copy.Open();
        using var count = copy.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM sessions";
        Convert.ToString(count.ExecuteScalar()).Should().Be(before.Trim(), "the row that lived only in the write-ahead log is in the copy");
        using var integrity = copy.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check";
        integrity.ExecuteScalar().Should().Be("ok");
        foreach (var (name, bytes) in files)
        {
            File.ReadAllBytes(Path.Combine(folder, "sessions", name!)).Should().Equal(bytes, $"{name} byte for byte");
        }

        Directory.GetFiles(folder).Select(Path.GetFileName).Should().Equal([RoundsDb.FileName], "one file: no -wal or -shm beside the copy");
    }

    // ---------- a backup that does not verify writes nothing ----------

    [Fact]
    public async Task ABackupThatDoesNotVerify_WritesNothing()
    {
        using var world = Stripped();
        string? logicalAtTheSeam = null;
        var seams = new BackfillSeams(DuringBackup: () =>
        {
            Exec(world, "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('a-concurrent-writer', 'D:/x', 'b', '2026-10-01T00:00:00.0000000Z')");
            logicalAtTheSeam = Logical(world);
        });

        var run = await RunAsync(world, [], seams);

        run.Code.Should().Be(74);
        run.Err.Should().Contain("rowsMatch");
        JsonDocument.Parse(run.Out).RootElement.GetProperty("backupCheck").GetProperty("rowsMatch").GetBoolean().Should().BeFalse();
        Logical(world).Should().Be(logicalAtTheSeam, "the store and every session file are as the concurrent writer left them: the mode wrote nothing");
        Directory.GetDirectories(BackupsOf(world)).Should().HaveCount(1, "the refused backup's folder is kept");
    }

    [Fact]
    public async Task AConcurrentCommitDuringTheBackup_IsRefused()
    {
        using var world = Stripped();
        string? logicalAtTheSeam = null;
        var seams = new BackfillSeams(DuringBackup: () =>
        {
            Exec(world, "UPDATE rounds SET note = 'a live server wrote this'");
            logicalAtTheSeam = Logical(world);
        });

        var run = await RunAsync(world, [], seams);

        run.Code.Should().Be(74, "an UPDATE keeps every count; only data_version can tell");
        var check = JsonDocument.Parse(run.Out).RootElement.GetProperty("backupCheck");
        check.GetProperty("rowsMatch").GetBoolean().Should().BeTrue("positive control: the counts alone would have passed it");
        check.GetProperty("dataVersionUnchanged").GetBoolean().Should().BeFalse();
        Logical(world).Should().Be(logicalAtTheSeam);
    }

    // ---------- the order ----------

    [Fact]
    public async Task TheFirstWriteComesAfterTheVerifiedBackup()
    {
        using var world = Stripped();
        var trace = new List<string>();

        var run = await RunAsync(world, [], new BackfillSeams(Trace: trace.Add));

        run.Code.Should().Be(0, run.Err);
        trace.Should().Contain("write");
        trace.First().Should().Be("open:ReadOnly");
        trace.IndexOf("backup:verified").Should().BeGreaterThan(trace.IndexOf("backup:start"));
        trace.IndexOf("open:ReadWrite").Should().BeGreaterThan(trace.IndexOf("backup:verified"), "OpenExisting — which migrates — is the first read-write open, and only after the verified backup");
        trace.IndexOf("write").Should().BeGreaterThan(trace.IndexOf("open:ReadWrite"));
        trace.Count(entry => entry == "open:ReadWrite").Should().Be(1);
    }

    // ---------- a dry run ----------

    [Fact]
    public async Task ADryRunWritesNothing_AndItsCountsAreTheWritingRuns()
    {
        using var world = Stripped();
        var inventory = Inventory(world);
        var dump = Logical(world);

        var dry = await RunAsync(world, ["--dry-run"]);

        dry.Code.Should().Be(0, dry.Err);
        Logical(world).Should().Be(dump, "every table's rows and every session file are unchanged");
        Directory.Exists(BackupsOf(world)).Should().BeFalse("a dry run makes no backup folder");
        Inventory(world).Except(inventory).Should().OnlyContain(path => IsCoordinationFile(path), "the data directory differs only by the lock and journal files SQLite and the session store may make");

        var real = await RunAsync(world, []);

        var dryCounts = JsonDocument.Parse(dry.Out).RootElement;
        var realCounts = JsonDocument.Parse(real.Out).RootElement;
        foreach (var part in new[] { "selected", "written", "healed", "kept", "deferred", "unobserved", "size" })
        {
            dryCounts.GetProperty(part).GetRawText().Should().Be(realCounts.GetProperty(part).GetRawText(), $"the dry run's {part} equal the writing run's");
        }

        dryCounts.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        dryCounts.GetProperty("backupDir").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task ADryRunNeverOpensTheStoreForWriting()
    {
        using var world = Stripped();
        var trace = new List<string>();

        var run = await RunAsync(world, ["--dry-run"], new BackfillSeams(Trace: trace.Add));

        run.Code.Should().Be(0, run.Err);
        trace.Should().Contain("open:ReadOnly");
        trace.Should().NotContain("open:ReadWrite", "a dry run reads through Mode=ReadOnly only");
        trace.Should().NotContain(entry => entry.StartsWith("backup", StringComparison.Ordinal));
    }

    private static List<string> Inventory(BackfillWorld world) =>
        [.. Directory.GetFiles(world.DataDir, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(world.DataDir, path))];

    private static bool IsCoordinationFile(string path) =>
        path.EndsWith("-wal", StringComparison.Ordinal) || path.EndsWith("-shm", StringComparison.Ordinal) || path.EndsWith(".turn", StringComparison.Ordinal);

    // ---------- nothing is ever deleted ----------

    [Fact]
    public async Task TheModeNeverDeletes_TwoRunsLeaveTwoBackupFolders()
    {
        using var world = Stripped();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

        await RunAsync(world, [], clock: clock);
        var afterOne = Inventory(world).Where(path => !IsCoordinationFile(path)).ToList();
        clock.Advance(TimeSpan.FromSeconds(1));
        await RunAsync(world, [], clock: clock);

        Directory.GetDirectories(BackupsOf(world)).Should().HaveCount(2, "each writing run makes its own, and none is removed");
        Inventory(world).Should().Contain(afterOne, "no file the mode did not create is missing afterwards, and none it created is removed");
    }

    [Fact]
    public async Task TwoRunsInOneSecond_GetTwoFolders_NeverASharedOne()
    {
        using var world = Stripped();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

        await RunAsync(world, [], clock: clock);
        await RunAsync(world, [], clock: clock);

        Directory.GetDirectories(BackupsOf(world)).Select(Path.GetFileName).Should().Equal("2026-10-19T12-00-00Z", "2026-10-19T12-00-00Z-2");
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        private DateTimeOffset _at = at;

        public void Advance(TimeSpan by) => _at += by;

        public override DateTimeOffset GetUtcNow() => _at;
    }
}

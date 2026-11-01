using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Authors;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>A decorator over the run's writes that counts them and lets a test act between the selection and the write — a live server's write.</summary>
internal sealed class SpyWrites(IBackfillWrites inner, Action<RoundWrite>? beforeWrite = null) : IBackfillWrites
{
    public int Writes { get; private set; }

    public int Heals { get; private set; }

    public WriteResult Write(RoundWrite write)
    {
        Writes++;
        beforeWrite?.Invoke(write);

        return inner.Write(write);
    }

    public WriteResult Heal(StoredGate gate, StoredRound round, string expected)
    {
        Heals++;

        return inner.Heal(gate, round, expected);
    }
}

/// <summary>
/// The backfill's units wired over a <see cref="BackfillWorld"/>'s store the way the mode wires them (E7.S3/S4) — a read-only connection for the
/// selection, a read-write <see cref="RoundsDb"/> for the writer, the run itself — with the seams the tests need: the fault point, the busy wait, the
/// transcript reader, a spy between selection and write. Everything is under the world's temp folder.
/// </summary>
internal sealed class BackfillRig : IDisposable
{
    private readonly SqliteConnection _read;
    private readonly RoundsDb? _db;

    public BackfillRig(
        BackfillWorld world,
        bool dry = false,
        TimeSpan? busyWait = null,
        Action<WritePoint>? fault = null,
        Action<RoundWrite>? beforeWrite = null,
        Func<CallerTranscriptLocation, ReadBudget, DateTime?, ObservedSession>? readTranscript = null,
        Func<AttributionInput, WindowAssignment>? assign = null,
        DateTime? since = null)
        : this(world.DataDir, world.Settings, world.Store, dry, busyWait, fault, beforeWrite, readTranscript, assign, since)
    {
        World = world;
    }

    /// <summary>The same rig over any data directory — the real-git tests build theirs through a live <see cref="PanelService"/>.</summary>
    public BackfillRig(
        string dataDir,
        PanelSettings settings,
        SessionStore store,
        bool dry = false,
        TimeSpan? busyWait = null,
        Action<WritePoint>? fault = null,
        Action<RoundWrite>? beforeWrite = null,
        Func<CallerTranscriptLocation, ReadBudget, DateTime?, ObservedSession>? readTranscript = null,
        Func<AttributionInput, WindowAssignment>? assign = null,
        DateTime? since = null)
    {
        _read = AuthorPeers.OpenReadOnly(dataDir)!;
        _db = dry ? null : RoundsDb.OpenExisting(dataDir, Serilog.Core.Logger.None);
        IBackfillWrites inner = _db is null ? new DryWrites() : new BackfillWriter(dataDir, store, _db, busyWait ?? TimeSpan.FromMilliseconds(300), fault);
        Writes = new SpyWrites(inner, beforeWrite);
        var launcher = new ProcessLauncher();
        Run = new BackfillRun(
            settings, store, _read, Writes, Counts, new StoredSizer(new ContextAssembler(launcher), launcher, Serilog.Core.Logger.None),
            Serilog.Core.Logger.None, since, readTranscript, assign);
    }

    public BackfillWorld? World { get; }

    public BackfillCounts Counts { get; } = new();

    public SpyWrites Writes { get; }

    public BackfillRun Run { get; }

    public Task RunAsync() => Run.RunAsync(TestContext.Current.CancellationToken);

    public void Dispose()
    {
        _read.Dispose();
        _db?.Dispose();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>One whole run, rig and all, and what it counted.</summary>
    public static async Task<BackfillCounts> RunOnceAsync(BackfillWorld world, bool dry = false, DateTime? since = null)
    {
        using var rig = new BackfillRig(world, dry, since: since);
        await rig.RunAsync();

        return rig.Counts;
    }
}

/// <summary>Canonical text of a store, for the tests that prove a run changed — or did not change — a byte of it.</summary>
internal static class StoreDump
{
    private static readonly string[] AuthorsOrSizeColumns =
        ["authors_", "submodule_writes", "uncovered_writes", "ambiguous_", "size_", "delta_state", "delta_from"];

    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>The rows of <paramref name="sql"/>, one line each.</summary>
    public static string Rows(string dataDir, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={Path.Combine(dataDir, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var text = new StringBuilder();
        while (rows.Read())
        {
            text.AppendJoin('|', Enumerable.Range(0, rows.FieldCount).Select(at => rows.IsDBNull(at) ? "NULL" : Convert.ToString(rows.GetValue(at), CultureInfo.InvariantCulture))).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The authors and size half of the store: the round's columns and every row of both tables.</summary>
    public static string AuthorsAndSize(string dataDir) =>
        Rows(dataDir, """
            SELECT 'round', id, authors_state, authors_session, authors_id_source, authors_reader, authors_attribution, submodule_writes, uncovered_writes,
                   ambiguous_agents, ambiguous_launches, ambiguous_turns, ambiguous_input_tokens, ambiguous_output_tokens, ambiguous_cache_creation_tokens,
                   ambiguous_cache_read_tokens, size_state, delta_state, delta_from, size_origin, size_gitlinks FROM rounds ORDER BY id
            """)
        + Rows(dataDir, "SELECT 'authors', * FROM round_authors ORDER BY round_id, ordinal")
        + Rows(dataDir, "SELECT 'changes', * FROM round_changes ORDER BY round_id, scope, coverage, kind");

    /// <summary>Everything else a backfill must leave alone: every non-authors column of <c>rounds</c>, and <c>findings</c>, <c>reviewers</c>, <c>sessions</c> whole.</summary>
    public static string EverythingElse(string dataDir)
    {
        var columns = Columns(dataDir).Where(name => !AuthorsOrSizeColumns.Any(name.StartsWith)).ToList();

        return Rows(dataDir, $"SELECT {string.Join(", ", columns)} FROM rounds ORDER BY id")
            + Rows(dataDir, "SELECT * FROM findings ORDER BY round_id, ordinal")
            + Rows(dataDir, "SELECT * FROM reviewers ORDER BY round_id, provider, role")
            + Rows(dataDir, "SELECT * FROM sessions ORDER BY id");
    }

    private static List<string> Columns(string dataDir) =>
        [.. Rows(dataDir, "SELECT name FROM pragma_table_info('rounds')").Split('\n', StringSplitOptions.RemoveEmptyEntries)];
}

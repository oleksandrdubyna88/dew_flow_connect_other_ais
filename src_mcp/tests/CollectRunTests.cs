using CoaiMcp.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A collector run reaches the database, against a real repository and a real database.
/// </summary>
/// <remarks>
/// The end-to-end the plan round asked for by name: "the described pure classification logic and git
/// fixture can pass while no command or service loads candidates, creates a run id, and updates the
/// four columns, leaving every candidate unprocessed in the shipped product". So this drives the
/// shipped path — `BugsQuery` out, `Collector` through, `RecordCollect` in — and then reads the rows
/// back. (codex.)
/// </remarks>
[Collection("console-out")]
public sealed class CollectRunTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly string _data = Path.Combine(Path.GetTempPath(), "coai-run-" + Guid.NewGuid().ToString("N")[..8]);
    private string _repo = string.Empty;

    private const string Racy = """
        public sealed class Totals
        {
            public int GetOrAdd(string key, int value)
            {
                if (!_items.ContainsKey(key))
                {
                    _items.Add(key, value);
                }

                return _items[key];
            }
        }
        """;

    private const string Fixed = """
        public sealed class Totals
        {
            public int GetOrAdd(string key, int value)
            {
                lock (_items)
                {
                    if (!_items.ContainsKey(key))
                    {
                        _items.Add(key, value);
                    }

                    return _items[key];
                }
            }
        }
        """;

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-runrepo-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Racy);
        await Commit("the defect");
    }

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var dir in (string[])[_repo, _data])
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ACollectedCandidateGetsItsFixCommitAndItsRunId()
    {
        var broken = await Head();
        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Fixed);
        await Commit("hold the lock");
        var fix = await Head();

        Seed(broken, "Totals.cs", 5);
        var summary = await Run();

        summary.Candidates.Should().Be(1);
        summary.Collected.Should().Be(1);
        summary.RunId.Should().NotBeEmpty("a row must be able to say which run decided it");

        var row = Row();
        row["collect_state"].Should().Be("collected");
        row["fix_sha"].Should().Be(fix);
        row["collect_run_id"].Should().Be(summary.RunId);
        row["collect_reason"].Should().BeEmpty("a collected candidate has nothing to explain");
    }

    [Fact]
    public async Task ASkippedCandidateRecordsTheReason_AndTheFunnelCountsIt()
    {
        var broken = await Head();
        await File.WriteAllTextAsync(Path.Combine(_repo, "notes.md"), "# hello");
        await Commit("a note");
        Seed(broken, "notes.md", 1);

        var summary = await Run();

        summary.Skipped.Should().Be(1);
        summary.Reasons.Should().ContainKey("language_unsupported").WhoseValue.Should().Be(1);
        Row()["collect_state"].Should().Be("skipped");
        Row()["collect_reason"].Should().Be("language_unsupported");
    }

    /// <summary>A second run does not see what the first one handled.</summary>
    /// <remarks>
    /// What `collect_state` being text rather than a flag buys: the first run consumes nothing it did
    /// not decide, and a later run with a repaired walk can still be pointed at the rest.
    /// </remarks>
    [Fact]
    public async Task ASecondRunSkipsWhatTheFirstOneAlreadyHandled()
    {
        var broken = await Head();
        await File.WriteAllTextAsync(Path.Combine(_repo, "Totals.cs"), Fixed);
        await Commit("hold the lock");
        Seed(broken, "Totals.cs", 5);

        (await Run()).Candidates.Should().Be(1);
        (await Run()).Candidates.Should().Be(0, "the first run left nothing unprocessed");
    }

    private async Task<CollectSummary> Run()
    {
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var run = new CollectRun(
            new Collector(new GitHistory(_launcher), new TreeSitterNormalizer()), TimeProvider.System);

        return await run.RunAsync(_data, db, 50);
    }

    /// <summary>One accepted, gating, runtime finding pointing at a real commit.</summary>
    private void Seed(string headSha, string file, int line)
    {
        using var db = RoundsDb.Open(_data, Serilog.Core.Logger.None)!;
        var state = new SessionState("s1", _repo, "main", new PanelConfig()) { Stage = Stage.CodeReview };
        var found = new Finding(
            Severity.Major, Category.Reliability, file, line, "a race", "it races", "hold the lock", ["codex"]);
        db.RecordRound(
            state,
            new RoundRecord("CodeReview", 1, "revise", 1, "all answered", DateTime.UtcNow),
            [found],
            new RoundContext("SCOPE", headSha, "claude-code"));
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept([found], 0)]);
    }

    private Dictionary<string, string> Row()
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_data, RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT collect_state, collect_reason, collect_run_id, fix_sha FROM findings";
        using var rows = read.ExecuteReader();
        rows.Read().Should().BeTrue();
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.FieldCount; i++)
        {
            row[rows.GetName(i)] = rows.IsDBNull(i) ? string.Empty : rows.GetString(i);
        }

        return row;
    }

    private async Task<string> Head()
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], _repo));
        result.ExitCode.Should().Be(0, result.StdErr);

        return result.StdOut.Trim();
    }

    private async Task Commit(string message)
    {
        await Git("add", ".");
        await Git("-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-m", message);
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }
}

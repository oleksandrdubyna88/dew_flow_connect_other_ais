using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static CoaiMcp.Tests.RoundCountsSnapshotFixtures;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--round-counts-merged</c>, run as the PROCESS the extension runs (E6.S1 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>).
/// </summary>
/// <remarks>
/// <para>The extension spawns this binary, reads stdout and believes the exit code, so the argument grammar, the JSON names and the codes
/// are what is exercised here, against a real database and real snapshot files. The code that matters most is the one this mode must NEVER
/// answer for a bad request: <b>64</b> means "this binary has never heard of that mode" and sends the extension down its fallback to
/// <c>--round-counts</c> — a request fault wearing it would hide the fault behind a successful-looking single-store count.</para>
/// <para>A snapshot problem is never an exit code: it is a row of <c>storesNotIncluded</c>.</para>
/// </remarks>
public sealed class RoundCountsMergedCliScenarioTests : IDisposable
{
    private readonly TempDir _data = TempDir.For("testsToDelete-merged-cli-data-");
    private readonly TempDir _others = TempDir.For("testsToDelete-merged-cli-others-");
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _data.Dispose();
        _others.Dispose();
    }

    private (int Code, string Out, string Err) Run(params string[] args) => OneShotProcess.Run(_data.Path, args);

    private static string[] Merged(params string[] more) => ["--round-counts-merged", "--now", Now.ToString("O"), .. more];

    private void Live(string key, DateTime started)
    {
        using var db = RoundsDb.Open(_data.Path, _log)!;
        Record(db, "s" + Guid.NewGuid().ToString("N")[..6], nameof(Stage.CodeReview), started, key, Reviewer("codex"), Reviewer("codex", role: "SecurityReliability"));
    }

    private string SnapshotOf(SnapshotStore store, string folder, params SnapshotRound[] rounds) =>
        Write(_others.At(folder), Document(store, Now.AddMinutes(-6), rounds));

    private static RoundCountsMergedAnswer Parsed(string text) =>
        JsonSerializer.Deserialize(text, ServerJsonContext.Default.RoundCountsMergedAnswer)!;

    [Fact]
    public void NoSnapshotPath_AnswersThisSideAlone_WithOneStore()
    {
        Live(Key('a'), Now.AddHours(-1));

        var (code, text, _) = Run(Merged());

        code.Should().Be(0);
        var answer = Parsed(text);
        answer.Stores.Should().ContainSingle().Which.Live.Should().BeTrue();
        answer.StoresNotIncluded.Should().BeEmpty();
        var row = answer.Windows[0].Rows.Single();
        row.Provider.Should().Be("codex");
        row.Rounds.Should().Be(new RoundsByStage(0, 1, 0, 0));
        row.Reviewers.Should().Be(2);
        row.RoundKeys.Should().Equal(Key('a'));
        row.KeyStore[Key('a')].Should().Be(answer.Stores[0].Id);
        row.Parts.Should().ContainSingle().Which.Store.Should().Be(answer.Stores[0].Id);
    }

    [Fact]
    public void TheAnswer_UsesTheNamesTheExtensionReads()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = SnapshotOf(Wsl("Ubuntu"), "wsl", Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran("codex")));
        var missing = _others.At("none", RoundCountsSnapshot.FileName);

        var (code, text, _) = Run(Merged(path, missing));

        code.Should().Be(0);
        var json = Parse(text);
        json.EnumerateObject().Select(p => p.Name).Should().Equal("nowUtc", "windows", "modelsKnownSince", "gauge", "stores", "storesNotIncluded");
        var row = json.GetProperty("windows")[0].GetProperty("rows")[0];
        row.EnumerateObject().Select(p => p.Name).Should().Equal(
            "provider", "rounds", "reviewers", "models", "roundKeys", "keyStore", "roundsWithoutKey", "parts");
        row.GetProperty("parts")[0].EnumerateObject().Select(p => p.Name).Should().Equal("store", "rounds", "reviewers");
        json.GetProperty("stores")[1].EnumerateObject().Select(p => p.Name).Should().Equal(
            "id", "live", "platform", "host", "wslDistro", "side", "directory", "paths", "asOf", "quiet", "truncated", "coaiMcpVersion", "conflicts");
        json.GetProperty("storesNotIncluded")[0].EnumerateObject().Select(p => p.Name).Should().Equal("path", "reason", "detail");
        json.GetProperty("storesNotIncluded")[0].GetProperty("reason").GetString().Should().Be("missing");
        text.Should().Contain("\"modelsKnownSince\":", "the key is present, null or not");
    }

    [Fact]
    public void ASnapshotStore_IsCountedBesideThisSide_ThroughTheRealProcess()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = SnapshotOf(Wsl("Ubuntu"), "wsl", Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran("codex")));

        var (code, text, _) = Run(Merged(path));

        code.Should().Be(0);
        var answer = Parsed(text);
        answer.Stores.Should().HaveCount(2);
        answer.Stores[1].Live.Should().BeFalse();
        answer.Stores[1].Paths.Should().Equal(path);
        answer.Stores[1].WslDistro.Should().Be("Ubuntu");
        answer.Windows[0].Rows.Single().Rounds.Code.Should().Be(2);
    }

    [Fact]
    public void ASnapshotOfThisSidesOwnStore_ThroughTheProcess_IsNotCountedTwice()
    {
        Live(Key('a'), Now.AddHours(-1));
        var mine = RoundCountsSnapshot.StoreOf(_data.Path, Environment.GetEnvironmentVariable);
        var path = SnapshotOf(mine, "mine", Round(1, Key('a'), Now.AddHours(-1), nameof(Stage.CodeReview), Ran("codex")));

        var (_, text, _) = Run(Merged(path));

        var answer = Parsed(text);
        answer.Stores.Should().ContainSingle().Which.Paths.Should().Equal(path);
        answer.Windows[0].Rows.Single().Rounds.Code.Should().Be(1);
    }

    [Fact]
    public void ASnapshotProblem_NeverChangesTheExitCode()
    {
        Live(Key('a'), Now.AddHours(-1));
        var good = SnapshotOf(Wsl("Ubuntu"), "wsl", Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran("codex")));
        var missing = _others.At("gone", RoundCountsSnapshot.FileName);
        var torn = _others.At("torn.json");
        File.WriteAllText(torn, "{");
        var newer = _others.At("newer.json");
        File.WriteAllText(newer, """{"schema":2}""");

        var (code, text, err) = Run(Merged(missing, good, torn, newer));

        code.Should().Be(0, $"a snapshot problem is a row of storesNotIncluded, never an exit code: {err}");
        var answer = Parsed(text);
        answer.StoresNotIncluded.Select(s => s.Reason).Should().Equal("missing", "unreadable", "newer-schema");
        answer.Windows[0].Rows.Single().Rounds.Code.Should().Be(2, "the good snapshot and this side are counted");
    }

    [Fact]
    public void APathGivenTwice_IsReadOnce()
    {
        var path = SnapshotOf(Wsl("Ubuntu"), "wsl", Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran("codex")));

        var (code, text, _) = Run(Merged(path, path));

        code.Should().Be(0);
        var answer = Parsed(text);
        answer.Stores[1].Paths.Should().Equal(path);
        answer.Windows[0].Rows.Single().Rounds.Code.Should().Be(1);
    }

    [Fact]
    public void ABadArgument_Is65_AndAnUnknownModeIs64()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = SnapshotOf(Wsl("Ubuntu"), "wsl", Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran("codex")));
        var tooMany = Enumerable.Range(0, 33).Select(n => _others.At("many", $"{n}.json")).ToArray();

        var runs = new (string Name, (int Code, string Out, string Err) Run)[]
        {
            ("a --now with no value", Run("--round-counts-merged", "--now")),
            ("an unparseable --now", Run("--round-counts-merged", "--now", "yesterday")),
            ("--now twice", Run("--round-counts-merged", "--now", Now.ToString("O"), "--now", Now.ToString("O"))),
            ("an unknown flag", Run("--round-counts-merged", "--since", Now.ToString("O"))),
            ("a flag-shaped argument", Run(Merged("--flag"))),
            ("a relative path", Run(Merged("snapshots/round-counts-snapshot.json"))),
            ("a relative path beside a good one", Run(Merged(path, "round-counts-snapshot.json"))),
            ("33 paths", Run(Merged(tooMany))),
        };

        foreach (var (name, run) in runs)
        {
            run.Code.Should().Be(65, $"{name} is a bad REQUEST to a mode this binary has; 64 would read as an old binary");
            run.Out.Should().BeEmpty($"{name}: a refused request answers nothing a caller could mistake for a count");
            run.Err.Should().Contain("--round-counts-merged", $"{name}: the sentence names the mode and what it takes");
        }

        Run("--round-counts-mergedx").Code.Should().Be(64, "the control: a mode the binary does NOT have is 64, and only that");
    }

    [Fact]
    public void ThirtyTwoPaths_AreAccepted()
    {
        var paths = Enumerable.Range(0, 32).Select(n => _others.At("many", $"{n}.json")).ToArray();

        var (code, text, _) = Run(Merged(paths));

        code.Should().Be(0);
        Parsed(text).StoresNotIncluded.Should().HaveCount(32, "none exist, each is said");
    }

    [Fact]
    public void ADatabaseThatWillNotRead_Is74_NeverAZero_NeverAnOldBinary()
    {
        File.WriteAllText(Path.Combine(_data.Path, RoundsDb.FileName), "this is not a database, it is text of some length so that the header is not a header");

        var (code, text, err) = Run(Merged());

        code.Should().Be(74);
        text.Should().BeEmpty();
        err.Should().Contain("could not be read");
    }

    [Fact]
    public void AMissingDatabase_IsThisSideWithNoRounds_ExitZero_AndNothingIsMade()
    {
        var missing = Path.Combine(_data.Path, "never-made");
        var info = OneShotProcess.StartInfo(missing, Merged());

        var (code, text, _) = OneShotProcess.Run(info, OneShotProcess.DefaultDeadline);

        code.Should().Be(0);
        Parsed(text).Windows.Should().HaveCount(3).And.AllSatisfy(w => w.Rows.Should().BeEmpty());
        Directory.Exists(missing).Should().BeFalse("a reader never makes the data directory");
    }

    /// <summary>A read mode leaves the directory byte for byte as it was — the plain counter's promise, kept by its sibling.</summary>
    [Fact]
    public void TheMode_WritesNothing_ToTheDataDirectoryOrBesideTheSnapshots()
    {
        Live(Key('a'), Now.AddHours(-1));
        var path = SnapshotOf(Wsl("Ubuntu"), "wsl", Round(1, Key('b'), Now.AddHours(-2), nameof(Stage.CodeReview), Ran("codex")));
        SqliteConnection.ClearAllPools();
        var before = Listing(_data.Path).Concat(Listing(_others.Path)).ToList();

        var (code, _, _) = Run(Merged(path));

        code.Should().Be(0);
        SqliteConnection.ClearAllPools();
        Listing(_data.Path).Concat(Listing(_others.Path)).Should().Equal(before);
        File.Exists(RoundCountsSnapshot.PathIn(_data.Path)).Should().BeFalse("a reader never writes the snapshot: only a round does");
    }

    private static IEnumerable<string> Listing(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            // SQLite's own sidecars of a WAL database a read-only connection opens: its work, not the mode's.
            .Where(entry => !entry.EndsWith("-wal", StringComparison.Ordinal) && !entry.EndsWith("-shm", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(root, entry)}|{(File.Exists(entry) ? new FileInfo(entry).Length : -1)}|{File.GetLastWriteTimeUtc(entry):O}")
            .Order(StringComparer.Ordinal);

    [Fact]
    public void TheModeIsKnown_InTheHelp_AndInTheOneShotList()
    {
        Program.Classify(["--round-counts-merged"]).Should().NotBe(Program.Startup.Usage, "64 is how the extension recognises a binary that predates the mode");
        Program.HelpText.Should().Contain("`--round-counts-merged");
        File.ReadAllText(Path.Combine(ProductionSources.RepositoryRoot(), ".agents", "PROJECT.md"))
            .Should().Contain("`--round-counts-merged`", "adding a one-shot mode means adding it to PROJECT.md's list");
    }

    [Fact]
    public void ThePlainMode_RowsGainTheirKeysTrailing_AndTheSnapshotIsStillNotWritten()
    {
        Live(Key('a'), Now.AddHours(-1));
        Live(string.Empty, Now.AddHours(-2));

        var (code, text, _) = Run("--round-counts", "--now", Now.ToString("O"));

        code.Should().Be(0);
        var row = JsonNode.Parse(text)!["windows"]![0]!["rows"]![0]!.AsObject();
        row.Select(p => p.Key).Should().Equal("provider", "rounds", "reviewers", "models", "roundKeys", "roundsWithoutKey");
        row["roundKeys"]!.AsArray().Select(k => k!.GetValue<string>()).Should().Equal(Key('a'));
        row["roundsWithoutKey"]!.GetValue<int>().Should().Be(1);
        File.Exists(RoundCountsSnapshot.PathIn(_data.Path)).Should().BeFalse();
    }
}

using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The engine's write point for the round-counts snapshot (E6.S1 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>, decision D2): a round
/// that is recorded leaves <c>round-counts-snapshot.json</c> beside <c>coai.db</c>, and a snapshot that cannot be written is one log
/// line — not a failed round, and not a round whose projection stops half way.
/// </summary>
/// <remarks>
/// Driven end to end through the real <see cref="PanelService"/> with FakeCli, like <see cref="ARoundIsMintedAKeyTests"/> and for the same
/// reason: the key is minted in the engine, written by the projection and read back by the snapshot, and no unit of the three can
/// prove the three agree. The file's own content and cap are <see cref="RoundCountsSnapshotTests"/>'s.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ARoundLeavesASnapshotBesideTheStoreTests : IAsyncLifetime
{
    private const string Clean = """{"findings": []}""";

    private readonly ProcessLauncher _launcher = new();
    private readonly ListSink _sink = new();
    private TempDir _repo = null!;
    private TempDir _data = null!;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private string Snapshot => RoundCountsSnapshot.PathIn(_data.Path);

    public async ValueTask InitializeAsync()
    {
        _repo = TempDir.For("testsToDelete-snapshot-repo-");
        _data = TempDir.For("testsToDelete-snapshot-data-");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", Clean);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", Clean);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(_repo.At("app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _repo.Dispose();
        _data.Dispose();

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo.Path));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service() =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _data.Path,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger(),
            Noticing.None);

    private async Task<JsonElement> PlanRound(PanelService service)
    {
        await service.OpenAsync(_repo.Path, "feature");

        return JsonDocument.Parse(await service.ReviewPlanAsync(_repo.Path, "feature", "a plan nobody objects to")).RootElement;
    }

    private string KeyInTheDatabase()
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_data.Path, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT round_key FROM rounds";

        return (string)read.ExecuteScalar()!;
    }

    private long ConsultMissedInTheDatabase()
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_data.Path, RoundsDb.FileName)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT consult_missed FROM rounds";

        return (long)read.ExecuteScalar()!;
    }

    [Fact]
    public async Task ARecordedRound_LeavesASnapshotBesideTheStore_WithItsKey()
    {
        var answer = await PlanRound(Service());

        answer.GetProperty("verdict").GetString().Should().Be("proceed", $"{answer}");
        File.Exists(Snapshot).Should().BeTrue("the snapshot is written after the round is recorded");
        var read = RoundCountsSnapshot.Read(Snapshot);
        read.Ok.Should().BeTrue($"{read.Refusal}");
        read.Document.Schema.Should().Be(1);
        var round = read.Document.Rounds.Should().ContainSingle().Which;
        round.Key.Should().Be(KeyInTheDatabase()).And.HaveLength(32, "the key the projection wrote");
        round.Stage.Should().Be("PlanReview");
        round.Reviewers.Should().NotBeEmpty();
        read.Document.Store.Id.Should().Be(RoundAuthorsMerge.SourceIdOf(read.Document.Store.Platform, read.Document.Store.WslDistro, _data.Path));
        Directory.EnumerateFiles(_data.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedSnapshotWrite_IsLogged_AndTheRoundStillAnswers()
    {
        Directory.CreateDirectory(Snapshot);

        var answer = await PlanRound(Service());

        answer.TryGetProperty("error", out var error).Should().BeFalse($"a courtesy file never takes a round down: {error}");
        answer.GetProperty("verdict").GetString().Should().Be("proceed");
        var warnings = _sink.Lines.Where(line => line.Contains("the round-counts snapshot could not be written", StringComparison.Ordinal)).ToList();
        warnings.Should().ContainSingle("one line, and in its own words: not the projection's");
        warnings[0].Should().NotContain(_data.Path);
        _sink.Lines.Should().NotContain(line => line.Contains("could not be projected", StringComparison.Ordinal), "the round DID project");
    }

    /// <summary>
    /// A snapshot written inside the projection's lambda would, when it failed, be logged as the ROUND failing to project and would skip
    /// the stuck-findings count after it (<c>RecordConsultMissed</c>, which keeps -1 for "not measured").
    /// </summary>
    [Fact]
    public async Task TheSnapshot_IsNotWrittenInsideTheProjection()
    {
        Directory.CreateDirectory(Snapshot);

        await PlanRound(Service());

        ConsultMissedInTheDatabase().Should().BeGreaterThanOrEqualTo(0, "the projection ran to its end although the snapshot could not be written");
    }
}

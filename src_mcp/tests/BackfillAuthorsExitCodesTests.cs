using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E7.S4 — the exit codes of <c>--backfill-authors</c>, one test each, and the rounds it counts per reason: 0 done (a missing store included), 65 a bad,
/// repeated or unknown argument, 74 a store or session file that will not read or write (or a named root that is missing), 75 a deferral — and never 64,
/// which means this binary does not know the mode. Through the real process, over temp stores.
/// </summary>
public sealed class BackfillAuthorsExitCodesTests
{
    private static BackfillWorld Stripped(string fixture = "resumed-agent")
    {
        var world = BackfillWorld.Of(fixture);
        world.RecordLive();
        world.StripAuthors();

        return world;
    }

    private static (int Code, string Out, string Err) Run(string dataDir, params string[] flags)
    {
        var info = OneShotProcess.StartInfo(dataDir, ["--backfill-authors", .. flags]);
        info.Environment["COAI_AGENT_LOG_DIR"] = Path.Combine(dataDir, "no-transcripts");

        return OneShotProcess.Run(info, TimeSpan.FromMinutes(2));
    }

    // ---------- 0 ----------

    [Fact]
    public void ADataDirectoryWithoutADatabase_IsExit0_AndNothingIsCreated()
    {
        using var temp = TempDir.For("testsToDelete-coai-backfill-empty-");

        var run = Run(temp);

        run.Code.Should().Be(0, run.Err);
        JsonDocument.Parse(run.Out).RootElement.GetProperty("selected").GetInt32().Should().Be(0);
        Directory.GetFileSystemEntries(temp).Should().BeEmpty("a missing database is nothing to backfill: no database, no backup folder, no claim, no session");
    }

    [Fact]
    public void EveryRoundAlreadyCurrent_IsExit0_NothingToDo()
    {
        using var world = Stripped();
        BackfillAuthorsCliScenarioTests.Run(world).Code.Should().Be(0);

        var again = BackfillAuthorsCliScenarioTests.Run(world);

        again.Code.Should().Be(0, again.Err);
    }

    [Fact]
    public async Task ADryRunOnAMissingStore_IsExit0WithoutMakingTheDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "testsToDelete-coai-backfill-missing-" + Guid.NewGuid().ToString("N"));
        var output = new StringWriter();

        var code = await BackfillAuthorsMode.RunAsync(["--backfill-authors", "--dry-run"], name => name == "COAI_DATA_DIR" ? path : null, output, _ => { }, TimeProvider.System);

        code.Should().Be(0);
        Directory.Exists(path).Should().BeFalse("the data directory is never created");
    }

    // ---------- 65 ----------

    [Theory]
    [InlineData("--since", "yesterday")]
    [InlineData("--since")]
    [InlineData("--since", "2026-09-30T00:00:00Z", "--since", "2026-09-30T00:00:00Z")]
    [InlineData("--dry-run", "--dry-run")]
    [InlineData("--nope")]
    [InlineData("2026-09-30T00:00:00Z")]
    public void ABadRepeatedOrUnknownArgument_Is65_WithTheUsageOnStderr(params string[] flags)
    {
        using var world = Stripped();
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var info = OneShotProcess.StartInfo(world.DataDir, ["--backfill-authors", .. flags]);
        var run = OneShotProcess.Run(info, TimeSpan.FromMinutes(1));

        run.Code.Should().Be(65, "this binary HAS the mode and is refusing the request");
        run.Err.Should().Contain("--backfill-authors takes");
        run.Out.Should().BeEmpty();
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
        Directory.Exists(Path.Combine(world.DataDir, BackfillBackup.FolderName)).Should().BeFalse();
    }

    // ---------- 74 ----------

    [Fact]
    public void ACoaiDbThatIsADirectory_Is74()
    {
        using var temp = TempDir.For("testsToDelete-coai-backfill-dbdir-");
        Directory.CreateDirectory(Path.Combine(temp, RoundsDb.FileName));

        var run = Run(temp);

        run.Code.Should().Be(74, run.Err);
        run.Code.Should().NotBe(64);
    }

    [Fact]
    public void ANamedDataFolderThatIsMissing_Is74_ByTheWriterModesRefusal()
    {
        var missing = Path.Combine(Path.GetTempPath(), "testsToDelete-coai-backfill-gone-" + Guid.NewGuid().ToString("N"));

        var run = Run(missing);

        run.Code.Should().Be(74, run.Err);
        Directory.Exists(missing).Should().BeFalse("the refusal comes before the mode runs and creates nothing");
    }

    [Fact]
    public void ASessionFileThatCannotBeWritten_Is74_AndTheStoreIsKept()
    {
        using var world = Stripped();
        var file = Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").Single();
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var run = BackfillAuthorsCliScenarioTests.Run(world);

            run.Code.Should().Be(74, run.Err);
            StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds").Should().Be("\n", "nothing reached the database for the round whose file could not be written");
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    // ---------- 75 ----------

    [Fact]
    public void ASessionHeldByALiveRound_Is75_WithTheSummaryPrintedAndNothingWritten()
    {
        using var world = Stripped();
        var gate = world.Fixture.Rounds.GateSessions[0];
        using var claim = SessionClaim.TryTake(world.DataDir, world.RepoOf(gate), BackfillWorld.BranchOf(gate));
        claim.Should().NotBeNull();
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var run = BackfillAuthorsCliScenarioTests.Run(world);

        run.Code.Should().Be(75, run.Err);
        var summary = JsonDocument.Parse(run.Out).RootElement;
        summary.GetProperty("deferred").GetProperty("busy").GetInt32().Should().Be(1);
        summary.GetProperty("next").GetString().Should().Contain("run the same command again");
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
    }

    // ---------- --since ----------

    [Fact]
    public void ARoundStartedBeforeSince_IsUntouched()
    {
        using var world = Stripped();
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var run = BackfillAuthorsCliScenarioTests.Run(world, "--since", "2001-01-01T00:00:00Z");

        run.Code.Should().Be(0, run.Err);
        JsonDocument.Parse(run.Out).RootElement.GetProperty("selected").GetInt32().Should().Be(0);
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
        JsonDocument.Parse(run.Out).RootElement.GetProperty("since").GetString().Should().StartWith("2001-01-01T00:00:00");
    }

    // ---------- no mode path answers 64 ----------

    [Fact]
    public void NoPathThroughTheModeAnswers64()
    {
        using var world = Stripped();
        using var empty = TempDir.For("testsToDelete-coai-backfill-sweep-");
        var codes = new List<int>
        {
            Run(empty).Code,
            Run(world.DataDir, "--dry-run").Code,
            BackfillAuthorsCliScenarioTests.Run(world, "--dry-run").Code,
            BackfillAuthorsCliScenarioTests.Run(world).Code,
            BackfillAuthorsCliScenarioTests.Run(world, "--bogus").Code,
        };

        codes.Should().NotContain(64);
    }

    // ---------- text-free ----------

    [Fact]
    public void NoSentinelReachesStdoutStderrTheDatabaseOrTheSessionFiles()
    {
        using var world = Stripped();

        var run = BackfillAuthorsCliScenarioTests.Run(world);

        run.Code.Should().Be(0, run.Err);
        run.Out.Should().NotContain(FixtureManifest.Sentinel);
        run.Err.Should().NotContain(FixtureManifest.Sentinel);
        SqliteConnection.ClearAllPools();
        var bytes = Directory.GetFiles(world.DataDir, RoundsDb.FileName + "*").SelectMany(File.ReadAllBytes).ToArray();
        bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(FixtureManifest.Sentinel)).Should().Be(-1);
        Directory.GetFiles(world.DataDir, "*", SearchOption.AllDirectories).Where(file => file.EndsWith(".json", StringComparison.Ordinal))
            .Select(File.ReadAllText).Should().NotContain(text => text.Contains(FixtureManifest.Sentinel, StringComparison.Ordinal));
        JsonDocument.Parse(run.Out).RootElement.GetProperty("written").GetProperty("computed").GetInt32().Should().BeGreaterThan(0, "positive control: the fixture was read");
    }

    // ---------- rounds counted per reason ----------

    [Fact]
    public void UnrecoverableRoundsAreCountedPerReason()
    {
        using var world = BackfillWorld.Of("persistence-off");
        RecordCaller(world, "g-1", "11111111-1111-1111-1111-111111111111", "claude");       // no transcript anywhere
        RecordCaller(world, "g-2", "repo:/some/where", "claude");                           // not a session id
        RecordCaller(world, "g-3", "22222222-2222-2222-2222-222222222222", "codex");        // no reader for the vendor
        RecordCaller(world, "g-4", "33333333-3333-3333-3333-333333333333", "claude");       // the id in two folders
        foreach (var folder in (string[])["one", "two"])
        {
            Directory.CreateDirectory(Path.Combine(world.Fixture.Home.ProjectsDir, folder));
            File.WriteAllText(Path.Combine(world.Fixture.Home.ProjectsDir, folder, "33333333-3333-3333-3333-333333333333.jsonl"), string.Empty);
        }

        var run = BackfillAuthorsCliScenarioTests.Run(world);

        run.Code.Should().Be(0, run.Err);
        var summary = JsonDocument.Parse(run.Out).RootElement;
        var states = summary.GetProperty("unobserved").EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetInt32());
        states["transcript not found"].Should().Be(1, "the round whose id names no transcript anywhere");
        states["no session id"].Should().Be(1);
        states["no reader for codex"].Should().Be(1);
        states["transcript in two folders"].Should().Be(1);
        StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds WHERE session_id = 'g-2'").Should().Be("unobserved: no session id\n", "each round's own state names it");
        summary.GetProperty("size").GetProperty("unrecoverable").GetProperty("size: no base recorded").GetInt32().Should().BeGreaterThan(0);
    }

    private static void RecordCaller(BackfillWorld world, string session, string caller, string vendor)
    {
        var repo = Path.Combine(world.Fixture.Root, "repo-a");
        var state = new SessionState(session, repo, "feat/" + session, new PanelConfig()) { Stage = Stage.CodeReview };
        var started = new DateTime(2000, 1, 1, 0, 0, 10, DateTimeKind.Utc);
        var round = new RoundRecord("CodeReview", 1, "proceed", 0, "answered", started.AddSeconds(5))
        {
            StartedUtc = started,
            Caller = new CallerDeclaration(Vendor: vendor),
        };
        world.Store.Save(new PersistedSession(state, [round]) { OpenedUtc = started.AddSeconds(-5) });
        using var db = RoundsDb.Open(world.DataDir, Serilog.Core.Logger.None)!;
        db.RecordRound(state, round, [], new RoundContext(Caller: caller));
    }
}

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
/// E7.S4 — <c>coai-mcp --backfill-authors</c>, end to end through the REAL process: a store recorded through the live writers from E1's fixtures,
/// stripped of its authors, then backfilled twice. The parent's acceptance is the scenario: the second run changes no totals, and the newer row and every
/// resolution survive. Every data directory is a <c>testsToDelete-</c> temp folder and the process is handed it explicitly — no live store is named.
/// </summary>
public sealed class BackfillAuthorsCliScenarioTests
{
    private const string Fixture = "resumed-agent";
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    /// <summary>One run of the built binary against <paramref name="world"/>'s data directory and its transcripts.</summary>
    internal static (int Code, string Out, string Err) Run(BackfillWorld world, params string[] flags)
    {
        var info = OneShotProcess.StartInfo(world.DataDir, ["--backfill-authors", .. flags]);
        info.Environment["COAI_AGENT_LOG_DIR"] = world.Fixture.Home.ProjectsDir;

        return OneShotProcess.Run(info, Deadline);
    }

    internal static JsonElement Summary(string stdout) => JsonDocument.Parse(stdout).RootElement.Clone();

    internal static int Int(JsonElement root, params string[] path)
    {
        var at = root;
        foreach (var name in path)
        {
            at = at.GetProperty(name);
        }

        return at.GetInt32();
    }

    private static BackfillWorld Stripped(string fixture = Fixture)
    {
        var world = BackfillWorld.Of(fixture);
        world.RecordLive();
        world.StripAuthors();

        return world;
    }

    private static string Everything(BackfillWorld world) =>
        StoreDump.AuthorsAndSize(world.DataDir) + StoreDump.EverythingElse(world.DataDir)
        + string.Concat(Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").Order(StringComparer.Ordinal).Select(File.ReadAllText));

    // ---------- the scenario ----------

    [Fact]
    public void BackfillTwice_TheSecondRunChangesNoTotals()
    {
        using var world = Stripped();

        var first = Run(world);
        var afterFirst = Everything(world);
        var second = Run(world);

        first.Code.Should().Be(0, first.Err);
        second.Code.Should().Be(0, second.Err);
        var one = Summary(first.Out);
        var two = Summary(second.Out);
        Int(one, "written", "computed").Should().BeGreaterThan(0, "positive control: run 1 wrote");
        foreach (var bucket in new[] { "computed", "recomputedProvisional", "recomputedSnapshotLost", "reprojected", "databaseOnly", "corrected" })
        {
            Int(two, "written", bucket).Should().Be(0, $"run 2 writes nothing: {bucket}");
        }

        Int(two, "healed").Should().Be(0);
        Int(two, "kept", "equalOrNewer").Should().Be(Int(one, "selected"), "run 2 reads every row run 1 wrote as keep — not recompute and find it equal");
        Everything(world).Should().Be(afterFirst, "the SHA-256 of a canonical dump of the authors and size tables, and every session file, is identical after both runs");
        Int(two, "readerVersion").Should().Be(ObservedSession.ReaderVersion);
        Int(two, "attributionVersion").Should().Be(RoundAttribution.Version);
    }

    [Fact]
    public void ANewerReadersRowAndEveryResolutionSurviveTwoRuns()
    {
        using var world = BackfillWorld.Of(Fixture);
        world.RecordLive();
        var gate = world.Fixture.Rounds.GateSessions[0];
        var file = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!;
        var newer = ObservedSession.ReaderVersion + 1;
        world.Store.Save(file with { Rounds = [.. file.Rounds.Select(round => round with { Authors = round.Authors! with { Reader = newer } })] });
        using (var db = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, RoundsDb.FileName)};Pooling=False"))
        {
            db.Open();
            BackfillWorld.Run(db, $"UPDATE rounds SET authors_reader = {newer}");
        }

        SqliteConnection.ClearAllPools();
        var before = Everything(world);

        var first = Run(world);
        var second = Run(world);

        first.Code.Should().Be(0, first.Err);
        second.Code.Should().Be(0, second.Err);
        Int(Summary(first.Out), "kept", "equalOrNewer").Should().Be(1);
        Everything(world).Should().Be(before, "the newer row, the resolutions and every other byte are identical after two runs");
    }

    // ---------- the provisional rows are the ones recomputed ----------

    private const string Since = "2000-01-01T00:00:00Z";

    /// <summary>
    /// A gate session whose row says <c>partial: peer running</c> and names one peer; the peer's last round is <paramref name="peerStatus"/>. The peer's
    /// own rounds started before <c>--since</c>, so the run looks at the gate's row alone.
    /// </summary>
    private static BackfillWorld WithAPeerRunningRow(string peerStatus)
    {
        var world = BackfillWorld.Of("work-before-open");
        world.RecordLive();
        var gate = world.Fixture.Rounds.GateSessions[0];
        var start = gate.Rounds[0].StartedUtc;
        const string peer = "peer-gate-00000000";
        new PeerWorld(world.DataDir, world.Caller).Add(
            peer,
            start.AddHours(-3),
            [
                new RoundRecord("CodeReview", 1, "proceed", 0, "answered", start.AddMinutes(-9)) { StartedUtc = start.AddMinutes(-10), Caller = new CallerDeclaration(Vendor: "claude") },
                new RoundRecord("CodeReview", 2, "proceed", 0, "answered", start.AddMinutes(-4)) { StartedUtc = start.AddMinutes(-5), Status = peerStatus, Caller = new CallerDeclaration(Vendor: "claude") },
            ]);
        var file = world.Store.Load(world.RepoOf(gate), BackfillWorld.BranchOf(gate))!;
        world.Store.Save(file with
        {
            Rounds = [.. file.Rounds.Select(round => round with { Authors = round.Authors! with { State = ObservationKinds.Partial, Reasons = [AttributionReasons.PeerRunning], PeerGates = [peer] } })],
        });
        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, RoundsDb.FileName)};Pooling=False");
        db.Open();
        BackfillWorld.Run(db, $"UPDATE rounds SET authors_state = 'partial: peer running' WHERE session_id = '{gate.Id}'");

        return world;
    }

    [Theory]
    [InlineData(RoundRecord.Done)]
    [InlineData(RoundRecord.Interrupted)]
    public void ARowWhoseNamedPeerRoundsAreFinal_IsRecomputed(string peerStatus)
    {
        using var world = WithAPeerRunningRow(peerStatus);

        var run = Run(world, "--since", Since);

        run.Code.Should().Be(0, run.Err);
        var summary = Summary(run.Out);
        Int(summary, "selected").Should().Be(1);
        Int(summary, "written", "recomputedProvisional").Should().Be(1, "a done and an interrupted peer round both release the row");
        Int(summary, "kept", "peerStillRunning").Should().Be(0);
        StoreDump.Rows(world.DataDir, "SELECT authors_state FROM rounds WHERE session_id = '" + world.Fixture.Rounds.GateSessions[0].Id + "'").Should().NotContain("peer running");
        Int(Summary(Run(world, "--since", Since).Out), "written", "recomputedProvisional").Should().Be(0, "and a second run leaves it alone");
    }

    [Fact]
    public void ARowWhoseNamedPeerRoundStillRuns_IsKept_AndTheBackfillNeverSweeps()
    {
        using var world = WithAPeerRunningRow(RoundRecord.Running);
        var before = StoreDump.AuthorsAndSize(world.DataDir);

        var run = Run(world, "--since", Since);

        run.Code.Should().Be(0, run.Err);
        var summary = Summary(run.Out);
        Int(summary, "kept", "peerStillRunning").Should().Be(1);
        Int(summary, "written", "recomputedProvisional").Should().Be(0);
        StoreDump.AuthorsAndSize(world.DataDir).Should().Be(before);
        StoreDump.Rows(world.DataDir, "SELECT status FROM rounds WHERE session_id = 'peer-gate-00000000' ORDER BY number").Should().Be("done\nrunning\n", "the dead running row is the startup sweep's to turn into interrupted - never the backfill's");
    }
}

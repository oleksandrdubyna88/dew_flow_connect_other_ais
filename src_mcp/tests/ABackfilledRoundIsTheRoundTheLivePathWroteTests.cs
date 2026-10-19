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
/// E7.S2 — a stored round's history, rebuilt exactly as the live path sees it: the same open time (the session FILE's), the same timeline (one
/// mapping), the same peers (one lookup), the same reader (one read per caller) — so a backfilled snapshot is the snapshot the live path wrote.
/// </summary>
/// <remarks>Everything runs over <see cref="BackfillWorld"/>: E1's fixtures made into a store through the live writers, then stripped of their authors.</remarks>
public sealed class ABackfilledRoundIsTheRoundTheLivePathWroteTests
{
    public static IEnumerable<TheoryDataRow<string>> EveryFixture() =>
        FixtureManifest.Cases.Select(fixture => new TheoryDataRow<string>(fixture.Name) { TestDisplayName = fixture.Name });

    /// <summary>
    /// The one case whose journal lists a number twice: live, E4 refuses the duplicated round (<c>unobserved: invalid input</c>); the backfill builds
    /// the journal BEFORE the round and attributes it — stronger, never weaker, and the rows are not comparable.
    /// </summary>
    private const string ReplayCase = "replay-same-round-twice";

    private static int Strength(AuthorsSnapshot snapshot) => BackfillDecision.Strength(snapshot.Render());

    private static CallerReads Reader() => new(CallerTranscript.Read);

    // ---------- parity, the main one ----------

    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void ABackfilledRound_HasTheLiveRowsForEveryWindowUpToIt(string fixture)
    {
        using var world = BackfillWorld.Of(fixture);
        world.RecordLive();
        world.StripAuthors();

        var computed = BackfillDriver.Run(world, Reader());

        computed.Should().NotBeEmpty();
        foreach (var round in computed)
        {
            var live = world.Live[(round.Round.SessionId, round.Round.Stage, round.Round.Number)];
            round.Action.Verb.Should().Be(BackfillVerb.Compute, "a store stripped of its authors is computed whole");
            Strength(round.Snapshot!).Should().BeGreaterThanOrEqualTo(Strength(live), "a backfill is never weaker than the live row");
            if (fixture != ReplayCase)
            {
                AuthorsProjection.Of(round.Snapshot!).Should().Equal(AuthorsProjection.Of(live), $"{fixture}: the columns and every row of the round");
            }
        }
    }

    // ---------- the open time ----------

    [Fact]
    public void TheOpenTimeIsTheSessionFiles_NotTheFirstProjections()
    {
        using var world = BackfillWorld.Of("work-before-open");
        world.RecordLive();
        var opened = world.Fixture.Rounds.GateSessions[0].OpenedUtc;
        SqliteConnection.ClearAllPools();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, RoundsDb.FileName)};Pooling=False"))
        {
            db.Open();
            BackfillWorld.Run(db, $"UPDATE sessions SET opened_utc = '{opened.AddHours(1):O}'");
        }

        world.StripAuthors();
        var computed = BackfillDriver.Run(world, Reader()).Single();

        AuthorsProjection.Of(computed.Snapshot!).Should().Equal(
            AuthorsProjection.Of(world.Live.Values.Single()),
            "the first window's tokens are the live path's — the first projection's clock is an hour later and would shorten it");
        computed.Facts.Before.OpenedUtc.Should().Be(opened);
    }

    // ---------- one timeline whichever source filled it ----------

    [Fact]
    public void TheTimelineIsOneShape_WhicheverSourceFilledIt()
    {
        using var world = BackfillWorld.Of("skipped-round-between-recorded");
        world.RecordLive();
        world.StripAuthors();
        var gate = world.Fixture.Rounds.GateSessions[0];

        var fromFile = Timeline(world, gate);
        File.Delete(Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").Single());
        var fromDatabase = Timeline(world, gate);

        fromFile.Rounds.Should().NotBeEmpty();
        fromDatabase.Rounds.Should().Equal(fromFile.Rounds, "both go through AttributionMapping.Timeline and carry the same rounds in the same shape");
    }

    private static GateTimeline Timeline(BackfillWorld world, FixtureGateSession gate)
    {
        using var db = AuthorPeers.OpenReadOnly(world.DataDir)!;
        var stored = new StoredHistory(db, world.Store).Gates().Single(candidate => candidate.SessionId == gate.Id);

        return AttributionMapping.Timeline(stored.SessionId, stored.RepoPath, stored.OpenedUtc, stored.Journal, _ => world.Caller);
    }

    // ---------- the peers are the live path's peers ----------

    [Fact]
    public void ThePeersAreTheLivePathsPeers()
    {
        using var world = BackfillWorld.Of("second-gate-session-same-caller");
        var seenLive = new List<AttributionInput>();
        world.RecordLive(assign: input =>
        {
            seenLive.Add(input);

            return RoundAttribution.Assign(input);
        });
        var loads = new List<string>();
        var seenBackfilled = new List<AttributionInput>();
        world.StripAuthors();

        BackfillDriver.Run(
            world,
            Reader(),
            assign: input =>
            {
                seenBackfilled.Add(input);

                return RoundAttribution.Assign(input);
            },
            load: (repo, branch) =>
            {
                loads.Add(repo + "|" + branch);

                return world.Store.Load(repo, branch);
            });

        var second = world.Fixture.Rounds.GateSessions[1];
        var live = seenLive.Last(input => input.Gate.GateSessionId == second.Id);
        var backfilled = seenBackfilled.Last(input => input.Gate.GateSessionId == second.Id);
        Json(backfilled.Peers).Should().Be(Json(live.Peers), "the peers handed to Assign are the ones the live step handed it on the same store");
        backfilled.PeersKnown.Should().BeTrue();
        loads.Should().OnlyHaveUniqueItems("AuthorPeers.Find is called once per round — one lookup, shared by the decision and the compute, never one per pass");
        loads.Count.Should().Be(2, "each of the two rounds has exactly one peer file to load");
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    [Fact]
    public void ARunningPeerRound_IsSeenFromItsFile_AndNamedStillRunning()
    {
        using var world = BackfillWorld.Of("work-before-open");
        world.RecordLive();
        world.StripAuthors();
        var peer = AddPeer(world, RoundRecord.Running);

        var (facts, _) = FactsOf(world, named: [peer]);

        facts.PeerStatus.PeersKnown.Should().BeTrue();
        facts.PeerStatus.PeerIds.Should().Contain(peer, "the peer is found through the store and BUILT from its session file");
        facts.PeerStatus.ANamedPeerRoundStillRuns.Should().BeTrue("its round is running in the file and overlaps this round's window");
    }

    [Theory]
    [InlineData(RoundRecord.Done)]
    [InlineData(RoundRecord.Interrupted)]
    public void APeerRoundThatIsFinal_ReleasesAPeerRunningRow(string status)
    {
        using var world = BackfillWorld.Of("work-before-open");
        world.RecordLive();
        world.StripAuthors();
        var peer = AddPeer(world, status);

        var (facts, _) = FactsOf(world, named: [peer]);

        facts.PeerStatus.ANamedPeerRoundStillRuns.Should().BeFalse($"a `{status}` peer round is final: only `running` holds a row back — an interrupted one never becomes done");
    }

    /// <summary>Another gate session of the caller whose last round overlaps the fixture's, written through the same writers; returns its id.</summary>
    private static string AddPeer(BackfillWorld world, string lastStatus)
    {
        var gate = world.Fixture.Rounds.GateSessions[0];
        var start = gate.Rounds[0].StartedUtc;
        const string id = "peer-gate-00000000";
        var peer = new PeerWorld(world.DataDir, world.Caller);
        peer.Add(
            id,
            start.AddHours(-3),
            [
                new RoundRecord("CodeReview", 1, "proceed", 0, "answered", start.AddMinutes(-20)) { StartedUtc = start.AddMinutes(-30), Caller = new CallerDeclaration(Vendor: "claude") },
                new RoundRecord("CodeReview", 2, "proceed", 0, "answered", start.AddMinutes(-1)) { StartedUtc = start.AddMinutes(-5), Status = lastStatus, Caller = new CallerDeclaration(Vendor: "claude") },
            ]);

        return id;
    }

    private static (RoundFacts Facts, StoredGate Gate) FactsOf(BackfillWorld world, System.Collections.Immutable.ImmutableArray<string> named)
    {
        using var db = AuthorPeers.OpenReadOnly(world.DataDir)!;
        var attribution = new BackfillAttribution(world.Settings, Serilog.Core.Logger.None, world.Store, Reader(), TimeProvider.System);
        var gate = new StoredHistory(db, world.Store).Gates().Single(candidate => candidate.SessionId == world.Fixture.Rounds.GateSessions[0].Id);
        var round = gate.Rounds.Single();
        var probe = new StoredFacts(db, world.Settings, attribution).Of(gate, round);
        var presence = probe.Presence with { HasAuthors = true, AuthorsState = "partial: peer running", PeerGates = named, IdSource = "registry", Reader = ObservedSession.ReaderVersion, Attribution = RoundAttribution.Version };
        var set = attribution.PeersOf(db, probe.CallerId, gate.SessionId);

        return (probe with { Peers = set, PeerStatus = attribution.StatusOf(probe.Before, probe.Record, probe.CallerId, set, presence) }, gate);
    }

    // ---------- the file is not always there ----------

    [Fact]
    public void ARound1WithoutItsFile_IsUnobservedNoOpenTime_AndRound2IsStillComputed_InTheDatabaseOnly()
    {
        using var world = BackfillWorld.Of("skipped-round-between-recorded");
        world.RecordLive();
        world.StripAuthors();
        File.Delete(Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").Single());

        var computed = BackfillDriver.Run(world, Reader());

        computed.Should().HaveCount(2);
        computed.Should().OnlyContain(round => round.Facts.Presence.FileState == SessionFileState.Gone && !round.Facts.Presence.Holds, "no file: nothing to read, nothing to heal");
        computed[0].Snapshot!.Render().Should().Be("unobserved: no open time", "round 1 has no open time without the file — E4's own reason, none of ours");
        computed[1].Snapshot.Should().NotBeNull("round 2's window starts at round 1's START, which the database holds");
        computed[1].Snapshot!.Render().Should().NotContain("no open time");
    }

    [Fact]
    public void AReplacedSessionFile_IsTreatedAsNoFile()
    {
        using var world = BackfillWorld.Of("work-before-open");
        world.RecordLive();
        var gate = world.Fixture.Rounds.GateSessions[0];
        world.Store.Save(new PersistedSession(new SessionState("another-session", world.RepoOf(gate), BackfillWorld.BranchOf(gate), new PanelConfig()), []) { OpenedUtc = DateTime.UtcNow });
        world.StripAuthors();

        var computed = BackfillDriver.Run(world, Reader()).Single();

        computed.Facts.Presence.FileState.Should().Be(SessionFileState.Replaced);
        computed.Facts.Presence.Holds.Should().BeFalse();
        computed.Snapshot.Should().NotBeNull("the round is computed from the database rows");
    }

    [Fact]
    public void AnExistingButUnreadableSessionFile_IsUnreadableNotGone()
    {
        using var world = BackfillWorld.Of("work-before-open");
        world.RecordLive();
        File.WriteAllText(Directory.GetFiles(Path.Combine(world.DataDir, "sessions"), "session-*.json").Single(), "{ torn");
        using var db = AuthorPeers.OpenReadOnly(world.DataDir)!;

        var gate = new StoredHistory(db, world.Store).Gates().Single();

        gate.FileState.Should().Be(SessionFileState.Unreadable, "SessionStore.Load answers null for a torn file as for a missing one; Exists tells them apart");
    }

    // ---------- the reader reads once ----------

    [Fact]
    public void EachCallersTranscriptIsReadOnce()
    {
        using var world = BackfillWorld.Of("second-gate-session-same-caller");
        world.RecordLive();
        world.StripAuthors();
        var counted = 0;
        var reads = new CallerReads((where, budget, bound) =>
        {
            counted++;

            return CallerTranscript.Read(where, budget, bound);
        });

        var computed = BackfillDriver.Run(world, reads);

        computed.Should().HaveCount(2).And.OnlyContain(round => round.Snapshot != null);
        counted.Should().Be(1, "two rounds of one caller are attributed from one read, taken at the lowest bound either needs");
        reads.Reads.Should().Be(1);
    }

    // ---------- unsettled rounds never reach Assign ----------

    [Fact]
    public void NoUnsettledRound_ReachesAssign()
    {
        using var world = BackfillWorld.Of("work-before-open");
        world.RecordLive();
        var gate = world.Fixture.Rounds.GateSessions[0];
        var round = gate.Rounds[0];
        var all = new List<RoundRecord> { BackfillWorld.RoundOf(round) };
        foreach (var (number, status, verdict) in new[] { (2, RoundRecord.Done, RoundRecord.Skipped), (3, RoundRecord.Interrupted, "proceed"), (4, RoundRecord.Running, "proceed") })
        {
            var later = new FixtureRound(number, round.StartedUtc.AddMinutes(number), round.CompletedUtc.AddMinutes(number));
            var record = BackfillWorld.RoundOf(later, status: status, verdict: verdict);
            all.Add(record);
            world.Record(gate, all, record);
        }

        world.StripAuthors();
        var assigned = new List<int>();

        var computed = BackfillDriver.Run(world, Reader(), assign: input =>
        {
            assigned.Add(input.Number);

            return RoundAttribution.Assign(input);
        });

        assigned.Distinct().Should().Equal([1], "only the settled round is attributed — a skipped, an interrupted and a running round never reach Assign");
        computed.Where(round => round.Round.Number > 1).Should().OnlyContain(round => round.Snapshot == null && round.Action.Verb == BackfillVerb.NotSelected);
    }

    // ---------- a codex caller ----------

    [Fact]
    public void ACodexCaller_IsLocatedAsNoReaderForCodex()
    {
        using var world = BackfillWorld.Of("work-before-open");

        var location = CallerTranscriptLocator.LocateStored("codex", world.Caller, world.Settings.TranscriptHome);

        location.Should().BeOfType<CallerTranscriptLocation.NotObservable>().Which.Reason.Should().Be("no reader for codex");
        CallerTranscriptLocator.LocateStored("claude", world.Caller, world.Settings.TranscriptHome).Should().BeOfType<CallerTranscriptLocation.Found>();
        CallerTranscriptLocator.LocateStored("claude", "repo:/some/path", world.Settings.TranscriptHome).Should().BeOfType<CallerTranscriptLocation.NotObservable>().Which.Reason.Should().Be("no session id");
    }

    // ---------- text-free ----------

    [Fact]
    public void NoSentinelReachesAnyRenderingOfABackfilledRound()
    {
        using var world = BackfillWorld.Of("work-before-open");
        world.RecordLive();
        world.StripAuthors();

        var computed = BackfillDriver.Run(world, Reader()).Single();

        var rendered = string.Join('\n', AuthorsProjection.Of(computed.Snapshot!)) + JsonSerializer.Serialize(computed.Snapshot);
        rendered.Should().NotContain(FixtureManifest.Sentinel);
        computed.Snapshot!.Rows.Should().NotBeEmpty("a positive control: the fixture was read, so the absence means something");
    }
}

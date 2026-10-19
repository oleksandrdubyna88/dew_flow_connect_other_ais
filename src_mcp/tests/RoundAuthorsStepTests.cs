using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The authors step (E5.S4 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>) over E1's real fixtures in a temp home:
/// what it records for a settled round, what it refuses to record, what it hands E4 and under which budget. The identity is always the
/// step's INJECTED environment — no test here sets a caller-identity variable on the process — and the home is always a temp folder.
/// </summary>
public sealed class RoundAuthorsStepTests : IDisposable
{
    private static readonly DateTime T0 = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _owned.ForEach(owned => owned.Dispose());
    }

    private AuthorsStepHarness Harness(string fixture)
    {
        var harness = AuthorsStepHarness.Of(fixture);
        _owned.Add(harness);

        return harness;
    }

    /// <summary>Hands E4 the input and records it: the "spy on Assign" of the plan is this delegate, never a hook on the static.</summary>
    private sealed class AssignSpy
    {
        public List<AttributionInput> Inputs { get; } = [];

        public WindowAssignment Run(AttributionInput input)
        {
            Inputs.Add(input);

            return RoundAttribution.Assign(input);
        }
    }

    private static RoundRecord PeerRound(int number, DateTime started, DateTime completed, string status = RoundRecord.Done) =>
        new("CodeReview", number, "proceed", 0, "answered", completed) { Status = status, StartedUtc = started };

    // ---------- what a settled round records ----------

    [Fact]
    public void ASonnetWriterIntoTheRepo_IsObserved_WithItsRow_BothVersions_AndTheWindows()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var round = h.Round(1);

        var snapshot = h.Step(peers: AuthorsStepHarness.AlwaysNoPeers).For(h.SessionBefore(round), round)!;

        snapshot.Render().Should().Be("observed");
        snapshot.Rows.Should().Contain(row => row.Who == "subagent" && row.Model == "claude-sonnet-5-5" && row.Association == Association.WroteHere && row.WriteCalls == 1,
            "the fixture's resumed agent is a Sonnet writer into the checkout: its row says who, which model and why it is credited");
        snapshot.Rows.Should().Contain(row => row.Who == "main" && row.Model == "claude-opus-5-5");
        snapshot.IdSource.Should().Be(CallerSessionId.Environment, "the id came through the injected CLAUDE_CODE_SESSION_ID");
        snapshot.Session.Should().Be(h.Caller);
        (snapshot.Reader, snapshot.Attribution).Should().Be((ObservedSession.ReaderVersion, RoundAttribution.Version));
        snapshot.Windows.Select(window => window.Window).Should().Equal([WindowKind.BeforeOpen, WindowKind.Round], "E4's windows, as it returned them");
        snapshot.PeerGates.Should().BeEmpty();
        (snapshot.SubmoduleWrites, snapshot.UncoveredWrites).Should().Be((0, 0), "a measured zero: attribution ran");
        snapshot.Stage.Should().Be("CodeReview");
        snapshot.Number.Should().Be(1);
    }

    [Theory]
    [InlineData("persistence-off", "claude", "environment", "unobserved: transcript not found")]
    [InlineData("resumed-agent", "claude", "none", "unobserved: no session id")]
    [InlineData("resumed-agent", "claude", "stated", "unobserved: stated id")]
    [InlineData("resumed-agent", "codex", "environment", "unobserved: no reader for codex")]
    public void ARoundNobodyCanObserve_IsAnUnknownSnapshot_AndNeverOpensTheStore(string fixture, string vendor, string identity, string state)
    {
        using var h = AuthorsStepHarness.Of(fixture);
        var queries = 0;
        var reads = 0;
        var round = h.Round(1) with { Caller = new CallerDeclaration(Vendor: vendor) };
        Func<string, string?> environment = identity switch
        {
            "none" => _ => null,
            "stated" => name => name == "COAI_CALLER_SESSION" ? "an operator's string" : null,
            _ => h.Environment,
        };
        var step = h.Step(
            environment,
            peers: (_, _) =>
            {
                queries++;

                return AuthorsStepHarness.NoPeers;
            },
            read: (location, budget, from) =>
            {
                reads++;

                return CallerTranscript.Read(location, budget, from);
            });

        var snapshot = step.For(h.SessionBefore(round), round)!;

        snapshot.Render().Should().Be(state);
        queries.Should().Be(0, "a location that is not observable ends the step before the peer query");
        (snapshot.SubmoduleWrites, snapshot.UncoveredWrites, snapshot.UnobservedLaunches).Should().Be((-1, -1, -1));
        snapshot.Ambiguous.Should().Be(ActivityTotals.Unknown);
        snapshot.Rows.Should().BeEmpty();
        (snapshot.Reader, snapshot.Attribution).Should().Be((0, 0), "neither component ran");
        reads.Should().Be(0, "the reader is never called for a location that is not observable — nothing is opened");
    }

    [Fact]
    public void TheIdentityIsTheInjectedEnvironmentAlone()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var reads = 0;
        var round = h.Round(1);

        var none = h.Step(_ => null, peers: AuthorsStepHarness.AlwaysNoPeers).For(h.SessionBefore(round), round)!;
        var stranger = h.Step(
                name => name == "CLAUDE_CODE_SESSION_ID" ? Guid.NewGuid().ToString("D") : null,
                peers: AuthorsStepHarness.AlwaysNoPeers,
                read: (location, budget, from) =>
                {
                    reads++;

                    return CallerTranscript.Read(location, budget, from);
                })
            .For(h.SessionBefore(round), round)!;

        none.Render().Should().Be("unobserved: no session id", "whatever the test process holds — run inside Claude Code it holds a real session id");
        stranger.Render().Should().Be("unobserved: transcript not found", "a real-looking id against an empty home names no file");
        reads.Should().Be(0, "the reader spy is never called: the id names no file");
    }

    // ---------- which rounds ----------

    [Theory]
    [InlineData("PlanReview", "proceed", RoundRecord.Running)]
    [InlineData("CodeReview", "skipped", RoundRecord.Done)]
    [InlineData("CodeReview", "proceed", RoundRecord.Interrupted)]
    [InlineData("FeatureReview", "proceed", RoundRecord.Done)]
    [InlineData("DocumentReview", "proceed", RoundRecord.Done)]
    public void ARoundThatIsNotSettled_GetsNothing_AndIsNeverHandedToAssign(string stage, string verdict, string status)
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var spy = new AssignSpy();
        var round = h.Round(1, stage, verdict, status);

        var snapshot = h.Step(peers: AuthorsStepHarness.AlwaysNoPeers, assign: spy.Run).For(h.SessionBefore(round), round);

        snapshot.Should().BeNull("not recorded: a skipped, interrupted or running round and every feature or document round");
        spy.Inputs.Should().BeEmpty("Assign refuses what is not settled — it is never asked");
    }

    [Fact]
    public void ASettledPlanRound_GetsASnapshotToo()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var round = h.Round(1, "PlanReview");

        h.Step(peers: AuthorsStepHarness.AlwaysNoPeers).For(h.SessionBefore(round), round).Should().NotBeNull();
    }

    // ---------- what is handed to E4 ----------

    [Fact]
    public void TheGateTimelineHoldsTheRoundBeingAttributed_WhichTheSessionDoesNot()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var spy = new AssignSpy();
        var round = h.Round(1);
        var session = h.SessionBefore(round);

        var snapshot = h.Step(peers: AuthorsStepHarness.AlwaysNoPeers, assign: spy.Run).For(session, round)!;

        session.Rounds.Should().BeEmpty("the save has not appended it");
        var rounds = spy.Inputs.Single().Gate.Rounds;
        (rounds[^1].Stage, rounds[^1].Number).Should().Be((round.Stage, round.Number));
        snapshot.Render().Should().Be("observed", "without the append Assign would refuse every live round as unobserved: invalid input");
    }

    [Fact]
    public void AnEarlierRoundsCaller_ComesFromItsSnapshot_ThenTheStore_ThenAUuidCaller_ThenNothing()
    {
        using var h = Harness("resumed-agent");
        var c1 = Guid.NewGuid().ToString("D");
        var c2 = Guid.NewGuid().ToString("D");
        var u3 = Guid.NewGuid().ToString("D");
        var spy = new AssignSpy();
        RoundRecord Earlier(int number, AuthorsSnapshot? authors = null) => PeerRound(number, T0.AddHours(number), T0.AddHours(number).AddMinutes(1)) with { Authors = authors };
        var rounds = new[] { Earlier(1, AuthorsSnapshotFixtures.Observed(number: 1, session: c1)), Earlier(2), Earlier(3), Earlier(4) };
        var attributed = PeerRound(5, T0.AddHours(5), T0.AddHours(5).AddMinutes(1)) with { Caller = new CallerDeclaration(Vendor: "claude") };
        using (var db = RoundsDb.Open(h.DataDir, Logger.None)!)
        {
            db.RecordRound(h.State, rounds[1] with { Authors = AuthorsSnapshotFixtures.Observed(number: 2, session: c2) }, []);
            db.RecordRound(h.State, rounds[2], [], new RoundContext(Caller: u3));
            db.RecordRound(h.State, rounds[3], [], new RoundContext(Caller: "repo:D:/some/checkout"));
        }

        var session = new PersistedSession(h.State, [.. rounds]) { OpenedUtc = T0 };
        var peers = (AuthorPeerSource)((lookup, deadline) => AuthorPeers.Find(AuthorPeers.OpenReadOnly(h.DataDir), lookup, (repo, branch) => h.Store.Load(repo, branch), deadline, TimeProvider.System));

        h.Step(peers: peers, assign: spy.Run).For(session, attributed);

        spy.Inputs.Single().Gate.Rounds.Select(round => round.CallerSessionId).Should().Equal(
            [c1, c2, u3, string.Empty, h.Caller],
            "the file's own snapshot, else rounds.authors_session, else a UUID rounds.caller (a repo: caller is no session id), and the located id for the round itself");
    }

    [Fact]
    public void WhenTheStoreCannotAnswer_TheFilesSnapshotsAreUsed_TheRestIsEmpty_AndThePeersAreUnknown()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var spy = new AssignSpy();
        var c1 = Guid.NewGuid().ToString("D");
        var first = PeerRound(1, T0.AddHours(1), T0.AddHours(1).AddMinutes(1)) with { Authors = AuthorsSnapshotFixtures.Observed(number: 1, session: c1) };
        var second = PeerRound(2, T0.AddHours(2), T0.AddHours(2).AddMinutes(1));
        var attributed = PeerRound(3, T0.AddHours(3), T0.AddHours(3).AddMinutes(1)) with { Caller = new CallerDeclaration(Vendor: "claude") };
        var session = new PersistedSession(h.State, [first, second]) { OpenedUtc = T0 };

        var snapshot = h.Step(peers: (_, _) => AuthorPeerSet.Unknown("locked"), assign: spy.Run).For(session, attributed)!;

        var input = spy.Inputs.Single();
        input.Gate.Rounds.Select(round => round.CallerSessionId).Should().Equal([c1, string.Empty, h.Caller]);
        input.PeersKnown.Should().BeFalse();
        input.Peers.Should().BeEmpty();
        snapshot.Reasons.Should().Contain(AttributionReasons.PriorWindowsUnknown, "E4 says what it could not see instead of treating no peers as a fact");
    }

    [Fact]
    public void ANoDatabase_OrAStepTwentyThreeOne_MeansPeersUnknown_NotAnError()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var round = h.Round(1);

        var absent = h.Step().For(h.SessionBefore(round), round)!;
        MakeStepTwentyThreeDatabase(h.DataDir);
        var old = h.Step().For(h.SessionBefore(round), round)!;

        foreach (var snapshot in new[] { absent, old })
        {
            snapshot.State.Should().Be(ObservationKinds.Partial);
            snapshot.Reasons.Should().Contain(AttributionReasons.PriorWindowsUnknown, "no database, or one with no authors_session column, cannot answer — and says so");
        }
    }

    private static void MakeStepTwentyThreeDatabase(string dataDir)
    {
        using (var db = new SqliteConnection($"Data Source={Path.Combine(dataDir, RoundsDb.FileName)};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..23]) + "; PRAGMA user_version=23;";
            make.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    // ---------- peers ----------

    [Fact]
    public void APeerOfTheSameCaller_IsHandedToE4_BuiltFromItsFile_EvenWhenTheDatabaseDisagreesAboutWhenItOpened()
    {
        using var h = AuthorsStepHarness.Of("background-agent-across-round-start");
        var spy = new AssignSpy();
        new PeerWorld(h.DataDir, h.Caller).Add("peerB", T0.AddSeconds(2), [PeerRound(1, T0.AddSeconds(5), T0.AddSeconds(9))]);
        var round = h.Round(1);

        var snapshot = h.Step(assign: spy.Run).For(h.SessionBefore(round), round)!;

        var input = spy.Inputs.Single();
        input.PeersKnown.Should().BeTrue();
        var peer = input.Peers.Should().ContainSingle().Subject;
        peer.GateSessionId.Should().Be("peerB");
        peer.OpenedUtc.Should().Be(new DateTimeOffset(T0.AddSeconds(2)), "from the session FILE — sessions.opened_utc is the time of the recording, which differs");
        peer.RepoPath.Should().Be(PeerWorld.RepoOf("peerB"));
        peer.Rounds.Single().CallerSessionId.Should().Be(h.Caller, "from rounds.authors_session");
        snapshot.PeerGates.Should().Equal("peerB");
    }

    [Fact]
    public void ARunningPeersStamp_IsNotItsCompletion_ADonePeersIs()
    {
        using var h = AuthorsStepHarness.Of("background-agent-across-round-start");
        var spy = new AssignSpy();
        var world = new PeerWorld(h.DataDir, h.Caller);
        var done = PeerRound(1, T0.AddSeconds(5), T0.AddSeconds(9));
        var running = PeerRound(2, T0.AddSeconds(20), T0.AddSeconds(30), RoundRecord.Running);
        world.Add("peerB", T0.AddSeconds(2), [done, running], [done]);
        var round = h.Round(1);

        h.Step(assign: spy.Run).For(h.SessionBefore(round), round);

        var rounds = spy.Inputs.Single().Peers.Single().Rounds;
        rounds[0].CompletedUtc.Should().Be(new DateTimeOffset(T0.AddSeconds(9)), "a done round keeps its stamp");
        rounds[1].CompletedUtc.Should().BeNull("a running row carries a stamp too — its last progress tick, which is no completion (E4 G1)");
    }

    [Fact]
    public void ARunningOnlyPeer_IsNotFound_AndTheLimitIsStated()
    {
        using var h = AuthorsStepHarness.Of("background-agent-across-round-start");
        var spy = new AssignSpy();
        new PeerWorld(h.DataDir, h.Caller).Add("peerB", T0.AddSeconds(2), [PeerRound(1, T0.AddSeconds(5), T0.AddSeconds(9), RoundRecord.Running)], projected: []);
        using (var db = RoundsDb.Open(h.DataDir, Logger.None)) { }
        var round = h.Round(1);

        var snapshot = h.Step(assign: spy.Run).For(h.SessionBefore(round), round)!;

        spy.Inputs.Single().Peers.Should().BeEmpty("RecordRound never ran for a running round, so a peer whose only same-caller round is running is invisible");
        snapshot.PeerGates.Should().BeEmpty();
        snapshot.Render().Should().Be("observed", "the stated limit: E7's grown-peer-set rule recomputes this round once that round is projected");
    }

    [Fact]
    public void APeerRunningRound_IsStoredAsItIs_AndNoOtherGatesRowsAreWritten()
    {
        using var h = AuthorsStepHarness.Of("background-agent-across-round-start");
        var world = new PeerWorld(h.DataDir, h.Caller);
        var done = PeerRound(1, T0.AddSeconds(1), T0.AddSeconds(2));
        var running = PeerRound(2, T0.AddSeconds(5), T0.AddSeconds(20), RoundRecord.Running);
        world.Add("peerB", T0.AddSeconds(0), [done, running]);
        var (fileBefore, rowsBefore) = (world.FileOf("peerB"), world.RowsOf("peerB"));
        var round = h.Round(1);

        var snapshot = h.Step().For(h.SessionBefore(round), round)!;

        snapshot.State.Should().Be(ObservationKinds.Partial);
        snapshot.Reasons.Should().Contain(AttributionReasons.PeerRunning, "a same-caller peer round still running across this round's window: provisional, stored as E4 returns it");
        world.FileOf("peerB").Should().Be(fileBefore, "E5 holds no claim on the peer's session and writes none of it");
        world.RowsOf("peerB").Should().Equal(rowsBefore);
    }

    // ---------- the budget ----------

    [Fact]
    public void ASliceOverTheByteBudget_IsPartialReadBudget_ASliceUnderIt_IsObserved()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        // 2 KiB: the head of the main file (its first stamped line, which the byte gate always yields) is 1.6 KiB, so only a larger SLICE can pass it.
        var budget = new AuthorsBudget(2048, TimeSpan.FromSeconds(2));
        var round = h.Round(1);
        var big = h.Step(budget: budget, peers: AuthorsStepHarness.AlwaysNoPeers).For(h.SessionBefore(round), round)!;

        // The same budget with the gate's window starting hours after the last line: the byte gate yields only the head, so little is charged.
        var earlier = PeerRound(1, T0.AddHours(8), T0.AddHours(8).AddMinutes(1)) with { Authors = AuthorsSnapshotFixtures.Observed(number: 1, session: h.Caller) };
        var later = PeerRound(2, T0.AddHours(9), T0.AddHours(9).AddMinutes(1)) with { Caller = new CallerDeclaration(Vendor: "claude") };
        var session = new PersistedSession(h.State, [earlier]) { OpenedUtc = T0 };
        var small = h.Step(budget: budget, peers: AuthorsStepHarness.AlwaysNoPeers).For(session, later)!;

        big.Render().Should().Be("partial: read budget", "the unbounded slice is larger than 2 KiB");
        big.Reader.Should().Be(ObservedSession.ReaderVersion, "the reader ran, and stopped");
        small.Render().Should().Be("observed", "the byte limit is the SLICE's: the step passes the bound and does not re-charge the file");
    }

    [Fact]
    public void ReadFromIsCalledBeforeTheRead_AndItsBoundReachesTheReader()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        DateTime? seen = DateTime.MinValue;
        var earlier = PeerRound(1, T0.AddHours(8), T0.AddHours(8).AddMinutes(1)) with { Authors = AuthorsSnapshotFixtures.Observed(number: 1, session: h.Caller) };
        var later = PeerRound(2, T0.AddHours(9), T0.AddHours(9).AddMinutes(1)) with { Caller = new CallerDeclaration(Vendor: "claude") };
        var session = new PersistedSession(h.State, [earlier]) { OpenedUtc = T0 };
        var expected = RoundAttribution.ReadFrom(new AttributionInput(
            new CallerObservation(h.Caller, new Core.Authors.ObservationState(ObservationKinds.Observed, []), [], [], [], []),
            AttributionMapping.Timeline(h.State.SessionId, h.State.RepoPath, T0, [earlier, later], round => round.Number == 2 ? h.Caller : h.Caller),
            [], [h.State.RepoPath], "CodeReview", 2, PeersKnown: true));

        h.Step(
                peers: AuthorsStepHarness.AlwaysNoPeers,
                read: (location, budget, from) =>
                {
                    seen = from;

                    return CallerTranscript.Read(location, budget, from);
                })
            .For(session, later);

        expected.Should().NotBeNull("a gate with a prior recorded round has a bounded read");
        seen.Should().Be(expected!.Value.UtcDateTime, "the bound E4 computes from the timelines alone is what the reader is handed");
        seen.Should().Be(T0.AddHours(8), "the previous recorded round's START");
    }

    /// <summary>A clock that runs one second faster per read, but only while the reader is inside — the budget must run out MID-read.</summary>
    private sealed class ReadClock : TimeProvider
    {
        private DateTimeOffset _now = new(T0);

        public bool Reading { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            if (Reading)
            {
                _now = _now.AddSeconds(1);
            }

            return _now;
        }
    }

    [Fact]
    public void ADeadlineThatPassesInsideTheRead_IsPartialReadBudget()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var clock = new ReadClock();
        var round = h.Round(1);

        var snapshot = h.Step(
                clock: clock,
                peers: AuthorsStepHarness.AlwaysNoPeers,
                read: (location, budget, from) =>
                {
                    clock.Reading = true;
                    try
                    {
                        return CallerTranscript.Read(location, budget, from);
                    }
                    finally
                    {
                        clock.Reading = false;
                    }
                })
            .For(h.SessionBefore(round), round)!;

        snapshot.Render().Should().Be("partial: read budget");
        snapshot.Rows.Should().NotBeNull();
    }

    [Fact]
    public void ADeadlineThatPassesInThePeerPhase_StopsThere_NoReaderNoAssign_AndEverythingUnknown()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var clock = new MovableClock(new DateTimeOffset(T0));
        var (reads, assigns) = (0, 0);
        var round = h.Round(1);

        var snapshot = h.Step(
                clock: clock,
                peers: (_, _) =>
                {
                    clock.Advance(TimeSpan.FromSeconds(3));

                    return AuthorsStepHarness.NoPeers;
                },
                read: (location, budget, from) =>
                {
                    reads++;

                    return CallerTranscript.Read(location, budget, from);
                },
                assign: input =>
                {
                    assigns++;

                    return RoundAttribution.Assign(input);
                })
            .For(h.SessionBefore(round), round)!;

        snapshot.Render().Should().Be("partial: read budget");
        (reads, assigns).Should().Be((0, 0));
        (snapshot.Reader, snapshot.Attribution).Should().Be((0, 0), "neither component ran");
        (snapshot.SubmoduleWrites, snapshot.UncoveredWrites, snapshot.UnobservedLaunches).Should().Be((-1, -1, -1), "never a measured zero");
        snapshot.Rows.Should().BeEmpty();
    }

    [Fact]
    public void ADeadlineThatPassesAfterTheRead_NeverAttributes_ButSaysTheReaderRan()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var clock = new MovableClock(new DateTimeOffset(T0));
        var assigns = 0;
        var round = h.Round(1);

        var snapshot = h.Step(
                clock: clock,
                peers: AuthorsStepHarness.AlwaysNoPeers,
                read: (location, budget, from) =>
                {
                    var observed = CallerTranscript.Read(location, budget, from);
                    clock.Advance(TimeSpan.FromSeconds(3));

                    return observed;
                },
                assign: input =>
                {
                    assigns++;

                    return RoundAttribution.Assign(input);
                })
            .For(h.SessionBefore(round), round)!;

        snapshot.Render().Should().Be("partial: read budget");
        assigns.Should().Be(0, "attribution never ran, so nothing may look measured");
        (snapshot.Reader, snapshot.Attribution).Should().Be((ObservedSession.ReaderVersion, 0));
        snapshot.UncoveredWrites.Should().Be(-1);
    }

    [Fact]
    public void WithThreePeers_AndTheClockAdvancedWhileTheFirstFileLoads_TheOthersAreNeverLoaded()
    {
        using var h = AuthorsStepHarness.Of("background-agent-across-round-start");
        var world = new PeerWorld(h.DataDir, h.Caller);
        foreach (var id in new[] { "peerA", "peerB", "peerC" })
        {
            world.Add(id, T0.AddSeconds(2), [PeerRound(1, T0.AddSeconds(5), T0.AddSeconds(9))]);
        }

        var clock = new MovableClock(new DateTimeOffset(T0));
        var loaded = new List<string>();
        var round = h.Round(1);

        var snapshot = h.Step(
                clock: clock,
                load: (repo, branch) =>
                {
                    loaded.Add(repo);
                    clock.Advance(TimeSpan.FromSeconds(3));

                    return h.Store.Load(repo, branch);
                })
            .For(h.SessionBefore(round), round)!;

        loaded.Should().HaveCount(1, "the deadline is checked before EACH peer file: the second and third are never loaded");
        snapshot.Render().Should().Be("partial: read budget");
    }

    // ---------- failures ----------

    [Fact]
    public void AReaderThatThrows_IsUnobservedReaderFailed_ThroughAnUnknownSnapshot_AndTheLogNamesOnlyTheType()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var sink = new ListSink();
        var log = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        var round = h.Round(1);

        var snapshot = h.Step(
                peers: AuthorsStepHarness.AlwaysNoPeers,
                read: (_, _, _) => throw new InvalidOperationException("a sentence of the transcript: " + FixtureManifest.Sentinel),
                log: log)
            .For(h.SessionBefore(round), round)!;

        snapshot.Render().Should().Be("unobserved: reader failed");
        (snapshot.Reader, snapshot.Attribution, snapshot.UncoveredWrites).Should().Be((0, 0, -1));
        sink.Lines.Should().Contain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal));
        sink.Lines.Should().NotContain(line => line.Contains(FixtureManifest.Sentinel, StringComparison.Ordinal), "a log line never carries what an exception quoted");
    }

    [Fact]
    public void AnInputE4Refuses_IsUnobservedInvalidInput_WithTheReaderVersion_AndNoAttribution()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var round = h.Round(1);
        var session = new PersistedSession(h.State, [round]) { OpenedUtc = T0 };

        var snapshot = h.Step(peers: AuthorsStepHarness.AlwaysNoPeers).For(session, round)!;

        snapshot.Render().Should().Be("unobserved: invalid input", "the journal lists the round twice: refused, never merged");
        (snapshot.Reader, snapshot.Attribution).Should().Be((ObservedSession.ReaderVersion, 0), "the reader ran; Assign did not");
        (snapshot.SubmoduleWrites, snapshot.UncoveredWrites, snapshot.UnobservedLaunches).Should().Be((-1, -1, -1));
    }

    // ---------- the diagnostics ----------

    [Fact]
    public void TheReadersCountsAreLoggedAsNumbers_NeverAsAStateOrAWord()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var sink = new ListSink();
        var log = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        var round = h.Round(1);

        var snapshot = h.Step(peers: AuthorsStepHarness.AlwaysNoPeers, log: log).For(h.SessionBefore(round), round)!;

        sink.Lines.Should().Contain(line => line.Contains("agent files empty", StringComparison.Ordinal) && line.Contains("in the slice", StringComparison.Ordinal),
            "the reader's diagnostics reach the log, AgentFilesEmpty included");
        snapshot.Reasons.Should().BeEmpty("a diagnostic is never a reason");
        sink.Lines.Should().Contain(line => line.Contains("ms", StringComparison.Ordinal) && line.Contains("row(s)", StringComparison.Ordinal), "and so does the elapsed time");
        sink.Lines.Should().NotContain(line => line.Contains(FixtureManifest.Sentinel, StringComparison.Ordinal));
    }

    // ---------- writes into other checkouts ----------

    [Fact]
    public void AWriteIntoARegisteredSubmodule_IsCountedAsOne_AndAWriteIntoANestedCheckoutIsOnlyUncovered()
    {
        using var h = AuthorsStepHarness.Of("write-into-submodule-and-scratchpad");
        var round = h.Round(1);

        var nested = h.Step(peers: AuthorsStepHarness.AlwaysNoPeers).For(h.SessionBefore(round), round)!;
        File.WriteAllText(Path.Combine(h.RepoPath, ".gitmodules"), "[submodule \"p1000\"]\n\tpath = p1000\n\turl = ../p1000\n");
        var registered = h.Step(peers: AuthorsStepHarness.AlwaysNoPeers).For(h.SessionBefore(round), round)!;

        (nested.SubmoduleWrites, nested.UncoveredWrites).Should().Be((0, 1), "a nested checkout nobody declared: uncovered, not a submodule");
        (registered.SubmoduleWrites, registered.UncoveredWrites).Should().Be((1, 1), "the parsed .gitmodules names it — no process is launched on the answer path");
        registered.Rows.Should().Contain(row => row.Association == Association.WroteHere, "the rows are unchanged: the size, not the authors, is what these counts qualify");
    }

    [Fact]
    public void TheMapping_FillsAnsweredAtAndTheUniverse_AndNeverListsMainAsAnAgent()
    {
        using var h = AuthorsStepHarness.Of("resumed-agent");
        var spy = new AssignSpy();
        var round = h.Round(1);

        h.Step(peers: AuthorsStepHarness.AlwaysNoPeers, assign: spy.Run).For(h.SessionBefore(round), round);

        var input = spy.Inputs.Single();
        input.Caller.Writes.Should().OnlyContain(write => write.AnsweredAt >= write.At, "E3's AnsweredUtc is mapped onto E4's AnsweredAt for every write — the decision is E4's");
        input.Repositories.Should().Contain(h.RepoPath, "the universe of E4.S2 decision 6, in the spelling it arrives in");
        input.Caller.Agents.Should().NotContain(agent => agent.Key.IsMain, "main is never listed as an agent");
    }
}

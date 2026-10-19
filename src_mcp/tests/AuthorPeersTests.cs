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
/// The peer lookup (E5.S4, <c>AuthorPeers.Find</c>) — the ONE unit the live path and E7's backfill both call: which other gate sessions hold a
/// round of this caller, each built from its SESSION FILE, and <b>whether the lookup completed</b>. A lookup that did not complete says so
/// (<c>PeersKnown</c> false) — a precondition of E7's rule 3 (E7 §10 item 8) — and never answers "no peers".
/// </summary>
public sealed class AuthorPeersTests : IDisposable
{
    private static readonly DateTime T0 = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string Caller = "11111111-1111-1111-1111-111111111111";

    private readonly TempDir _dir = TempDir.For("testsToDelete-coai-peers-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private PeerWorld World => new(_dir.Path, Caller);

    private SessionStore Store => new(_dir.Path);

    private static RoundRecord Round(int number, string status = RoundRecord.Done) =>
        new("CodeReview", number, "proceed", 0, "answered", T0.AddMinutes(number)) { Status = status, StartedUtc = T0.AddMinutes(number - 1) };

    private AuthorPeerSet Find(Func<string, string, PersistedSession?>? load = null, DateTimeOffset? deadline = null, TimeProvider? clock = null, string gate = "me")
    {
        using var connection = AuthorPeers.OpenReadOnly(_dir.Path);

        return AuthorPeers.Find(connection, new PeerLookup(Caller, gate), load ?? ((repo, branch) => Store.Load(repo, branch)), deadline, clock ?? TimeProvider.System);
    }

    [Fact]
    public void ThePeersAreTheOtherGateSessionsOfThisCaller_EachFromItsFile()
    {
        World.Add("peerA", T0, [Round(1)]);
        World.Add("peerB", T0.AddSeconds(30), [Round(1), Round(2)]);

        var found = Find();

        found.PeersKnown.Should().BeTrue();
        found.Peers.Select(peer => peer.GateSessionId).Should().Equal("peerA", "peerB");
        found.Peers[1].Rounds.Should().HaveCount(2);
        found.Peers[1].OpenedUtc.Should().Be(new DateTimeOffset(T0.AddSeconds(30)), "from the file, never sessions.opened_utc");
    }

    [Fact]
    public void TheGateSessionBeingAttributed_IsNeverItsOwnPeer()
    {
        World.Add("me", T0, [Round(1)]);
        World.Add("peerA", T0, [Round(1)]);

        Find(gate: "me").Peers.Select(peer => peer.GateSessionId).Should().Equal("peerA");
    }

    [Fact]
    public void ASessionOfAnotherCaller_IsNotAPeer()
    {
        new PeerWorld(_dir.Path, Guid.NewGuid().ToString("D")).Add("stranger", T0, [Round(1)]);
        World.Add("peerA", T0, [Round(1)]);

        Find().Peers.Select(peer => peer.GateSessionId).Should().Equal("peerA");
    }

    [Fact]
    public void ARoundRecordedBeforeThisEpic_IsFoundByItsUuidCaller()
    {
        var old = new SessionState("old", PeerWorld.RepoOf("old"), "feat/old", new PanelConfig()) { Stage = Stage.CodeReview };
        Store.Save(new PersistedSession(old, [Round(1)]) { OpenedUtc = T0 });
        using (var db = RoundsDb.Open(_dir.Path, Logger.None)!)
        {
            db.RecordRound(old, Round(1), [], new RoundContext(Caller: Caller.ToUpperInvariant()));
        }

        Find().Peers.Select(peer => peer.GateSessionId).Should().Equal(["old"], "rounds.authors_session is empty there; a UUID rounds.caller names the caller, in any case");
    }

    [Fact]
    public void ARoundOfAnUnnamedCaller_IsNeverAPeer()
    {
        var old = new SessionState("old", PeerWorld.RepoOf("old"), "feat/old", new PanelConfig()) { Stage = Stage.CodeReview };
        Store.Save(new PersistedSession(old, [Round(1)]) { OpenedUtc = T0 });
        using (var db = RoundsDb.Open(_dir.Path, Logger.None)!)
        {
            db.RecordRound(old, Round(1), [], new RoundContext(Caller: "repo:D:/checkout"));
        }

        var found = Find();

        found.PeersKnown.Should().BeTrue();
        found.Peers.Should().BeEmpty("a repo: caller is not a session id");
    }

    [Fact]
    public void ANoDatabase_IsNotAnAnswer()
    {
        var found = Find();

        found.PeersKnown.Should().BeFalse();
        found.Peers.Should().BeEmpty();
        found.Why.Should().NotBeEmpty();
    }

    [Fact]
    public void AStoreWithoutTheColumn_IsNotAnAnswer()
    {
        using (var db = new SqliteConnection($"Data Source={_dir.At(RoundsDb.FileName)};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..23]) + "; PRAGMA user_version=23;";
            make.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        Find().PeersKnown.Should().BeFalse("older than step 24: no authors_session column to ask");
    }

    [Fact]
    public void APeerFileThatDoesNotLoad_MeansPeersUnknown_AndNoPartialList()
    {
        World.Add("peerA", T0, [Round(1)]);
        World.Add("peerB", T0, [Round(1)]);

        var found = Find(load: (repo, branch) => repo.EndsWith("peerB", StringComparison.Ordinal) ? null : Store.Load(repo, branch));

        found.PeersKnown.Should().BeFalse("one peer that cannot be read makes the whole set unknown");
        found.Peers.Should().BeEmpty("never a half-answer taken for a whole one");
    }

    [Fact]
    public void APeerFileThatNamesAnotherSession_MeansPeersUnknown()
    {
        World.Add("peerA", T0, [Round(1)]);

        var found = Find(load: (repo, branch) =>
        {
            var file = Store.Load(repo, branch)!;

            return file with { State = file.State with { SessionId = "someone-else" } };
        });

        found.PeersKnown.Should().BeFalse();
    }

    [Fact]
    public void ADeadlineThatPassesBeforeAFileLoads_EndsTheWalk_NoDeadlineWalksAll()
    {
        foreach (var id in new[] { "peerA", "peerB", "peerC" })
        {
            World.Add(id, T0, [Round(1)]);
        }

        var clock = new MovableClock(new DateTimeOffset(T0));
        var loaded = 0;
        var cut = Find(
            load: (repo, branch) =>
            {
                loaded++;
                clock.Advance(TimeSpan.FromSeconds(3));

                return Store.Load(repo, branch);
            },
            deadline: new DateTimeOffset(T0).AddSeconds(2),
            clock: clock);
        var unbounded = Find(deadline: null);

        cut.PeersKnown.Should().BeFalse("the deadline cut the walk");
        loaded.Should().Be(1, "checked before EACH peer file is loaded");
        unbounded.PeersKnown.Should().BeTrue("E7 passes no deadline and walks every peer");
        unbounded.Peers.Should().HaveCount(3);
    }

    [Fact]
    public void TheEarlierCallersOfTheGateSession_AreReadByTheSameLookup()
    {
        var me = new SessionState("me", PeerWorld.RepoOf("me"), "feat/me", new PanelConfig()) { Stage = Stage.CodeReview };
        World.Project(me, [Round(1)]);
        using (var db = RoundsDb.Open(_dir.Path, Logger.None)!)
        {
            db.RecordRound(me, Round(2), [], new RoundContext(Caller: "0b0b0b0b-0b0b-0b0b-0b0b-0b0b0b0b0b0b"));
        }

        var found = Find();

        found.EarlierCallers[("CodeReview", 1)].Should().Be(Caller, "rounds.authors_session");
        found.EarlierCallers[("CodeReview", 2)].Should().Be("0b0b0b0b-0b0b-0b0b-0b0b-0b0b0b0b0b0b", "else a UUID rounds.caller");
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111")]
    [InlineData("ABCDEFAB-0000-0000-0000-000000000000", "abcdefab-0000-0000-0000-000000000000")]
    [InlineData(" 11111111-1111-1111-1111-111111111111 ", "11111111-1111-1111-1111-111111111111")]
    [InlineData("repo:D:/checkout", "")]
    [InlineData("an operator's string", "")]
    [InlineData("", "")]
    public void OnlyAUuidIsASessionId(string raw, string expected) => AuthorPeers.UuidOrEmpty(raw).Should().Be(expected);
}

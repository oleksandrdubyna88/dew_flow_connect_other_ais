using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Tests;

/// <summary>
/// One of E1's fixture cases made into a STORE the backfill can be run over (E7): its transcripts in a temp Claude home, and every gate session of
/// its <c>rounds.json</c> recorded through the LIVE path — <see cref="RoundAuthorsStep"/> for the snapshot, <see cref="SessionStore"/> for the file,
/// <see cref="RoundsDb.RecordRound"/> for the projection — so what the backfill is compared against is what the live writers left, not a table a
/// test typed. Everything is under a <c>testsToDelete-</c> temp folder; no live store is ever named.
/// </summary>
internal sealed class BackfillWorld : IDisposable
{
    private readonly TempDir _temp;

    private BackfillWorld(TempDir temp, CallerTranscriptFixture fixture)
    {
        _temp = temp;
        Fixture = fixture;
        DataDir = temp.At("data");
        Directory.CreateDirectory(DataDir);
        Settings = new PanelSettings { DataDir = DataDir, AgentLogDir = fixture.Home.ProjectsDir };
        Store = new SessionStore(DataDir);
    }

    public CallerTranscriptFixture Fixture { get; }

    public string DataDir { get; }

    public PanelSettings Settings { get; }

    public SessionStore Store { get; }

    /// <summary>What the live path recorded, by (gate session, stage, number).</summary>
    public Dictionary<(string Session, string Stage, int Number), AuthorsSnapshot> Live { get; } = [];

    public string Caller => Fixture.Rounds.CallerSessionId;

    public Func<string, string?> Environment => name => name == "CLAUDE_CODE_SESSION_ID" ? Caller : null;

    public static BackfillWorld Of(string fixtureName)
    {
        var temp = TempDir.For("testsToDelete-coai-backfill-");

        return new BackfillWorld(temp, CallerTranscriptFixture.Materialise(fixtureName, temp));
    }

    public string RepoOf(FixtureGateSession gate) => Fixture.Root + gate.RepoPath["D:/fixture".Length..];

    public static string BranchOf(FixtureGateSession gate) => "feat/" + gate.Id[..8];

    public SessionState StateOf(FixtureGateSession gate) => new(gate.Id, RepoOf(gate), BranchOf(gate), new PanelConfig()) { Stage = Stage.CodeReview };

    /// <summary>A round as the engine holds it when it asks for the authors: done, a claude caller, the fixture's own instants.</summary>
    public static RoundRecord RoundOf(FixtureRound round, string stage = "CodeReview", string verdict = "proceed", string status = RoundRecord.Done, string sha = "") =>
        new(stage, round.Number, verdict, 0, "answered", round.CompletedUtc)
        {
            Status = status,
            StartedUtc = round.StartedUtc,
            Sha = sha,
            Caller = new CallerDeclaration(Vendor: "claude"),
        };

    /// <summary>Every gate session of the case, round by round, through the live writers.</summary>
    public void RecordLive(AuthorPeerSource? peers = null, Func<AttributionInput, WindowAssignment>? assign = null)
    {
        // The store exists before the first round, as it does on every machine that has ever run a round: a fresh directory's first round would be
        // `partial: prior windows unknown` (E5 §3), which is the live limit this backfill repairs and not what these tests compare against.
        RoundsDb.Open(DataDir, Serilog.Core.Logger.None)!.Dispose();
        var step = new RoundAuthorsStep(Settings, Serilog.Core.Logger.None, Store, Environment, peers: peers, assign: assign);
        foreach (var gate in Fixture.Rounds.GateSessions)
        {
            var earlier = new List<RoundRecord>();
            foreach (var fixtureRound in gate.Rounds)
            {
                var round = RoundOf(fixtureRound);
                var before = new PersistedSession(StateOf(gate), [.. earlier]) { OpenedUtc = gate.OpenedUtc };
                var authors = step.For(before, round);
                Live[(gate.Id, round.Stage, round.Number)] = authors!;
                earlier.Add(round with { Authors = authors });
                Record(gate, earlier, round with { Authors = authors });
            }
        }
    }

    /// <summary>Saves the session file with <paramref name="rounds"/> and projects <paramref name="projected"/> into the database.</summary>
    public void Record(FixtureGateSession gate, List<RoundRecord> rounds, RoundRecord projected, RoundContext context = default)
    {
        Store.Save(new PersistedSession(StateOf(gate), [.. rounds]) { OpenedUtc = gate.OpenedUtc });
        using var db = RoundsDb.Open(DataDir, Serilog.Core.Logger.None)!;
        db.RecordRound(StateOf(gate), projected, [], string.IsNullOrEmpty(context.Caller) ? context with { Caller = Caller } : context);
    }

    /// <summary>The store as an older server left it: the authors columns empty, the authors rows gone, and the session files without a snapshot.</summary>
    public void StripAuthors()
    {
        SqliteConnection.ClearAllPools();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(DataDir, RoundsDb.FileName)};Pooling=False"))
        {
            db.Open();
            Run(db, """
                UPDATE rounds SET authors_state = '', authors_session = '', authors_id_source = '', authors_reader = 0, authors_attribution = 0,
                    submodule_writes = 0, uncovered_writes = 0, ambiguous_agents = 0, ambiguous_launches = 0, ambiguous_turns = 0,
                    ambiguous_input_tokens = 0, ambiguous_output_tokens = 0, ambiguous_cache_creation_tokens = 0, ambiguous_cache_read_tokens = 0;
                DELETE FROM round_authors;
                """);
        }

        foreach (var gate in Fixture.Rounds.GateSessions)
        {
            if (Store.Load(RepoOf(gate), BranchOf(gate)) is { } file)
            {
                Store.Save(file with { Rounds = [.. file.Rounds.Select(round => round with { Authors = null })] });
            }
        }

        SqliteConnection.ClearAllPools();
    }

    public static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }
}

/// <summary>What a run of the backfill's units computes over a <see cref="BackfillWorld"/> — the same loop the mode makes, without the writes.</summary>
internal static class BackfillDriver
{
    public sealed record Computed(StoredRound Round, StoredRoundFacts Facts, BackfillAction Action, AuthorsSnapshot? Snapshot);

    /// <summary>
    /// Gathers the facts of every round ONCE (the peers are looked up once per round), plans (no transcript read), then computes every round that
    /// needs it, gate by gate in round order, the journal carrying each result forward as the mode's overlay does. Nothing is written to either store.
    /// </summary>
    public static List<Computed> Run(
        BackfillWorld world,
        CallerReads reads,
        Func<AttributionInput, WindowAssignment>? assign = null,
        Func<string, string, PersistedSession?>? load = null,
        DateTime? since = null)
    {
        using var db = AuthorPeers.OpenReadOnly(world.DataDir)!;
        var attribution = new BackfillAttribution(world.Settings, Serilog.Core.Logger.None, world.Store, reads, TimeProvider.System, assign, load);
        var facts = new StoredFacts(db, world.Settings, attribution);
        var rounds = new StoredHistory(db, world.Store).Gates()
            .SelectMany(gate => gate.Rounds.Select(round => facts.Of(gate, round)))
            .Select(found => (Facts: found, Action: BackfillDecision.Decide(found.Input(since))))
            .ToList();
        foreach (var (found, action) in rounds.Where(round => round.Action.Verb == BackfillVerb.Compute))
        {
            attribution.Compute(found.Before, found.Record, found.CallerId, found.Peers, found.Round.AuthorsIdSource, planning: true);
        }

        return [.. rounds.Select(round => Settled(round.Facts, round.Action, attribution))];
    }

    private static Computed Settled(StoredRoundFacts found, BackfillAction action, BackfillAttribution attribution)
    {
        var snapshot = action.Verb == BackfillVerb.Compute
            ? attribution.Compute(found.Before, found.Record, found.CallerId, found.Peers, found.Round.AuthorsIdSource, planning: false)
            : null;
        if (snapshot is not null && found.Index >= 0)
        {
            found.Gate.Journal[found.Index] = found.Record with { Authors = snapshot };
        }

        return new Computed(found.Round, found, action, snapshot);
    }
}

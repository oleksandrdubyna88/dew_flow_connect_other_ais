using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;

namespace CoaiMcp.Tests;

/// <summary>
/// One of E1's fixture cases, made real in a temp home, and the gate session around it: what <see cref="RoundAuthorsStep"/> is handed
/// (E5.S4). The gate's rounds come from the case's own <c>rounds.json</c>, the LAST of the first gate session being the round attributed;
/// everything before it is the session's journal, exactly as at the real call site, where the save has not appended the round yet.
/// </summary>
internal sealed class AuthorsStepHarness : IDisposable
{
    private readonly TempDir _temp;

    private AuthorsStepHarness(TempDir temp, CallerTranscriptFixture fixture, FixtureGateSession gate, string dataDir)
    {
        _temp = temp;
        Fixture = fixture;
        Gate = gate;
        DataDir = dataDir;
        Settings = new PanelSettings { DataDir = dataDir, AgentLogDir = fixture.Home.ProjectsDir };
        Store = new SessionStore(dataDir);
    }

    public CallerTranscriptFixture Fixture { get; }

    public FixtureGateSession Gate { get; }

    public string DataDir { get; }

    public PanelSettings Settings { get; }

    public SessionStore Store { get; }

    /// <summary>The caller's session id: what <c>CLAUDE_CODE_SESSION_ID</c> says through the INJECTED environment, never the process's.</summary>
    public string Caller => Fixture.Rounds.CallerSessionId;

    public Func<string, string?> Environment => name => name == "CLAUDE_CODE_SESSION_ID" ? Caller : null;

    public static AuthorsStepHarness Of(string fixtureName, int gateIndex = 0)
    {
        var temp = TempDir.For("testsToDelete-coai-authors-fixture-");
        var fixture = CallerTranscriptFixture.Materialise(fixtureName, temp);
        var harness = new AuthorsStepHarness(temp, fixture, fixture.Rounds.GateSessions[gateIndex], temp.At("data"));
        Directory.CreateDirectory(harness.DataDir);

        return harness;
    }

    /// <summary>The checkout the gate session reviews, as the fixture put it on disk.</summary>
    public string RepoPath => Fixture.Root + Gate.RepoPath["D:/fixture".Length..];

    public SessionState State => new(Gate.Id, RepoPath, "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    /// <summary>A round as the engine would hold it when it asks for the authors: done, with the fixture's own instants.</summary>
    public RoundRecord Round(int number, string stage = "CodeReview", string verdict = "proceed", string status = RoundRecord.Done) =>
        Round(Gate.Rounds.First(round => round.Number == number), stage, verdict, status);

    /// <summary>The same, for one round of the case by identity — a case may list a number twice (replay).</summary>
    public RoundRecord Round(FixtureRound fixtureRound, string stage = "CodeReview", string verdict = "proceed", string status = RoundRecord.Done) =>
        new RoundRecord(stage, fixtureRound.Number, verdict, 0, "answered", fixtureRound.CompletedUtc)
        {
            Status = status,
            StartedUtc = fixtureRound.StartedUtc,
            Caller = new CallerDeclaration(Vendor: "claude"),
        };

    /// <summary>The session as the engine holds it before the save: every round BEFORE <paramref name="attributed"/>, none of it.</summary>
    public PersistedSession SessionBefore(RoundRecord attributed, params RoundRecord[] earlier) =>
        new(State, [.. earlier.Where(round => round.Number < attributed.Number)]) { OpenedUtc = Gate.OpenedUtc };

    /// <summary>The step over this harness, with the real reader, assignment and peer lookup unless a seam is given.</summary>
    public RoundAuthorsStep Step(
        Func<string, string?>? environment = null,
        TimeProvider? clock = null,
        AuthorsBudget? budget = null,
        AuthorPeerSource? peers = null,
        Func<CallerTranscriptLocation, ReadBudget, DateTime?, ObservedSession>? read = null,
        Func<AttributionInput, WindowAssignment>? assign = null,
        Func<string, string, PersistedSession?>? load = null,
        Serilog.ILogger? log = null) =>
        new(Settings, log ?? Serilog.Core.Logger.None, Store, environment ?? Environment, clock, budget, peers, read, assign, load);

    /// <summary>The same step, but the peer source says nobody else is there and the store was fully read.</summary>
    public static AuthorPeerSet NoPeers { get; } = new(true, ImmutableArray<GateTimeline>.Empty, new Dictionary<(string Stage, int Number), string>());

    public static AuthorPeerSource AlwaysNoPeers { get; } = (_, _) => NoPeers;

    public void Dispose() => _temp.Dispose();
}

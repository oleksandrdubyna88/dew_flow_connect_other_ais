using System.Collections.Immutable;
using CoaiMcp.Core.Authors;

namespace CoaiMcp.Tests;

/// <summary>
/// A scenario of attribution built from short statements, and the one place a test makes an <see cref="AttributionInput"/>
/// (E4.S1, S2.9). Every instant is the fixed base <c>2026-10-14T12:00:00Z</c> plus minutes — never the clock. Message ids are
/// generated in the order the messages are added (<c>m-0001</c>, …).
/// </summary>
/// <remarks>
/// <para><b>It asserts what it builds.</b> <see cref="Build"/> throws when <see cref="AttributionInput.Validate"/> names a violation,
/// so no later test can pass on a fixture the code quietly declines (testing.md, A fixture the code REJECTS proves nothing).
/// <see cref="BuildUnchecked"/> is the one way round it, for the tests whose subject is the violation.</para>
/// <para><b>Gates.</b> <see cref="Gate"/> starts a gate session and every later <see cref="Opened"/> / <see cref="Round"/> belongs to
/// it. Every gate of the scenario is served by the scenario's one caller: the observation is the caller's, so a message, an agent and
/// a write belong to the scenario, not to a gate. The attributed round's gate is named when the input is built; every other gate is
/// a peer.</para>
/// </remarks>
internal sealed class AttributionScenario
{
    /// <summary>The one fixed base every instant is counted from.</summary>
    public static readonly DateTimeOffset Base = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly string _caller;
    private readonly List<GateSpec> _gates = [];
    private readonly List<ObservedMessage> _messages = [];
    private readonly List<ObservedAgent> _agents = [];
    private readonly List<ObservedWrite> _writes = [];
    private readonly List<UnobservedLaunch> _unobserved = [];
    private readonly List<StateReason> _reasons = [];
    private readonly List<string> _universe = [];
    private string _kind = ObservationKinds.Observed;
    private GateSpec? _current;
    private bool _peersKnown = true;
    private bool _withoutPriorEnd;

    private AttributionScenario(string caller) => _caller = caller;

    public string Caller => _caller;

    /// <summary>The instant <paramref name="minutes"/> after the base (negative is before it).</summary>
    public static DateTimeOffset At(double minutes) => Base.AddMinutes(minutes);

    public static AttributionScenario For(string caller = "c0") => new(caller);

    // ---- gates and rounds -----------------------------------------------------------------------------------------------

    /// <summary>Starts a gate session, or moves back to one already started (its repository is then unchanged).</summary>
    public AttributionScenario Gate(string id, string repoPath)
    {
        _current = _gates.Find(gate => gate.Id == id) ?? Add(new GateSpec(id, repoPath));

        return this;
    }

    public AttributionScenario Opened(double minutes)
    {
        Current.Opened = At(minutes);

        return this;
    }

    public AttributionScenario NeverOpened()
    {
        Current.Opened = null;

        return this;
    }

    /// <summary>
    /// A journal row. <paramref name="startedAt"/> null is a row older than the field; <paramref name="completedAt"/> defaults to ten
    /// minutes after the start (the stamp ON DISK — a running row carries one too).
    /// </summary>
    public AttributionScenario Round(
        string stage,
        int number,
        double? startedAt,
        double? completedAt = null,
        string status = "done",
        string verdict = "proceed",
        string? caller = null)
    {
        var completed = completedAt ?? (startedAt ?? 0) + 10;
        Current.Rounds.Add(new GateRound(
            stage,
            number,
            startedAt is { } started ? At(started) : null,
            status,
            verdict,
            caller ?? _caller,
            At(completed)));

        return this;
    }

    public AttributionScenario Code(int number, double startedAt, double completedAt) => Round("CodeReview", number, startedAt, completedAt);

    // ---- the observation ------------------------------------------------------------------------------------------------

    public AttributionScenario Agent(string id, string parent = AgentKey.MainId, double? launchedAt = null, string requested = "sonnet", string type = "general-purpose")
    {
        _agents.Add(new ObservedAgent(new AgentKey(id), new AgentKey(parent), type, requested, launchedAt is { } at ? At(at) : null, true));

        return this;
    }

    public AttributionScenario Message(
        string agent,
        double at,
        string model = "claude-sonnet-5-5",
        string effort = "high",
        string requested = "sonnet",
        TokenTerms? tokens = null)
    {
        var id = "m-" + (_messages.Count + 1).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
        _messages.Add(new ObservedMessage(new AgentKey(agent), id, At(at), model, requested, effort, tokens ?? new TokenTerms(1, 100, 10, 1000)));

        return this;
    }

    public AttributionScenario Main(double at, string model = "claude-opus-5-5", string effort = "high", string requested = "opus", TokenTerms? tokens = null) =>
        Message(AgentKey.MainId, at, model, effort, requested, tokens);

    /// <summary>A <see cref="WriteKind.Checkout"/> write whose key is <paramref name="path"/>; <paramref name="answeredAt"/> defaults to <paramref name="at"/>.</summary>
    public AttributionScenario Write(string agent, double at, string path, double? answeredAt = null) =>
        AddWrite(agent, at, answeredAt, WriteKind.Checkout, path);

    /// <summary>A write answered at <paramref name="answeredAt"/>, or no write at all when it is null — what a live read that ended before the result saw.</summary>
    public AttributionScenario WriteWhenAnswered(string agent, double at, string path, double? answeredAt) =>
        answeredAt is { } answered ? Write(agent, at, path, answered) : this;

    public AttributionScenario WriteOutside(string agent, double at, double? answeredAt = null) =>
        AddWrite(agent, at, answeredAt, WriteKind.OutsideAnyCheckout, string.Empty);

    public AttributionScenario WriteUnresolved(string agent, double at, string path, double? answeredAt = null) =>
        AddWrite(agent, at, answeredAt, WriteKind.Unresolved, path);

    /// <summary>
    /// A shell command naming a path. It is NOT a write: the reader never counts one (parent §5, E3 <c>WriteTools</c>), so this records
    /// nothing in the observation — the call exists so a test can say that, and so a plant can make it lie.
    /// </summary>
    public AttributionScenario ShellCall(string agent, double at, string path) => this;

    /// <summary>An <c>Agent</c> call, or a meta, with no transcript: one item, stamped <paramref name="at"/>.</summary>
    public AttributionScenario MissingChild(string parent, double? at, string why)
    {
        _unobserved.Add(new UnobservedLaunch(new AgentKey(parent), "toolu_" + (_unobserved.Count + 1), at is { } instant ? At(instant) : null, why));

        return this;
    }

    /// <summary>The two records E3 emits for ONE meta without a file: the unobserved launch and the inert agent (G4).</summary>
    public AttributionScenario MetaWithoutTranscript(string id, string parent, double at)
    {
        _agents.Add(new ObservedAgent(new AgentKey(id), new AgentKey(parent), "general-purpose", string.Empty, At(at), false));

        return MissingChild(parent, at, "meta without transcript");
    }

    /// <summary>An E3 reason with no instant (G5): the observation turns <c>partial</c>.</summary>
    public AttributionScenario Reason(string why)
    {
        _reasons.Add(new StateReason(why, null));
        _kind = ObservationKinds.Partial;

        return this;
    }

    public AttributionScenario CallerUnobserved(string why)
    {
        _reasons.Add(new StateReason(why, null));
        _kind = ObservationKinds.Unobserved;

        return this;
    }

    public AttributionScenario Repositories(params string[] roots)
    {
        _universe.AddRange(roots);

        return this;
    }

    public AttributionScenario PeersUnknown()
    {
        _peersKnown = false;

        return this;
    }

    /// <summary>Hands <see cref="RoundAttribution"/> no prior end: the read bound alone, to show it is not an ownership rule.</summary>
    public AttributionScenario WithoutPriorEnd()
    {
        _withoutPriorEnd = true;

        return this;
    }

    // ---- what it builds -------------------------------------------------------------------------------------------------

    public AttributionInput Build(string gate, string stage, int number)
    {
        var input = BuildUnchecked(gate, stage, number);
        var violations = input.Validate();
        if (violations.Length > 0)
        {
            throw new InvalidOperationException("The scenario is not a well-formed input: " + string.Join("; ", violations));
        }

        return input;
    }

    public AttributionInput BuildUnchecked(string gate, string stage, int number)
    {
        var timelines = _gates.Select(Timeline).ToList();
        var mine = timelines.Single(timeline => timeline.GateSessionId == gate);
        var peers = _peersKnown ? timelines.Where(timeline => timeline.GateSessionId != gate).ToImmutableArray() : [];

        return new AttributionInput(Observation(), mine, peers, Universe(), stage, number, _peersKnown);
    }

    public WindowAssignment Assign(string gate, string stage, int number)
    {
        var input = Build(gate, stage, number);

        return _withoutPriorEnd ? RoundAttribution.Assign(input, usePriorEnd: false) : RoundAttribution.Assign(input);
    }

    /// <summary>
    /// Test-side only (L9): the assignment of every settled, recorded round of this caller in every timeline, the others as peers
    /// (G6). A round of another caller and a running round are skipped — <see cref="AttributionInput.Validate"/> refuses both.
    /// </summary>
    public IReadOnlyList<Assigned> AssignEvery() =>
    [
        .. _gates.SelectMany(gate => Timeline(gate).Rounds.Where(IsSettledOfThisCaller).Select(round => (gate.Id, round)))
            .Select(pair => new Assigned(pair.Id, pair.round.Stage, pair.round.Number, Assign(pair.Id, pair.round.Stage, pair.round.Number))),
    ];

    public GateTimeline Timeline(string gateId) => Timeline(_gates.Single(gate => gate.Id == gateId));

    public CallerObservation Observation() => new(
        _caller,
        new ObservationState(_kind, [.. _reasons]),
        [.. _messages],
        [.. _agents],
        [.. _writes],
        [.. _unobserved]);

    // ---- private --------------------------------------------------------------------------------------------------------

    private GateSpec Current => _current ?? throw new InvalidOperationException("Start a gate with Gate(...) first.");

    private GateSpec Add(GateSpec gate)
    {
        _gates.Add(gate);

        return gate;
    }

    private bool IsSettledOfThisCaller(GateRound round) =>
        round.Stage is "PlanReview" or "CodeReview" && round.Verdict != "skipped" && round.Status == "done" && round.CallerSessionId == _caller;

    private static GateTimeline Timeline(GateSpec gate) => new(gate.Id, gate.RepoPath, gate.Opened, [.. gate.Rounds]);

    private ImmutableArray<string> Universe() =>
    [
        .. _gates.Select(gate => gate.RepoPath).Concat(_universe).Distinct(StringComparer.Ordinal),
    ];

    private AttributionScenario AddWrite(string agent, double at, double? answeredAt, WriteKind kind, string where)
    {
        _writes.Add(new ObservedWrite(new AgentKey(agent), At(at), At(answeredAt ?? at), kind, where));

        return this;
    }

    /// <summary>One gate session, mutable while the scenario is being written.</summary>
    private sealed class GateSpec(string id, string repoPath)
    {
        public string Id { get; } = id;

        public string RepoPath { get; } = repoPath;

        public DateTimeOffset? Opened { get; set; } = At(-60);

        public List<GateRound> Rounds { get; } = [];
    }

    /// <summary>One gate's round and what attribution said about it.</summary>
    public sealed record Assigned(string Gate, string Stage, int Number, WindowAssignment Assignment);
}

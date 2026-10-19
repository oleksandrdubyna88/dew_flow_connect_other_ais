using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// One call of <c>RoundAttribution.Assign</c>: the tiling of every gate in the input, the ladder over them, and the placements of THIS
/// round's windows. Pure; the only state is the memo of the ladder, filled in a deterministic order.
/// </summary>
internal sealed class AssignRun
{
    private readonly AttributionInput _input;
    private readonly Dictionary<string, ImmutableArray<Window>> _windowsOf = new(StringComparer.Ordinal);
    private readonly AssociationLadder _ladder;
    private readonly ImmutableArray<Window> _mine;
    private readonly ImmutableArray<Window> _peerWindows;

    public AssignRun(AttributionInput input, bool usePriorEnd)
    {
        _input = input;
        var gates = GatesOf(input);
        foreach (var gate in gates)
        {
            _windowsOf[gate.GateSessionId] = Windows.Of(gate, PriorEndOf(gate, gates, input.Caller.CallerSessionId, usePriorEnd));
        }

        _ladder = new AssociationLadder(input, gates.ToDictionary(gate => gate.GateSessionId, gate => gate.RepoPath, StringComparer.Ordinal));
        _mine = [.. _windowsOf[input.Gate.GateSessionId].Where(window => window.Stage == input.Stage && window.Number == input.Number)];
        _peerWindows = [.. gates.Where(gate => gate.GateSessionId != input.Gate.GateSessionId).SelectMany(gate => _windowsOf[gate.GateSessionId])];
    }

    private static List<GateTimeline> GatesOf(AttributionInput input) =>
        [.. new[] { input.Gate }.Concat(input.PeersKnown ? input.Peers : []).DistinctBy(gate => gate.GateSessionId, StringComparer.Ordinal)];

    private static DateTimeOffset? PriorEndOf(GateTimeline gate, List<GateTimeline> gates, string caller, bool usePriorEnd) =>
        usePriorEnd ? Windows.PriorEnd(gate, gates.Where(other => other.GateSessionId != gate.GateSessionId), caller) : null;

    /// <summary>The earliest instant a line can matter: the first of this round's windows and of their claimants'; null when one is unbounded.</summary>
    public DateTimeOffset? ReadFrom()
    {
        var reached = _mine.Where(window => !window.Blind).SelectMany(window => new[] { window }.Concat(ClaimantsOf(window))).ToList();

        return reached.Count == 0 || reached.Any(window => window.From is null) ? null : reached.Min(window => window.From);
    }

    /// <summary>Whether an unsettled peer window of this caller overlaps one of this round's windows — the test that gives a window <c>partial: peer running</c>.</summary>
    public bool HasRunningPeer() => _mine.Any(window => !window.Blind && Ownership.HasRunningPeer(window, _peerWindows, _input.Caller.CallerSessionId));

    public WindowAssignment Build()
    {
        var windows = _mine.Where(window => !window.Blind).ToList();
        var observed = _input.Caller.State.Kind != ObservationKinds.Unobserved;
        var messages = When(observed, () => windows.SelectMany(Messages)).ToList();
        var launches = When(observed, () => windows.SelectMany(Launches)).ToList();
        var writes = When(observed, () => windows.SelectMany(Writes)).ToList();

        return new WindowAssignment(
            _input.Gate.GateSessionId,
            _input.Stage,
            _input.Number,
            _input.Caller.CallerSessionId,
            [.. _mine.Order(Comparer<Window>.Create(ByWindow))],
            [.. messages.OrderBy(placement => placement.Window, Comparer<Window>.Create(ByWindow)).ThenBy(placement => placement.At).ThenBy(placement => placement.Agent.Id, StringComparer.Ordinal).ThenBy(placement => placement.MessageId, StringComparer.Ordinal)],
            [.. launches.OrderBy(placement => placement.Window, Comparer<Window>.Create(ByWindow)).ThenBy(placement => placement.At).ThenBy(placement => placement.Agent.Id, StringComparer.Ordinal)],
            [.. writes.OrderBy(placement => placement.Window, Comparer<Window>.Create(ByWindow)).ThenBy(placement => placement.At).ThenBy(placement => placement.Agent.Id, StringComparer.Ordinal).ThenBy(placement => placement.Root, StringComparer.Ordinal)],
            [.. When(observed, () => windows.SelectMany(Unobserved)).OrderBy(placement => placement.Window, Comparer<Window>.Create(ByWindow)).ThenBy(placement => placement.At).ThenBy(placement => placement.Parent.Id, StringComparer.Ordinal).ThenBy(placement => placement.Why, StringComparer.Ordinal)],
            [.. When(observed, () => windows.SelectMany(window => AgentsIn(window, messages, launches, writes))).OrderBy(association => association.Window, Comparer<Window>.Create(ByWindow)).ThenBy(association => association.Agent.Id, StringComparer.Ordinal)],
            [.. _mine.OrderBy(window => window, Comparer<Window>.Create(ByWindow)).Select(StateOf)],
            ReadFrom(),
            [.. _peerWindows.Select(window => window.Gate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    /// <summary>What a caller that was not observed contributes: nothing, and the source is not even read.</summary>
    private static IEnumerable<T> When<T>(bool observed, Func<IEnumerable<T>> source) => observed ? source() : [];

    private static int ByWindow(Window left, Window right)
    {
        var byKind = string.CompareOrdinal(left.Kind, right.Kind);
        if (byKind != 0)
        {
            return byKind;
        }

        var byStage = string.CompareOrdinal(left.Stage, right.Stage);

        return byStage != 0 ? byStage : left.Number.CompareTo(right.Number);
    }

    private ImmutableArray<Window> ClaimantsOf(Window window) => Ownership.ClaimantsOf(window, _peerWindows, _input.Caller.CallerSessionId);

    private WindowState StateOf(Window window) =>
        WindowStates.Of(window, _input, !window.Blind && Ownership.HasRunningPeer(window, _peerWindows, _input.Caller.CallerSessionId), _input.Gate);

    private IEnumerable<Placement> Messages(Window window)
    {
        var claimants = ClaimantsOf(window);

        return _input.Caller.Messages.Where(message => window.Contains(message.At)).Select(message =>
        {
            var owned = Ownership.Of(message.Agent, message.At, window, claimants, _ladder);

            return new Placement(message.MessageId, message.Agent, message.At, window, owned.Association, owned.Rung, owned.OwnerGate, message.Model, message.Requested, Effort.Of(message.Effort), message.Tokens);
        });
    }

    private IEnumerable<LaunchPlacement> Launches(Window window)
    {
        var claimants = ClaimantsOf(window);

        return _input.Caller.Agents.Where(agent => agent.TranscriptPresent && agent.FirstLineAt is { } at && window.Contains(at)).Select(agent =>
        {
            var at = agent.FirstLineAt!.Value;
            var owned = Ownership.Of(agent.Key, at, window, claimants, _ladder);

            return new LaunchPlacement(agent.Key, at, window, owned.Association, owned.Rung, owned.OwnerGate);
        });
    }

    private IEnumerable<WritePlacement> Writes(Window window) =>
        _ladder.EvidenceIn(window).Select(write => new WritePlacement(write.Agent, write.At, window, write.Root, WhereOf(window, write.Root)));

    private string WhereOf(Window window, string writeRoot) =>
        writeRoot.Length == 0 ? WriteWhere.Outside : InGateRepository(window, writeRoot);

    private string InGateRepository(Window window, string writeRoot) =>
        Repositories.Contains(_ladder.RepositoryOfGate(window.Gate), writeRoot) ? WriteWhere.Here : WriteWhere.Elsewhere;

    private IEnumerable<UnobservedLaunchPlacement> Unobserved(Window window) =>
        _input.Caller.Unobserved.Where(item => item.At is { } at && window.Contains(at)).Select(item => new UnobservedLaunchPlacement(item.Parent, item.At!.Value, window, item.Why));

    private IEnumerable<AgentAssociation> AgentsIn(Window window, List<Placement> messages, List<LaunchPlacement> launches, List<WritePlacement> writes) =>
        messages.Where(placement => placement.Window == window).Select(placement => placement.Agent)
            .Concat(launches.Where(placement => placement.Window == window).Select(placement => placement.Agent))
            .Concat(writes.Where(placement => placement.Window == window).Select(placement => placement.Agent))
            .Distinct().Select(agent => AssociationOf(agent, window));

    private AgentAssociation AssociationOf(AgentKey agent, Window window)
    {
        var verdict = _ladder.Of(agent, window);

        return new AgentAssociation(agent, window, verdict.Association, verdict.Rung, verdict.ElsewhereRoots);
    }
}

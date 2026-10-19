using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// One window of an assignment, with what was placed in it — the unit <see cref="RoundAuthorsBuilder"/> aggregates (E4.S3). An
/// unobserved window has no rows and unknown totals; every other window counts what it holds, and a window with nothing in it is an
/// observed ZERO.
/// </summary>
internal sealed class WindowSlice
{
    private static readonly string[] RowAssociations = [Association.WroteHere, Association.ChildOf, Association.SameSession];

    private readonly Window _window;
    private readonly WindowState _state;
    private readonly AttributionInput _input;
    private readonly List<Placement> _messages;
    private readonly List<LaunchPlacement> _launches;
    private readonly List<WritePlacement> _writes;
    private readonly List<AgentAssociation> _agents;
    private readonly int _unobserved;

    public WindowSlice(Window window, AttributionInput input, WindowAssignment assignment)
    {
        _window = window;
        _input = input;
        _state = assignment.States.Single(state => state.Window == window);
        _messages = [.. assignment.Messages.Where(placement => placement.Window == window)];
        _launches = [.. assignment.Launches.Where(placement => placement.Window == window)];
        _writes = [.. assignment.Writes.Where(placement => placement.Window == window)];
        _agents = [.. assignment.Agents.Where(association => association.Window == window)];
        _unobserved = assignment.Unobserved.Count(placement => placement.Window == window) + input.Caller.Unobserved.Count(item => item.At is null);
    }

    private bool IsUnobserved => _state.Kind == ObservationKinds.Unobserved;

    public WindowAuthors Authors() => new(
        _window.Kind,
        _state.Kind,
        _state.Reasons,
        TotalsOf(Association.Ambiguous),
        TotalsOf(Association.WroteElsewhere),
        TotalsOf(Association.ClaimedByPeer),
        IsUnobserved ? -1 : _unobserved);

    /// <summary>The roots of this window's writes that landed in a checkout under the gate's repository but are not the gate's own.</summary>
    public IEnumerable<string> NestedCheckoutWrites(string gateRepository) =>
        IsUnobserved
            ? []
            : _writes.Where(write => write.Where == WriteWhere.Here && !Repositories.Normalise(write.Root).Equals(Repositories.Normalise(gateRepository), StringComparison.OrdinalIgnoreCase)).Select(write => write.Root);

    public IEnumerable<AuthorRow> Rows() => IsUnobserved ? [] : [.. MessageRows(), .. LaunchOnlyRows(), .. TwoRepositoryRows()];

    private ActivityTotals TotalsOf(string association)
    {
        if (IsUnobserved)
        {
            return ActivityTotals.Unknown;
        }

        var messages = _messages.Where(placement => placement.Association == association).ToList();
        var launches = _launches.Where(placement => placement.Association == association).ToList();
        var agents = messages.Select(placement => placement.Agent).Concat(launches.Select(placement => placement.Agent)).Concat(TwoRepositoryAgentsFor(association)).Distinct().Count();

        return new ActivityTotals(agents, launches.Count, messages.Count, messages.Aggregate(new TokenTerms(0, 0, 0, 0), (sum, placement) => RoundAuthorsBuilder.Add(sum, placement.Tokens)));
    }

    /// <summary>An agent that wrote into two repositories is ambiguous in every gate that saw them, whatever owns its messages (decision 4, G10).</summary>
    private IEnumerable<AgentKey> TwoRepositoryAgentsFor(string association) =>
        association == Association.Ambiguous ? _agents.Where(IsTwoRepositories).Select(agent => agent.Agent) : [];

    private static bool IsTwoRepositories(AgentAssociation agent) => agent.Association == Association.Ambiguous && agent.Rung == Rung.TwoRepositories;

    private IEnumerable<AuthorRow> MessageRows()
    {
        var owned = _messages.Where(placement => RowAssociations.Contains(placement.Association)).ToList();
        var homes = owned.GroupBy(placement => placement.Agent).ToDictionary(group => group.Key, group => KeyOf(group.OrderBy(placement => placement.At).ThenBy(placement => placement.MessageId, StringComparer.Ordinal).First()));
        var launched = _launches.Where(placement => RowAssociations.Contains(placement.Association)).Select(placement => placement.Agent).ToHashSet();
        var writes = _writes.Where(write => write.Where == WriteWhere.Here).ToList();

        return owned.GroupBy(KeyOf).Select(group => RowOf(group, homes, launched, writes, owned));
    }

    private AuthorRow RowOf(IGrouping<RowKey, Placement> group, Dictionary<AgentKey, RowKey> homes, HashSet<AgentKey> launched, List<WritePlacement> writes, List<Placement> owned)
    {
        var key = group.Key;
        var first = group.First();

        return Row(
            key.Agent,
            first.Requested,
            first.Model,
            first.Effort,
            first.Association,
            homes[key.Agent] == key && launched.Contains(key.Agent) ? 1 : 0,
            group.Count(),
            group.Aggregate(new TokenTerms(0, 0, 0, 0), (sum, placement) => RoundAuthorsBuilder.Add(sum, placement.Tokens)),
            writes.Count(write => write.Agent == key.Agent && WriteHome(write, homes, owned) == key));
    }

    /// <summary>The row a write is counted on: that of the message at the write's own instant (a turn is the unit), else the agent's first row.</summary>
    private static RowKey? WriteHome(WritePlacement write, Dictionary<AgentKey, RowKey> homes, List<Placement> owned)
    {
        var same = owned.FirstOrDefault(placement => placement.Agent == write.Agent && placement.At == write.At);

        return same is not null ? KeyOf(same) : homes.TryGetValue(write.Agent, out var home) ? home : null;
    }

    private IEnumerable<AuthorRow> LaunchOnlyRows()
    {
        var seen = _messages.Where(placement => RowAssociations.Contains(placement.Association)).Select(placement => placement.Agent).ToHashSet();

        return _launches.Where(placement => RowAssociations.Contains(placement.Association) && !seen.Contains(placement.Agent))
            .Select(placement => Row(placement.Agent, Effort.Unknown, Effort.Unknown, Effort.Unknown, placement.Association, 1, 0, new TokenTerms(0, 0, 0, 0), 0));
    }

    private IEnumerable<AuthorRow> TwoRepositoryRows() =>
        _agents.Where(IsTwoRepositories).Select(agent =>
            Row(agent.Agent, Effort.Unknown, Effort.Unknown, Effort.Unknown, Association.WroteHere, 0, 0, new TokenTerms(0, 0, 0, 0), _writes.Count(write => write.Agent == agent.Agent && write.Where == WriteWhere.Here)));

    private AuthorRow Row(AgentKey agent, string requested, string model, string effort, string association, int launches, int turns, TokenTerms tokens, int writeCalls) => new(
        _window.Kind,
        _window.Stage,
        _window.Number,
        agent.IsMain ? "main" : "subagent",
        agent,
        _input.Caller.Agents.FirstOrDefault(declared => declared.Key == agent)?.AgentType ?? string.Empty,
        requested,
        model,
        effort,
        association,
        launches,
        turns,
        tokens,
        writeCalls);

    private static RowKey KeyOf(Placement placement) => new(placement.Agent, placement.Requested, placement.Model, placement.Effort, placement.Association);

    private readonly record struct RowKey(AgentKey Agent, string Requested, string Model, string Effort, string Association);
}

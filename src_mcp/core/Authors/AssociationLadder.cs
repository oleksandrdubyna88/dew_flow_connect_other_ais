using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>What the ladder decided for one agent in one window: the association, the rung that decided it, and the roots written elsewhere.</summary>
internal readonly record struct LadderVerdict(string Association, string Rung, ImmutableArray<string> ElsewhereRoots)
{
    /// <summary>The verdict of a rung that has nothing to say.</summary>
    public static LadderVerdict Undecided { get; } = new(string.Empty, string.Empty, []);

    public bool IsDecided => Association.Length > 0;
}

/// <summary>A write that counts as evidence for one window: who, where it landed (the root, empty when outside) and when.</summary>
internal sealed record WriteEvidence(AgentKey Agent, DateTimeOffset At, string Root);

/// <summary>
/// The association ladder of E4 §S2.4, evaluated per (agent, window) — memoised for one call of <c>Assign</c> in a plain dictionary:
/// 1. the agent's own writes; 2. a SUBAGENT parent that wrote here or is a child of one, in the same window; 3. the window's
/// destinations. Evidence only: a name, a request, a launch time or a working directory is read by no rung.
/// </summary>
/// <remarks>
/// A write is evidence for a window only when its RESULT was written before that window's round completed (§S2.2b G3), so the live
/// read and a later backfill see the same writes. The destination set is built over EVERY gate in the input, whichever gate the
/// ladder runs for (G12c).
/// </remarks>
internal sealed class AssociationLadder
{
    private readonly AttributionInput _input;
    private readonly IReadOnlyDictionary<string, string> _repositoryOf;
    private readonly Dictionary<AgentKey, ObservedAgent> _agents;
    private readonly Dictionary<(AgentKey Agent, Window Window), LadderVerdict> _memo = [];
    private readonly Dictionary<Window, ImmutableArray<WriteEvidence>> _evidence = [];

    public AssociationLadder(AttributionInput input, IReadOnlyDictionary<string, string> repositoryOf)
    {
        _input = input;
        _repositoryOf = repositoryOf;
        _agents = input.Caller.Agents.GroupBy(agent => agent.Key).ToDictionary(group => group.Key, group => group.First());
    }

    public LadderVerdict Of(AgentKey agent, Window window)
    {
        if (!_memo.TryGetValue((agent, window), out var verdict))
        {
            verdict = Decide(agent, window);
            _memo[(agent, window)] = verdict;
        }

        return verdict;
    }

    /// <summary>Every write that is evidence for <paramref name="window"/>, by any agent — what S3's write calls and the write placements read.</summary>
    public ImmutableArray<WriteEvidence> EvidenceIn(Window window)
    {
        if (!_evidence.TryGetValue(window, out var found))
        {
            found = [.. _input.Caller.Writes.Where(write => IsEvidenceFor(write, window)).Select(write => new WriteEvidence(write.Agent, write.At, RootOf(write)))];
            _evidence[window] = found;
        }

        return found;
    }

    public string RepositoryOfGate(string gate) => _repositoryOf[gate];

    private LadderVerdict Decide(AgentKey agent, Window window)
    {
        var written = WriteRung(agent, window);
        if (written.IsDecided)
        {
            return written;
        }

        var inherited = ChildRung(agent, window);

        return inherited.IsDecided ? inherited : SessionRung(window);
    }

    private static bool IsEvidenceFor(ObservedWrite write, Window window) =>
        window.Contains(write.At) && window.CompletedUtc is { } horizon && write.AnsweredAt < horizon;

    private string RootOf(ObservedWrite write) =>
        write.Kind == WriteKind.OutsideAnyCheckout ? string.Empty : Repositories.RepositoryOf(write.Where, _input.Repositories);

    private LadderVerdict WriteRung(AgentKey agent, Window window)
    {
        var roots = EvidenceIn(window).Where(write => write.Agent == agent && write.Root.Length > 0).Select(write => write.Root)
            .DistinctBy(Repositories.Normalise, StringComparer.OrdinalIgnoreCase).ToList();
        var here = roots.Where(writeRoot => Repositories.Contains(RepositoryOfGate(window.Gate), writeRoot)).ToList();
        var elsewhere = roots.Except(here).Order(StringComparer.Ordinal).ToImmutableArray();

        return (here.Count, elsewhere.Length) switch
        {
            (0, 0) => LadderVerdict.Undecided,
            (_, 0) => new LadderVerdict(Association.WroteHere, Rung.Write, []),
            (0, _) => new LadderVerdict(Association.WroteElsewhere, Rung.Write, elsewhere),
            _ => new LadderVerdict(Association.Ambiguous, Rung.TwoRepositories, elsewhere),
        };
    }

    private LadderVerdict ChildRung(AgentKey agent, Window window) =>
        DeclaredParentOf(agent).Any(parent => Of(parent, window).Association is Association.WroteHere or Association.ChildOf)
            ? new LadderVerdict(Association.ChildOf, Rung.Child, [])
            : LadderVerdict.Undecided;

    /// <summary>The agent's parent when it is a declared subagent; nothing for main, an unknown agent, or a parent nobody declared.</summary>
    private IEnumerable<AgentKey> DeclaredParentOf(AgentKey agent) =>
        _agents.TryGetValue(agent, out var self) && !self.Parent.IsMain && _agents.ContainsKey(self.Parent) ? [self.Parent] : [];

    private LadderVerdict SessionRung(Window window)
    {
        var destinations = EvidenceIn(window).Where(write => write.Root.Length > 0).Select(write => DestinationOf(write.Root))
            .DistinctBy(Repositories.Normalise, StringComparer.OrdinalIgnoreCase).ToList();
        var theOne = destinations.Count == 1 && Repositories.SameRoot(destinations[0], RepositoryOfGate(window.Gate));

        return theOne ? new LadderVerdict(Association.SameSession, Rung.Session, []) : new LadderVerdict(Association.Ambiguous, Rung.NoEvidence, []);
    }

    /// <summary>The longest <c>repo_path</c> of any gate in the input that holds the root, else the root itself.</summary>
    private string DestinationOf(string writeRoot) =>
        _repositoryOf.Values.Where(repoPath => Repositories.Contains(repoPath, writeRoot)).OrderByDescending(repoPath => Repositories.Normalise(repoPath).Length)
            .ThenBy(repoPath => repoPath, StringComparer.Ordinal).FirstOrDefault() ?? writeRoot;
}

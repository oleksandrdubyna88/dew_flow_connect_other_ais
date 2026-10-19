using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// Names a release RETIRED from a type whose file members are kept (<see cref="SessionScrub"/>), per type.
/// </summary>
/// <remarks>
/// <para>A member a release removes or renames does not leave the files that hold it: an older file still spells the old name,
/// this build does not know it, and the bag would carry it for ever. Naming it here makes the save drop it. A rename matters
/// most — the old name in old files becomes a bag entry while the new property starts empty.</para>
/// <para><b>Empty today.</b> The rule is in <c>research/module_server.md</c> § Persistence: a release that removes or renames a
/// member of a bagged type adds the old name here and to the member list of
/// <c>EveryBaggedTypesMembers_AreTheListedOnesOrRetired</c>, in the same commit — that test fails the release until it does.</para>
/// </remarks>
internal sealed class RetiredMembers
{
    private static readonly HashSet<string> None = [];

    private readonly Dictionary<Type, HashSet<string>> _names;

    public RetiredMembers(IEnumerable<(Type Type, string Name)> names) =>
        _names = names
            .GroupBy(n => n.Type)
            .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(n => n.Name), StringComparer.OrdinalIgnoreCase));

    /// <summary>What this build ships with: nothing retired yet.</summary>
    public static RetiredMembers Shipped { get; } = new([]);

    /// <summary>The names retired from one type, compared without case — empty for a type with none.</summary>
    public IReadOnlySet<string> Of(Type type) => _names.TryGetValue(type, out var names) ? names : None;
}

/// <summary>
/// The save's last step before a session is written: every bag of unknown members is cleaned, on a COPY.
/// </summary>
/// <remarks>
/// <para>Deserialisation never puts a member this build knows into a bag (a name in another case binds to the known property;
/// a get-only member is skipped). Only code can — and a known name in a bag is written twice, after which the stale copy WINS
/// the next read, because the last member wins. The scrub drops (a) every name of the type's JSON contract, case-insensitive,
/// and (b) every name <see cref="RetiredMembers"/> lists. A bag that ends empty becomes <c>null</c>, so a file with nothing
/// unknown reads and writes as it always did.</para>
/// <para><b>Rebuilt from the leaves up</b>, because <c>with</c> is shallow: a scrubbed reviewer row is useless unless its round
/// is re-made to hold it, and so on up to the session. A level whose bag and children are unchanged is passed through as the
/// SAME object. The dictionary it was given is never changed — callers' snapshots share it.</para>
/// </remarks>
internal static class SessionScrub
{
    public static PersistedSession Scrubbed(PersistedSession session, RetiredMembers retired)
    {
        var state = StateOf(session.State, retired);
        var rounds = Mapped(session.Rounds, r => RoundOf(r, retired));
        var bag = Bag(session.Unknown, ServerJsonContext.Default.PersistedSession, retired);

        return Same((session.State, state), (session.Rounds, rounds), (session.Unknown, bag))
            ? session
            : session with { Unknown = bag, State = state, Rounds = rounds };
    }

    private static SessionState StateOf(SessionState state, RetiredMembers retired)
    {
        var config = ConfigOf(state.Config, retired);
        var bag = Bag(state.Unknown, ServerJsonContext.Default.SessionState, retired);

        return Same((state.Config, config), (state.Unknown, bag)) ? state : state with { Unknown = bag, Config = config };
    }

    private static PanelConfig ConfigOf(PanelConfig config, RetiredMembers retired)
    {
        var lane = GateOf(config.SecurityLane, retired);
        var roles = RolesOf(config.Roles, retired);
        var bag = Bag(config.Unknown, ServerJsonContext.Default.PanelConfig, retired);

        return Same((config.SecurityLane, lane), (config.Roles, roles), (config.Unknown, bag))
            ? config
            : config with { Unknown = bag, SecurityLane = lane, Roles = roles };
    }

    private static IReadOnlyDictionary<string, RoleGate> RolesOf(IReadOnlyDictionary<string, RoleGate> roles, RetiredMembers retired)
    {
        // A hand-made file can leave the member out, and the generator then hands over null rather than the default.
        if (roles is null)
        {
            return roles!;
        }

        var scrubbed = roles.ToDictionary(p => p.Key, p => GateOf(p.Value, retired));
        return roles.All(p => ReferenceEquals(p.Value, scrubbed[p.Key])) ? roles : scrubbed;
    }

    private static RoleGate GateOf(RoleGate gate, RetiredMembers retired)
    {
        // A file written before the security lane existed (2026-10-01) has no such member, and the generator hands over null
        // rather than the initializer's gate. The save wrote that null before the scrub existed; it must not throw on it now.
        if (gate is null)
        {
            return gate!;
        }

        var bag = Bag(gate.Unknown, ServerJsonContext.Default.RoleGate, retired);
        return ReferenceEquals(bag, gate.Unknown) ? gate : gate with { Unknown = bag };
    }

    private static RoundRecord RoundOf(RoundRecord round, RetiredMembers retired)
    {
        var rows = Mapped(round.ReviewerStates, v => RowOf(v, retired));
        var bag = Bag(round.Unknown, ServerJsonContext.Default.RoundRecord, retired);

        return Same((round.ReviewerStates, rows), (round.Unknown, bag)) ? round : round with { Unknown = bag, ReviewerStates = rows };
    }

    private static ReviewerState RowOf(ReviewerState row, RetiredMembers retired)
    {
        var bag = Bag(row.Unknown, ServerJsonContext.Default.ReviewerState, retired);
        return ReferenceEquals(bag, row.Unknown) ? row : row with { Unknown = bag };
    }

    /// <summary>
    /// Each contract's names, worked out once: a contract cannot change while the process runs. Bounded by the bagged types
    /// whose type info reaches the scrub — six, from <c>ServerJsonContext.Default</c>.
    /// </summary>
    private static readonly ConcurrentDictionary<JsonTypeInfo, FrozenSet<string>> ContractNames = new();

    /// <summary>
    /// The names of a type's JSON contract, compared without case. The bag itself is listed among a type's properties
    /// (measured: "unknown"); it is the container, not a member of the contract.
    /// </summary>
    internal static IReadOnlySet<string> KnownNamesOf(JsonTypeInfo type) =>
        ContractNames.GetOrAdd(
            type,
            static t => t.Properties.Where(p => !p.IsExtensionData).Select(p => p.Name).ToFrozenSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>The bag without the names this build knows or has retired — the same dictionary when nothing had to go.</summary>
    private static Dictionary<string, JsonElement>? Bag(Dictionary<string, JsonElement>? bag, JsonTypeInfo type, RetiredMembers retired) =>
        bag is null ? null : Kept(bag, Dropper(type, retired));

    private static Func<string, bool> Dropper(JsonTypeInfo type, RetiredMembers retired)
    {
        var known = KnownNamesOf(type);
        var gone = retired.Of(type.Type);
        return name => known.Contains(name) || gone.Contains(name);
    }

    /// <summary>A copy is made only when a name has to go; otherwise the bag the caller gave is the answer.</summary>
    private static Dictionary<string, JsonElement>? Kept(Dictionary<string, JsonElement> bag, Func<string, bool> drops) =>
        Compact(bag.Keys.Any(drops) ? bag.Where(p => !drops(p.Key)).ToDictionary(p => p.Key, p => p.Value) : bag);

    /// <summary>A bag that ends empty is no bag, so a file with nothing unknown reads and writes as it always did.</summary>
    private static Dictionary<string, JsonElement>? Compact(Dictionary<string, JsonElement> bag) => bag.Count == 0 ? null : bag;

    private static List<T> Mapped<T>(List<T> items, Func<T, T> scrub)
    {
        var mapped = items.Select(scrub).ToList();
        return mapped.Zip(items).All(p => ReferenceEquals(p.First, p.Second)) ? items : mapped;
    }

    private static bool Same(params (object? Before, object? After)[] pairs) =>
        pairs.All(p => ReferenceEquals(p.Before, p.After));
}

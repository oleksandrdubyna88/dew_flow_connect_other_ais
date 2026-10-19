using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>One round window an agent was active in: the round and which of its two windows.</summary>
public sealed record RoundRef(string Stage, int Number, string Window);

/// <summary>
/// Someone who contributed to the change under review: one agent of one caller session, with every model, request, effort and
/// association it was seen on and every round window it was active in.
/// </summary>
public sealed record Contributor(
    string CallerSessionId,
    AgentKey Agent,
    string Who,
    string AgentType,
    ImmutableArray<string> Models,
    ImmutableArray<string> Requested,
    ImmutableArray<string> Efforts,
    ImmutableArray<string> Associations,
    ImmutableArray<RoundRef> ActiveIn,
    int WriteCalls);

/// <summary>A round the contributors are a floor for: <paramref name="Why"/> is its state and reasons.</summary>
public sealed record NotSeen(string Stage, int Number, string Why);

public sealed record ContributorSet(ImmutableArray<Contributor> Items, ImmutableArray<NotSeen> NotSeen);

/// <summary>
/// Who contributed to the change a round reviewed (E4.S4): derived from the stored rows, never stored a second time. Pure and
/// ordinal. Only ROWS make a contributor — ambiguous, elsewhere and claimed-by-peer totals never do, so nobody is guessed in.
/// </summary>
/// <remarks>
/// <para>Identity is (caller session, agent): two caller sessions' <c>main</c> are two contributors, and one agent in two windows is
/// one contributor active in both. The journal is every stage, so a plan round's window is in the union and is TAGGED (stage and
/// number in <see cref="RoundRef"/>); whether a plan author is a code author is E6's cohort to choose. A model, request or effort
/// of <c>unknown</c> is a value like any other: the launch-only and two-repositories rows carry it, and a reader that wants models
/// reads the rows it means.</para>
/// <para>An unobserved round contributes nothing and a partial one contributes as a floor; both are named in
/// <see cref="ContributorSet.NotSeen"/> with <c>&lt;state&gt;: &lt;reasons&gt;</c>.</para>
/// </remarks>
public static class Contributors
{
    public static ContributorSet Of(IReadOnlyList<RoundAuthors> upToN, bool withBeforeOpen) =>
        new(ItemsOf(upToN, withBeforeOpen), NotSeenOf(upToN));

    /// <summary>
    /// The contributors with activity in round N's own window — the people whose work the delta of N measures. A first CODE round has
    /// no earlier diff to be a delta against, so it has no fixers.
    /// </summary>
    public static ContributorSet FixersOf(IReadOnlyList<RoundAuthors> upToN, string stage, int number, bool withBeforeOpen)
    {
        var set = Of(upToN, withBeforeOpen);
        var round = new RoundRef(stage, number, WindowKind.Round);
        var first = upToN.FirstOrDefault(candidate => candidate.Stage == "CodeReview");
        var isFirstCode = first is not null && first.Stage == stage && first.Number == number;

        return isFirstCode ? set with { Items = [] } : set with { Items = [.. set.Items.Where(item => item.ActiveIn.Contains(round))] };
    }

    private static ImmutableArray<Contributor> ItemsOf(IReadOnlyList<RoundAuthors> upToN, bool withBeforeOpen) =>
    [
        .. upToN.Where(round => round.State != ObservationKinds.Unobserved)
            .SelectMany(round => round.Rows.Where(row => withBeforeOpen || row.Window != WindowKind.BeforeOpen).Select(row => (round.CallerSessionId, Row: row)))
            .GroupBy(pair => (pair.CallerSessionId, pair.Row.Agent.Id))
            .Select(group => ContributorOf(group.Key.CallerSessionId, group.Select(pair => pair.Row).ToList()))
            .OrderBy(contributor => contributor.CallerSessionId, StringComparer.Ordinal)
            .ThenBy(contributor => contributor.Agent.IsMain ? 0 : 1)
            .ThenBy(contributor => contributor.Agent.Id, StringComparer.Ordinal),
    ];

    private static Contributor ContributorOf(string caller, List<AuthorRow> rows) => new(
        caller,
        rows[0].Agent,
        rows[0].Who,
        rows.Select(row => row.AgentType).FirstOrDefault(type => type.Length > 0) ?? string.Empty,
        Distinct(rows.Select(row => row.Model)),
        Distinct(rows.Select(row => row.Requested)),
        Distinct(rows.Select(row => row.Effort)),
        Distinct(rows.Select(row => row.Association)),
        [.. rows.Select(row => new RoundRef(row.Stage, row.Number, row.Window)).Distinct().OrderBy(round => round.Stage, StringComparer.Ordinal).ThenBy(round => round.Number).ThenBy(round => round.Window, StringComparer.Ordinal)],
        rows.Sum(row => row.WriteCalls));

    private static ImmutableArray<string> Distinct(IEnumerable<string> values) => [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static ImmutableArray<NotSeen> NotSeenOf(IReadOnlyList<RoundAuthors> upToN) =>
    [
        .. upToN.Where(round => round.State != ObservationKinds.Observed).Select(round => new NotSeen(round.Stage, round.Number, Why(round)))
            .OrderBy(item => item.Stage, StringComparer.Ordinal).ThenBy(item => item.Number),
    ];

    private static string Why(RoundAuthors round) => round.Reasons.Length == 0 ? round.State : round.State + ": " + string.Join(", ", round.Reasons);
}

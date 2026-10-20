using System.Collections.Immutable;

namespace CoaiMcp.Core.Authors;

/// <summary>
/// Aggregates one round's <see cref="WindowAssignment"/> into the rows and totals E5 stores (E4.S3). Pure; every ordering is ordinal,
/// so equal inputs give equal outputs byte for byte and the order of the input never shows.
/// </summary>
public static class RoundAuthorsBuilder
{
    public static RoundAuthors Build(AttributionInput input, WindowAssignment assignment)
    {
        var slices = assignment.Windows.Select(window => new WindowSlice(window, input, assignment)).ToList();
        var windows = slices.Select(slice => slice.Authors()).ToImmutableArray();
        var worst = windows.Select(window => window.State).DefaultIfEmpty(ObservationKinds.Observed).MaxBy(ObservationKinds.Badness) ?? ObservationKinds.Observed;
        var unknown = windows.Any(window => window.State == ObservationKinds.Unobserved);

        return new RoundAuthors(
            assignment.Stage,
            assignment.Number,
            assignment.CallerSessionId,
            worst,
            [.. windows.Where(window => window.State == worst).SelectMany(window => window.Reasons).Distinct(StringComparer.Ordinal)],
            [.. slices.SelectMany(slice => slice.Rows()).OrderBy(RowOrder, StringComparer.Ordinal)],
            Roll(windows, unknown, window => window.Ambiguous),
            Roll(windows, unknown, window => window.Elsewhere),
            Roll(windows, unknown, window => window.ClaimedByPeer),
            unknown ? -1 : UnobservedLaunchesOf(input, assignment),
            windows,
            assignment.PeerGates,
            OutsideTheGateCheckout(slices, input));
    }

    /// <summary>
    /// Each unobserved launch ONCE: the ones placed at an instant (in exactly one window) plus the ones with no instant, which mark every
    /// window but are still one missing transcript (decision 9, G4). Summing the windows' counts would count the second kind per window.
    /// </summary>
    private static int UnobservedLaunchesOf(AttributionInput input, WindowAssignment assignment) =>
        assignment.Unobserved.Length + input.Caller.Unobserved.Count(item => item.At is null);

    /// <summary>Who, in the order a row is stored: window, stage, number, who, agent, request, model, effort, association — ordinal.</summary>
    private static string RowOrder(AuthorRow row) =>
        string.Join('\u001f', row.Window, row.Stage, row.Number.ToString("D9", System.Globalization.CultureInfo.InvariantCulture), row.Who, row.Agent.Id, row.Requested, row.Model, row.Effort, row.Association);

    private static ActivityTotals Roll(ImmutableArray<WindowAuthors> windows, bool unknown, Func<WindowAuthors, ActivityTotals> pick) =>
        unknown ? ActivityTotals.Unknown : Total(windows.Select(pick));

    internal static ActivityTotals Total(IEnumerable<ActivityTotals> parts) => parts.Aggregate(ActivityTotals.Zero, (sum, next) =>
        new ActivityTotals(sum.Agents + next.Agents, sum.Launches + next.Launches, sum.Turns + next.Turns, Add(sum.Tokens, next.Tokens)));

    internal static TokenTerms Add(TokenTerms left, TokenTerms right) =>
        new(left.Input + right.Input, left.Output + right.Output, left.CacheCreation + right.CacheCreation, left.CacheRead + right.CacheRead);

    private static ImmutableArray<CheckoutWrites> OutsideTheGateCheckout(IEnumerable<WindowSlice> slices, AttributionInput input) =>
    [
        .. slices.SelectMany(slice => slice.NestedCheckoutWrites(input.Gate.RepoPath)).GroupBy(root => Repositories.Normalise(root), StringComparer.OrdinalIgnoreCase)
            .Select(group => new CheckoutWrites(group.First(), group.Count())).OrderBy(writes => writes.Root, StringComparer.Ordinal),
    ];
}

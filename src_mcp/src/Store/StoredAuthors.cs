using CoaiMcp.Core.Authors;

namespace CoaiMcp.Store;

/// <summary>
/// D18 of the E6 stories: the ONE mapping from the stored columns and rows of a round to E4.S3's <see cref="RoundAuthors"/> — the input of
/// <see cref="Contributors.Of"/>, so the report builds no second union. Pure.
/// </summary>
/// <remarks>
/// <para>The agent goes through <see cref="RoundAuthorsTable.AgentKeyOf"/>, E5.S2's one inverse of its column mapping — never a second
/// spelling of <c>'' → main</c>. The members step 24 does not project take <see cref="AuthorsSnapshot.Unknown"/>'s values, as E7's heal does:
/// <c>Elsewhere</c> and <c>ClaimedByPeer</c> are <see cref="ActivityTotals.Unknown"/>, <c>UnobservedLaunches</c> is <c>-1</c>, and
/// <c>Windows</c>, <c>PeerGates</c> and <c>WritesOutsideTheGateCheckout</c> are empty. <c>Contributors.Of</c> reads the rows and the state only.</para>
/// <para>Tokens are not projected into <see cref="StoredAuthorRow"/> (no decision of the report reads them per row), so they are zero here.</para>
/// </remarks>
public static class StoredAuthors
{
    public static RoundAuthors Of(RoundFacts round) => new(
        round.Stage,
        round.Number,
        round.Authors.CallerSessionId,
        Word(round.Authors.State),
        [.. round.Authors.Reasons],
        [.. round.Authors.Rows.Select(row => RowOf(round, row))],
        TotalsOf(round.Authors.Ambiguous),
        ActivityTotals.Unknown,
        ActivityTotals.Unknown,
        AuthorsSnapshot.NotMeasured,
        [],
        [],
        []);

    private static AuthorRow RowOf(RoundFacts round, StoredAuthorRow row) => new(
        row.Window,
        round.Stage,
        round.Number,
        row.Who,
        RoundAuthorsTable.AgentKeyOf(row.Agent),
        row.AgentType,
        row.Requested,
        row.Model,
        row.Effort,
        row.Association,
        row.Launches,
        row.Turns,
        new TokenTerms(0, 0, 0, 0),
        row.WriteCalls);

    private static ActivityTotals TotalsOf(AmbiguousFacts ambiguous) =>
        ambiguous.Known
            ? new ActivityTotals(ambiguous.Agents, ambiguous.Launches, ambiguous.Turns, new TokenTerms(ambiguous.InputTokens, ambiguous.OutputTokens, ambiguous.CacheCreationTokens, ambiguous.CacheReadTokens))
            : ActivityTotals.Unknown;

    /// <summary>The state word E4 prints; a round with no recorded state has none.</summary>
    private static string Word(AuthorsStateKind kind) => kind switch
    {
        AuthorsStateKind.Observed => ObservationKinds.Observed,
        AuthorsStateKind.Partial => ObservationKinds.Partial,
        AuthorsStateKind.Unobserved => ObservationKinds.Unobserved,
        _ => string.Empty,
    };
}

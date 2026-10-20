using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;

namespace CoaiMcp.Store;

/// <summary>
/// Who wrote the code under review, as the report names it (D4, D5): the coder mix, the coder effort, the main model, and the per-family counts
/// the cells add up. Pure.
/// </summary>
/// <remarks>
/// <para><b>Coders</b> are the rows of <c>subagent</c> contributors that <c>wrote here</c>; the family comes from the OBSERVED model (the one that
/// answered), the requested family beside it. <b><see cref="ModelFamily.Unknown"/> is not a family</b>: the launch-only and two-repositories rows
/// carry it — write or launch evidence, no model claim — so it never makes a mix <c>mixed</c>; it is appended as <c>+unknown</c> and stays visible.</para>
/// </remarks>
internal static class RoundAuthorsCoders
{
    public const string MainOnly = "main only";
    public const string NoWrite = "no write observed";
    public const string Mixed = "mixed";
    public const string NoCoders = "—";

    internal sealed record Coders(string Mix, string Effort, string MainModel, IReadOnlyList<NamedCount> Launches, IReadOnlyList<NamedCount> Pairs);

    private sealed record CoderRow(string Family, string Requested, string Effort, int Launches);

    /// <param name="authors">The contributing rounds, mapped by D18; <see cref="Contributors.Of"/> is the one union.</param>
    public static Coders Of(IReadOnlyList<RoundAuthors> authors, bool withBeforeOpen)
    {
        var set = Contributors.Of(authors, withBeforeOpen);
        var rows = CoderRows(authors, set, withBeforeOpen);

        return new Coders(MixOf(rows, MainWrote(set)), EffortOf(rows), MainModelOf(set), LaunchesOf(rows), PairsOf(rows));
    }

    private static List<CoderRow> CoderRows(IReadOnlyList<RoundAuthors> authors, ContributorSet set, bool withBeforeOpen)
    {
        var coders = set.Items.Where(IsCoder).Select(item => (item.CallerSessionId, item.Agent.Id)).ToHashSet();

        return
        [
            .. authors
                .SelectMany(round => round.Rows.Select(row => (round.CallerSessionId, Row: row)))
                .Where(pair => (withBeforeOpen || pair.Row.Window != WindowKind.BeforeOpen) && pair.Row.Association == Association.WroteHere && coders.Contains((pair.CallerSessionId, pair.Row.Agent.Id)))
                .Select(pair => new CoderRow(ModelFamily.OfModel(pair.Row.Model), ModelFamily.OfRequested(pair.Row.Requested), Effort.Of(pair.Row.Effort), pair.Row.Launches)),
        ];
    }

    /// <summary>A coder is a subagent that wrote here in some window (D4); a subagent that only ever was a child or shared the session is not.</summary>
    private static bool IsCoder(Contributor item) =>
        !item.Agent.IsMain && item.Associations.Contains(Association.WroteHere);

    private static bool MainWrote(ContributorSet set) =>
        set.Items.Any(item => item.Agent.IsMain && item.Associations.Contains(Association.WroteHere));

    /// <summary>The mix over the KNOWN families (<c>other</c> included): one → it, several → <c>mixed</c>, then <c>+unknown</c> when any coder row is unknown.</summary>
    private static string MixOf(List<CoderRow> rows, bool mainWrote)
    {
        var known = rows.Select(row => row.Family).Where(family => family != ModelFamily.Unknown).Distinct().ToList();
        var unknown = rows.Any(row => row.Family == ModelFamily.Unknown);

        return known.Count > 0 ? Suffixed(known.Count == 1 ? known[0] : Mixed, unknown) : NoKnownFamily(unknown, mainWrote);
    }

    private static string Suffixed(string mix, bool unknown) => unknown ? mix + "+" + ModelFamily.Unknown : mix;

    private static string NoKnownFamily(bool unknown, bool mainWrote) =>
        unknown ? ModelFamily.Unknown : mainWrote ? MainOnly : NoWrite;

    private static string EffortOf(List<CoderRow> rows) =>
        rows.Count == 0 ? NoCoders : string.Join('+', rows.Select(row => row.Effort).Distinct().Order(StringComparer.Ordinal));

    /// <summary>The distinct observed models of <c>main</c>, sorted and joined by <c>+</c>; none → <c>unknown</c> (D5).</summary>
    private static string MainModelOf(ContributorSet set)
    {
        var models = set.Items.Where(item => item.Agent.IsMain).SelectMany(item => item.Models).Select(Named).Distinct().Order(StringComparer.Ordinal).ToList();

        return models.Count == 0 ? ModelFamily.Unknown : string.Join('+', models);
    }

    private static string Named(string model) => model.Length == 0 ? ModelFamily.Unknown : model;

    private static List<NamedCount> LaunchesOf(List<CoderRow> rows) =>
        [.. rows.GroupBy(row => row.Family).Select(group => new NamedCount(group.Key, group.Sum(row => (long)row.Launches))).OrderBy(count => count.Name, StringComparer.Ordinal)];

    /// <summary>Coder rows per (requested family, answered family): the off-diagonal pairs are where a request was answered by another family.</summary>
    private static List<NamedCount> PairsOf(List<CoderRow> rows) =>
        [.. rows.GroupBy(row => PairName(row.Requested, row.Family)).Select(group => new NamedCount(group.Key, group.Count())).OrderBy(count => count.Name, StringComparer.Ordinal)];

    /// <summary>The map key of one pair; the table splits it again at the one newline.</summary>
    public static string PairName(string requested, string answered) => requested + "\n" + answered;
}

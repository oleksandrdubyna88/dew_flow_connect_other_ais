using System.Globalization;
using System.Text;

namespace CoaiMcp.Store;

/// <summary>
/// The <c>--table</c> rendering of a report (E6.S5): the header first, then per cohort and period one table of cells, then the exclusions table.
/// Fixed column order, no colour, ASCII separators. Pure: figures, marks and the header are computed HERE from the sums, never read from them.
/// </summary>
/// <remarks>
/// <para><b>A figure prints <c>Unknown</c>, <c>n/a</c> or a floor (<c>&gt;= n</c>) — never <c>0</c> for a population that does not exist and never
/// blank.</b> The exclusions table lists EVERY reason with its count, zeros included: an absent row and a zero are different claims. Nothing from a
/// store row is printed except dimension values (models, providers, effort words, family names) and a branch name.</para>
/// </remarks>
public static class RoundAuthorsText
{
    private const string Separator = " | ";

    private static readonly string[] NumeratorNames = ["all", "blocking+major", "gate_count", "re-raised", "accepted", "unresolved"];

    public static string Render(RoundAuthorsAnswer answer)
    {
        var sums = new RoundAuthorsSums(answer.Cells, answer.Exclusions);
        List<string> lines = [.. RoundAuthorsHeader.Of(sums, answer.Selection, answer.EffortMarkerKnown), string.Empty, .. answer.Sources.Select(SourceLine), string.Empty];
        foreach (var group in answer.Cells.GroupBy(cell => (cell.Key.Cohort, cell.Key.Period)))
        {
            lines.AddRange(Section(group.Key.Cohort, group.Key.Period, [.. group], answer.Selection.Stage));
        }

        lines.AddRange(ExclusionsTable(answer.Exclusions));

        return string.Join('\n', lines) + "\n";
    }

    private static string SourceLine(RoundAuthorsSource source) =>
        Printable($"source {source.SourceId[..8]} ({source.Platform}) read {source.ReadUtc}: {source.Sessions.Count} gate session(s)");

    private static IEnumerable<string> Section(string cohort, string period, List<CellSums> cells, string stage)
    {
        var corrective = cohort == Cohort.Corrective && stage != nameof(Core.Rounds.Stage.PlanReview);
        var rows = new List<string[]> { Columns(corrective) };
        rows.AddRange(cells.Select(cell => Row(cell, stage, corrective)));

        return [Printable($"cohort {cohort} | period {period}"), .. Aligned(rows), string.Empty];
    }

    private static string[] Columns(bool corrective) =>
    [
        "coder mix", "coder effort", "main model", "reviewer set", "rounds", "sessions", "branches", "top branch", "marks", "selected lines",
        .. NumeratorNames.SelectMany(name => new[] { name + " total", name + " per round", name + " per 100 lines", name + " coverage" }),
        "failed reviewers", "stood down", "partial set", "authors partial (provisional)",
        "ambiguous launches", "ambiguous turns", "ambiguous tokens in/out/cache-w/cache-r",
        "requested->answered", "coder launches", "not counted lines",
        .. corrective ? new[] { "delta lines", "delta coverage", "delta unavailable" }.Concat(NumeratorNames.Select(name => name + " per 100 delta")) : [],
    ];

    private static string[] Row(CellSums cell, string stage, bool corrective) =>
    [
        cell.Key.CoderMix, cell.Key.CoderEffort, cell.Key.MainModel, cell.Key.ReviewerSet,
        Count(cell.Rounds), Count(cell.Sessions.Count), Count(cell.Branches.Count), TopBranch(cell), Marks(cell), Count(cell.SelectedLines),
        .. Numerators(cell).SelectMany(sums => NumeratorCells(sums, stage, cell)),
        Count(cell.FailedReviewers), Count(cell.StoodDownReviewers), Count(cell.PartialSetRounds), $"{cell.AuthorsPartialRounds} ({cell.ProvisionalRounds})",
        Floor(cell.Ambiguous.Launches, cell.Ambiguous), Floor(cell.Ambiguous.Turns, cell.Ambiguous),
        string.Join('/', new[] { cell.Ambiguous.InputTokens, cell.Ambiguous.OutputTokens, cell.Ambiguous.CacheCreationTokens, cell.Ambiguous.CacheReadTokens }.Select(total => Floor(total, cell.Ambiguous))),
        OffDiagonal(cell), Named(cell.CoderLaunches), Named(cell.NotCounted),
        .. corrective ? DeltaCells(cell, stage) : [],
    ];

    private static IEnumerable<NumeratorSums> Numerators(CellSums cell) =>
    [
        cell.Numerators.All, cell.Numerators.Severe, cell.Numerators.GateCount, cell.Numerators.ReRaised, cell.Numerators.Accepted, cell.Numerators.Unresolved,
    ];

    private static string[] NumeratorCells(NumeratorSums sums, string stage, CellSums cell) =>
    [
        sums.CoveredRounds == 0 ? "Unknown" : Count(sums.Total),
        Figure(RoundAuthorsCells.PerRound(sums)),
        Figure(RoundAuthorsCells.PerHundredLines(sums, stage)),
        $"{sums.CoveredRounds}/{cell.Rounds} rounds",
    ];

    private static string[] DeltaCells(CellSums cell, string stage) =>
    [
        cell.DeltaCoveredRounds == 0 ? "Unknown" : Count(cell.DeltaLines),
        $"{cell.DeltaCoveredRounds}/{cell.Rounds} rounds",
        Named(cell.DeltaUnavailable),
        .. Numerators(cell).Select(sums => Figure(RoundAuthorsCells.PerHundredDelta(sums, stage))),
    ];

    private static string TopBranch(CellSums cell)
    {
        var top = cell.Branches.OrderByDescending(branch => branch.Count).ThenBy(branch => branch.Name, StringComparer.Ordinal).FirstOrDefault();

        return top is null ? "Unknown" : $"{top.Name[(top.Name.IndexOf('\n') + 1)..]} {(int)Math.Round(100.0 * top.Count / cell.Rounds, MidpointRounding.ToEven)}%";
    }

    private static string Marks(CellSums cell)
    {
        var marks = RoundAuthorsCells.Marks(cell);

        return marks.Count == 0 ? "-" : string.Join(',', marks);
    }

    /// <summary>The coder rows per (requested family, answered family) where the two differ — a request answered by another family; none is a dash.</summary>
    private static string OffDiagonal(CellSums cell)
    {
        var off = cell.Requested.Select(pair => (Pair: pair.Name.Split('\n'), pair.Count)).Where(pair => pair.Pair[0] != pair.Pair[1]).ToList();

        return off.Count == 0 ? "-" : string.Join(", ", off.Select(pair => $"{pair.Pair[0]}->{pair.Pair[1]} {pair.Count}"));
    }

    private static string Named(IReadOnlyList<NamedCount> counts) =>
        counts.Count == 0 ? "-" : string.Join(", ", counts.OrderByDescending(count => count.Count).ThenBy(count => count.Name, StringComparer.Ordinal).Select(count => $"{count.Name} {count.Count}"));

    private static string Floor(long total, AmbiguousSums sums) => Figure(RoundAuthorsCells.Ambiguous(total, sums));

    private static string Count(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A figure as text: two places, <c>Unknown</c>, <c>n/a</c> or <c>&gt;= n</c>; never zero for a missing population and never blank.</summary>
    internal static string Figure(RoundFigure figure) => figure.State switch
    {
        FigureState.Known => figure.Value.ToString("0.00", CultureInfo.InvariantCulture),
        FigureState.Floor => ">= " + figure.Value.ToString("0", CultureInfo.InvariantCulture),
        FigureState.NotApplicable => "n/a",
        _ => "Unknown",
    };

    private static IEnumerable<string> ExclusionsTable(IReadOnlyList<ExclusionCount> exclusions)
    {
        var rows = new List<string[]> { new[] { "cohort", "period", "reason", "rounds", "because" } };
        rows.AddRange(exclusions.Select(count => new[] { count.Cohort, count.Period, count.Reason, Count(count.Rounds), Named(count.Because) }));

        return ["exclusions", .. Aligned(rows)];
    }

    /// <summary>
    /// A value as the terminal may see it: every control character — an escape that would clear the screen or rewrite a figure, from a store row
    /// or an <c>--add</c> file — is shown as <c>?</c>. The table's own line breaks are added after, never through here.
    /// </summary>
    internal static string Printable(string value) =>
        value.Any(char.IsControl) ? string.Concat(value.Select(at => char.IsControl(at) ? '?' : at)) : value;

    /// <summary>Columns padded to their widest cell, joined by an ASCII separator, the header underlined by dashes — plain text a terminal and a diff both read.</summary>
    private static IEnumerable<string> Aligned(List<string[]> rows)
    {
        rows = [.. rows.Select(row => row.Select(Printable).ToArray())];
        var widths = Enumerable.Range(0, rows[0].Length).Select(column => rows.Max(row => row[column].Length)).ToArray();
        var lines = rows.Select(row => string.Join(Separator, row.Select((cell, at) => cell.PadRight(widths[at]))).TrimEnd()).ToList();

        return lines.Take(1).Concat([new string('-', lines[0].Length)]).Concat(lines.Skip(1));
    }
}

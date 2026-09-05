using System.Globalization;
using System.Text;
using CoaiBench.Model;

namespace CoaiBench.Judging;

/// <summary>What one provider found that no other provider found.</summary>
/// <param name="Provider">codex, gemini, local.</param>
/// <param name="Raw">Every finding it wrote, duplicates and repeats included.</param>
/// <param name="Distinct">How many distinct things those were.</param>
/// <param name="Shared">How many of them another provider also named.</param>
/// <param name="Only">How many nobody else named.</param>
/// <param name="OnlyUseful">Of those, how many the judge thought worth having.</param>
/// <param name="Useful">How many of its distinct findings the judge thought worth having at all.</param>
public sealed record ProviderOverlap(
    string Provider, int Raw, int Distinct, int Shared, int Only, int OnlyUseful, int Useful);

/// <summary>
/// Who found what, and who found it alone.
/// </summary>
/// <remarks>
/// <para>The per-arm table answers "how much of this provider's output was worth having". It cannot
/// answer the question that decides whether to PAY for a second provider: <b>did it find anything
/// the others did not?</b> A model with a fine hit-rate that only ever repeats what codex already
/// said is worth nothing on top of codex, and the counts alone make it look valuable.</para>
/// <para>The server does not merge across providers — every recorded finding carries exactly one —
/// so the matching is done here, and deliberately GENEROUSLY: when two findings are close enough to
/// argue about, they are counted as one. Erring the other way inflates "only I found this", which is
/// precisely the number a second provider gets bought on.</para>
/// <para>Findings are pooled per case and stage across every arm and repeat, so each provider is
/// judged on the union of its attempts — its best showing, not its unluckiest run.</para>
/// </remarks>
public static class Overlap
{
    /// <summary>How far apart two citations of the same thing may be.</summary>
    private const int Nearby = 10;

    private static readonly string[] Ignored =
    [
        "the", "and", "for", "with", "that", "this", "from", "when", "not", "into", "are", "its",
        "but", "can", "has", "have", "was", "will", "may", "does", "than", "then", "there",
    ];

    public static IReadOnlyList<ProviderOverlap> Across(IReadOnlyList<RunRecord> runs)
    {
        var clusters = new List<List<Finding>>();
        var raw = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var one in runs.SelectMany(Findings))
        {
            raw[ProviderOf(one.Finding)] = raw.GetValueOrDefault(ProviderOf(one.Finding)) + 1;
            Place(clusters, one.Finding, one.Group);
        }

        return
        [
            .. raw.Keys
                .Select(p => Count(p, clusters, raw[p]))
                .OrderByDescending(o => o.OnlyUseful)
                .ThenByDescending(o => o.Only),
        ];
    }

    /// <summary>Every finding in the campaign, each tagged with the group it may be compared inside.</summary>
    private static IEnumerable<(Finding Finding, string Group)> Findings(RunRecord run) =>
        run.Stages.SelectMany(s => s.Findings.Select(f => (f, $"{run.Case.Name}|{s.Stage}")));

    /// <summary>The cluster a finding joins, or a new one. Single linkage: close to any member is close.</summary>
    private static void Place(List<List<Finding>> clusters, Finding finding, string group)
    {
        var home = clusters.FirstOrDefault(c => Group(c) == group && c.Exists(other => SameThing(other, finding)));
        if (home is null)
        {
            clusters.Add([Tagged(finding, group)]);

            return;
        }

        home.Add(Tagged(finding, group));
    }

    // The comparison group rides on Role, which the clustering does not otherwise use: one case's
    // findings must never cluster with another case's, and a parallel list of keys would be one more
    // thing to keep in step with the clusters.
    private static Finding Tagged(Finding finding, string group) => finding with { Role = group };

    private static string Group(List<Finding> cluster) => cluster[0].Role;

    private static ProviderOverlap Count(string provider, List<List<Finding>> clusters, int raw)
    {
        var mine = clusters.Where(c => c.Exists(f => Is(f, provider))).ToList();
        var alone = mine.Where(c => c.TrueForAll(f => Is(f, provider))).ToList();

        return new ProviderOverlap(
            provider,
            raw,
            mine.Count,
            mine.Count - alone.Count,
            alone.Count,
            alone.Count(Worthwhile),
            mine.Count(c => c.Where(f => Is(f, provider)).Any(f => f.Useful == "yes")));
    }

    private static bool Worthwhile(List<Finding> cluster) => cluster.Exists(f => f.Useful == "yes");

    private static bool Is(Finding finding, string provider) =>
        string.Equals(ProviderOf(finding), provider, StringComparison.OrdinalIgnoreCase);

    private static string ProviderOf(Finding finding) =>
        finding.Providers is { Count: > 0 } list ? list[0] : "unattributed";

    /// <summary>
    /// Whether two findings are the same finding.
    /// </summary>
    /// <remarks>
    /// The same place, and something in common in what they say. Place first because it is cheap and
    /// decisive: two findings about different files are never the same finding, whatever the words.
    /// Findings that name no file at all are matched on wording alone, and are held to a higher bar
    /// for it.
    /// </remarks>
    internal static bool SameThing(Finding a, Finding b)
    {
        var (left, right) = (Normalise(a.File), Normalise(b.File));

        return left.Length == 0 || right.Length == 0
            ? left.Length == right.Length && Alike(a.Title, b.Title, 0.5)
            : left == right && Near(a.Line, b.Line) && Alike(a.Title, b.Title, 0.25);
    }

    /// <summary>A cited line of 0 is "no line", which cannot disagree with anything.</summary>
    private static bool Near(int a, int b) => a == 0 || b == 0 || Math.Abs(a - b) <= Nearby;

    private static string Normalise(string file) =>
        file.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant();

    /// <summary>Jaccard over the words that carry meaning, so the wording may differ and the subject may not.</summary>
    internal static bool Alike(string a, string b, double bar)
    {
        var (left, right) = (Words(a), Words(b));
        if (left.Count == 0 || right.Count == 0)
        {
            return false;
        }

        var shared = left.Intersect(right, StringComparer.Ordinal).Count();

        return (double)shared / left.Union(right, StringComparer.Ordinal).Count() >= bar;
    }

    private static HashSet<string> Words(string text) =>
    [
        .. new string([.. text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4 && !Ignored.Contains(w, StringComparer.Ordinal)),
    ];

    public static string Table(IReadOnlyList<ProviderOverlap> overlaps)
    {
        var table = new StringBuilder()
            .AppendLine("| provider | findings written | distinct | also found by another | found by it alone | of those, worth having |")
            .AppendLine("|---|---|---|---|---|---|");

        foreach (var one in overlaps)
        {
            table.AppendLine(string.Join(" | ",
            [
                $"| `{one.Provider}`",
                one.Raw.ToString(CultureInfo.InvariantCulture),
                one.Distinct.ToString(CultureInfo.InvariantCulture),
                Share(one.Shared, one.Distinct),
                Share(one.Only, one.Distinct),
                $"**{one.OnlyUseful}** |",
            ]));
        }

        return table.ToString();
    }

    private static string Share(int part, int whole) =>
        whole == 0 ? "—" : $"{part} ({100.0 * part / whole:0}%)";
}

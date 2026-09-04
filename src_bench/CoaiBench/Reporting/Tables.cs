using System.Globalization;
using System.Text;
using CoaiBench.Model;

namespace CoaiBench.Reporting;

/// <summary>
/// The tables a person actually reads: per arm, and per run.
/// </summary>
/// <remarks>
/// <para>Medians rather than means, because one reviewer that hit a rate limit and took nine minutes
/// moves a mean and says nothing about the ordinary case. Both are printed where they differ enough
/// to matter — a bench that hides its spread is a bench that flatters itself.</para>
/// <para><b>Useful findings are counted, never inferred.</b> An unjudged run says `—` in that column
/// rather than a zero, because zero is a measurement and "nobody has looked" is not.</para>
/// </remarks>
public static class Tables
{
    public static string PerArm(IReadOnlyList<RunRecord> runs)
    {
        var table = new StringBuilder()
            .AppendLine("| arm | stage | runs | verdicts | median time | median findings | gating | useful | tokens in / out | cost |")
            .AppendLine("|---|---|---|---|---|---|---|---|---|---|");

        foreach (var group in runs
            .SelectMany(r => r.Stages.Select(s => (r.Arm, Stage: s)))
            .GroupBy(x => (x.Arm, x.Stage.Stage))
            .OrderBy(g => g.Key.Arm, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Stage, StringComparer.Ordinal))
        {
            var stages = group.Select(x => x.Stage).ToList();
            table.AppendLine(Row(group.Key.Arm, group.Key.Stage, stages));
        }

        return table.ToString();
    }

    private static string Row(string arm, string stage, IReadOnlyList<StageResult> stages)
    {
        var findings = stages.SelectMany(s => s.Findings).ToList();
        var judged = findings.Where(f => f.Useful != "unjudged").ToList();
        var useful = judged.Count == 0
            ? "—"
            : $"{judged.Count(f => f.Useful == "yes")}/{judged.Count}";
        var cost = stages.Sum(s => s.CostUsd ?? 0);

        return string.Join(" | ",
        [
            $"| `{arm}`",
            stage,
            stages.Count.ToString(CultureInfo.InvariantCulture),
            Verdicts(stages),
            $"{Median([.. stages.Select(s => s.Seconds)])}s",
            Median([.. stages.Select(s => (double)s.Findings.Count)]).ToString(CultureInfo.InvariantCulture),
            stages.Sum(s => s.GatingCount).ToString(CultureInfo.InvariantCulture),
            useful,
            $"{Thousands(stages.Sum(s => s.TokensIn))} / {Thousands(stages.Sum(s => s.TokensOut))}",
            cost > 0 ? cost.ToString("C2", CultureInfo.InvariantCulture) : "not reported" + " |",
        ]);
    }

    /// <summary>Every verdict that happened, with how often — a failed run must not average away.</summary>
    private static string Verdicts(IReadOnlyList<StageResult> stages) =>
        string.Join(", ", stages
            .GroupBy(s => s.Verdict.Length > 0 ? s.Verdict : "FAILED")
            .OrderByDescending(g => g.Count())
            .Select(g => g.Count() == stages.Count ? g.Key : $"{g.Key}×{g.Count()}"));

    public static string PerRun(IReadOnlyList<RunRecord> runs)
    {
        var table = new StringBuilder()
            .AppendLine("| arm | case | # | lane | stage | verdict | time | findings | gating | tokens in / out |")
            .AppendLine("|---|---|---|---|---|---|---|---|---|---|");

        foreach (var run in runs)
        {
            foreach (var stage in run.Stages)
            {
                table.AppendLine(
                    $"| `{run.Arm}` | {run.Case.Name} | {run.Repeat} | {run.Lane} | {stage.Stage} | "
                    + $"{(stage.Verdict.Length > 0 ? stage.Verdict : "**FAILED**")} | {stage.Seconds}s | "
                    + $"{stage.Findings.Count} | {stage.GatingCount} | "
                    + $"{Thousands(stage.TokensIn)} / {Thousands(stage.TokensOut)} |");
            }

            if (run.Stages.Count == 0)
            {
                table.AppendLine(
                    $"| `{run.Arm}` | {run.Case.Name} | {run.Repeat} | {run.Lane} | — | **NOTHING RAN** | | | | |");
            }
        }

        return table.ToString();
    }

    internal static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToList();

        return Math.Round(sorted[sorted.Count / 2], 1);
    }

    internal static string Thousands(long value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000.0:0.#}M",
        >= 1_000 => $"{value / 1_000.0:0.#}k",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };
}

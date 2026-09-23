using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Commands;

/// <summary>How much work a plan is asking for, from the plan alone.</summary>
/// <param name="Lines">Its length. The cheapest signal, and on its own the weakest.</param>
/// <param name="Steps">Numbered items under a "Build order" heading — one step is nearly one story.</param>
/// <param name="Files">Distinct source files it names.</param>
/// <param name="Areas">Distinct top-level directories it touches: how BROAD the work is.</param>
public sealed record PlanShape(int Lines, int Steps, int Files, int Areas)
{
    /// <summary>How big this plan is — which decides what it is broken into before anybody builds it.</summary>
    /// <remarks>The declaration order is the order of size; <see cref="Verdict"/> relies on it.</remarks>
    public enum Split
    {
        /// <summary>Small enough to build as it stands.</summary>
        AsItIs,

        /// <summary>Stories only, three to five of them.</summary>
        Small,

        /// <summary>Two or three epics of two or three stories.</summary>
        Medium,

        /// <summary>Three or four epics of three or four stories.</summary>
        Large,

        /// <summary>Four or five epics of three to five stories.</summary>
        Huge,
    }

    /// <summary>
    /// The size — the LARGER of what the build steps and the length say. A heuristic, and labelled
    /// as one wherever it is shown.
    /// </summary>
    /// <remarks>
    /// <para><b>Recalibrated for issue #131, on the 187 <c>PLAN_*.md</c> of this repository on
    /// 2026-09-23</b>, measured with this reader. The first rule was calibrated on 23 plans and
    /// answered "epics" for 106 of the 187: plans had grown, the median one names 15 files (the old
    /// threshold was 14), and the area pattern matches <c>src</c>, <c>tests</c> and
    /// <c>research</c> in prose, so nine plans in ten "touch four areas". Neither discriminates any
    /// more, so neither decides; both are still reported in <see cref="Numbers"/>.</para>
    /// <para>This rule sorts the same corpus 15 / 129 / 28 / 10 / 5 — 8 % / 68 % / 14 % / 5 % / 2 % —
    /// and the five it calls Huge include the Team server, who-holds-a-key and
    /// every-message-is-written-down plans, each of which was in fact built as several epics.
    /// Length must be able to raise the size on its own: the last of those has no recognised build
    /// order at all.</para>
    /// </remarks>
    public Split Verdict => (Split)Math.Max((int)BySteps(Steps), (int)ByLength(Lines));

    private static Split BySteps(int steps) => steps switch
    {
        >= 13 => Split.Huge,
        >= 10 => Split.Large,
        >= 7 => Split.Medium,
        >= 3 => Split.Small,
        _ => Split.AsItIs,
    };

    private static Split ByLength(int lines) => lines switch
    {
        > 900 => Split.Huge,
        > 600 => Split.Large,
        > 350 => Split.Medium,
        > 120 => Split.Small,
        _ => Split.AsItIs,
    };

    /// <summary>The numbers, for a command that must say what it is judging.</summary>
    public string Numbers =>
        $"{Lines} lines, {Steps} build step(s), {Files} file(s) named, {Areas} area(s) touched";

    /// <summary>The size and the numbers in one line — what a round records it measured.</summary>
    public string Described => $"{Verdict}: {Numbers}";
}

/// <summary>
/// Reading a plan's shape out of its text.
/// </summary>
/// <remarks>
/// <b>The extraction rules are written down here rather than implied</b>, because a plan is markdown
/// somebody typed and a heuristic that silently reads zero from an unfamiliar layout would call a
/// large plan small. Raised in this change's own plan round. Where the structure is absent the
/// verdict falls back to what can still be counted — length and file names — which is why the rule
/// has a size axis at all.
/// </remarks>
public static class PlanShapeReader
{
    private static readonly Regex NumberedItem = new(@"^\s{0,3}\d+[.)]\s", RegexOptions.Multiline);
    private static readonly Regex BuildHeading = new(@"^##+\s*(build order|steps|implementation)\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex NextHeading = new(@"^##+\s", RegexOptions.Multiline);
    private static readonly Regex FilePath = new(@"[\w./\\-]+\.(cs|ts|tsx|razor|json|yml|yaml|mjs|md|csproj|slnx)\b", RegexOptions.IgnoreCase);
    // A lookbehind rather than `\b` at the start: `\b` before a DOT never matches, so `.github` — a
    // legitimate area, and one of the five this repository has — was invisible to the count. Found
    // by the test that generated a four-area plan and was told it had three.
    private static readonly Regex Area = new(
        @"(?<![\w.])(src_mcp|src_vs_code|src|tests?|\.github|research|todo|prompts|tools|docs)\b",
        RegexOptions.IgnoreCase);

    public static PlanShape Of(string planText)
    {
        var text = planText ?? string.Empty;
        var lines = text.Length == 0 ? 0 : text.Split('\n').Length;
        // By PATH, not by base name: `src/a.cs` and `tests/a.cs` are two files, and collapsing them
        // made a plan naming fourteen of them look like a plan naming nine — which is the threshold
        // the epics verdict turns on. (codex, this change's code round.)
        var files = FilePath.Matches(text)
            .Select(m => m.Value.Replace('\\', '/').TrimStart('.', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var areas = Area.Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new PlanShape(lines, StepsIn(text), files, areas);
    }

    /// <summary>
    /// Numbered items under a build-order heading, or — when there is no such heading — the longest
    /// run of numbered items anywhere, which is what an unfamiliar layout still gives away.
    /// </summary>
    private static int StepsIn(string text)
    {
        var heading = BuildHeading.Match(text);
        if (!heading.Success)
        {
            // The longest CONTIGUOUS run, which is what the docstring above promises and what the
            // first version did not do: counting every numbered line anywhere turned four
            // acceptance criteria, a numbered example and a checklist into "nine build steps".
            return LongestRun(text);
        }
        var after = text[(heading.Index + heading.Length)..];
        var next = NextHeading.Match(after);

        return NumberedItem.Matches(next.Success ? after[..next.Index] : after).Count;
    }

    /// <summary>The longest unbroken sequence of numbered lines — a build order without a heading.</summary>
    /// <remarks>
    /// Blank lines and indented continuations do not break a run: a numbered step in this repository
    /// is routinely three wrapped lines with a blank one after it. What breaks a run is prose that
    /// starts a new line without a number, which is exactly what separates a build order from a
    /// checklist somewhere else in the document.
    /// </remarks>
    private static int LongestRun(string text)
    {
        var longest = 0;
        var current = 0;
        foreach (var line in text.Split('\n'))
        {
            if (NumberedItem.IsMatch(line))
            {
                current += 1;
                longest = Math.Max(longest, current);
            }
            else if (line.Trim().Length > 0 && !line.StartsWith("   ", StringComparison.Ordinal))
            {
                current = 0;
            }
        }

        return longest;
    }
}

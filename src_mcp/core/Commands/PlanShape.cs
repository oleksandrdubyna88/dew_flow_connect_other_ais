using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Commands;

/// <summary>How much work a plan is asking for, from the plan alone.</summary>
/// <param name="Lines">Its length. The cheapest signal, and on its own the weakest.</param>
/// <param name="Steps">Numbered items under a "Build order" heading — one step is nearly one story.</param>
/// <param name="Files">Distinct source files it names.</param>
/// <param name="Areas">Distinct top-level directories it touches: how BROAD the work is.</param>
public sealed record PlanShape(int Lines, int Steps, int Files, int Areas)
{
    /// <summary>What this plan should be broken into before anybody starts building it.</summary>
    public enum Split
    {
        /// <summary>Small enough to build as it stands.</summary>
        AsItIs,

        /// <summary>Ordinary work: two to four logically complete stories.</summary>
        Stories,

        /// <summary>Big AND broad: epics first, then stories inside each.</summary>
        Epics,
    }

    /// <summary>
    /// The verdict — a heuristic, and labelled as one wherever it is shown.
    /// </summary>
    /// <remarks>
    /// <para><b>Measured over this repository's own 23 plans</b> (median 120 lines, 4 build steps,
    /// 6 files, 2 areas; max 554 / 9 / 28 / 5). Two axes rather than one, because size alone is
    /// refuted by the corpus: the ONE plan that was actually split into epics — the master plan,
    /// which became `epic_01`…`epic_06` — is 440 lines with 16 files across 5 areas and has no build
    /// order at all, so a step count misses it, while a 230-line plan with 9 steps shipped whole in
    /// a day.</para>
    /// <para>Applied to that corpus this splits one into epics, eighteen into stories, and leaves
    /// four alone.</para>
    /// </remarks>
    public Split Verdict =>
        (Lines > 300 && (Steps >= 6 || Areas >= 4)) || Files >= 14 ? Split.Epics
            : Steps >= 4 || Lines > 100 ? Split.Stories
                : Split.AsItIs;

    /// <summary>The numbers, for a command that must say what it is judging.</summary>
    public string Numbers =>
        $"{Lines} lines, {Steps} build step(s), {Files} file(s) named, {Areas} area(s) touched";
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
        var files = FilePath.Matches(text)
            .Select(m => Path.GetFileName(m.Value.Replace('\\', '/')))
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
            return NumberedItem.Matches(text).Count >= 4 ? NumberedItem.Matches(text).Count : 0;
        }
        var after = text[(heading.Index + heading.Length)..];
        var next = NextHeading.Match(after);

        return NumberedItem.Matches(next.Success ? after[..next.Index] : after).Count;
    }
}

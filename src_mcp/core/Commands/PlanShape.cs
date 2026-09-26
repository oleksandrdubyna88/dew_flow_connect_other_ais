using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Commands;

/// <summary>How much work a plan is asking for, from the plan alone.</summary>
/// <param name="Lines">Its length. The cheapest signal, and on its own the weakest.</param>
/// <param name="Steps">Numbered items under a "Build order" heading — one step is nearly one story.</param>
/// <param name="Files">Distinct source files it names.</param>
/// <param name="Areas">Distinct top-level directories it touches: how BROAD the work is.</param>
/// <param name="Epics">Distinct <c>Epic N</c> headings — when there are any, they decide the size.</param>
/// <param name="Stories"><c>Story N</c> / <c>Story N.M</c> headings — counted as steps when there are no epics.</param>
public sealed record PlanShape(int Lines, int Steps, int Files, int Areas, int Epics = 0, int Stories = 0)
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

        /// <summary>
        /// Six to fourteen epics — past that, two plans (<c>research/PLAN_consult_on_a_cadence.md</c>). Reached
        /// only by a plan that names its epics: no count of steps or lines was able to tell a ten-epic plan
        /// from a four-epic one.
        /// </summary>
        Massive,
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
    /// <para>This rule sorts the same corpus 15 / 129 / 28 / 10 / 5 — 8 % / 68 % / 14 % / 5 % / 2 % — and,
    /// re-measured after the build-order section stopped ending at a phase sub-heading, 189 plans (this
    /// change's own two among them) 15 / 129 / 29 / 10 / 6. The ones it calls Huge include the Team
    /// server, who-holds-a-key and
    /// every-message-is-written-down plans, each of which was in fact built as several epics.
    /// Length must be able to raise the size on its own: the last of those has no recognised build
    /// order at all.</para>
    /// <para><b>A plan that names its epics has answered the question</b> (<c>research/PLAN_consult_on_a_cadence.md</c>,
    /// D8, 2026-09-25). Measured on email-service's two plans: the 10-epic one has 5 numbered steps and the
    /// 4-epic one 31, and the longer file (1452 lines) is the smaller plan — steps and length both invert.
    /// So <c>Epic N</c> headings decide when present; story headings count as steps when there are no
    /// epics; the table above is untouched for a plan with neither.</para>
    /// <para><b>Re-measured on 201 plans</b> — every <c>PLAN_*.md</c> in <c>research/</c> and <c>todo/</c>
    /// plus those two — sizes before → after: AsItIs 15 → 9, Small 137 → 141, Medium 30 → 31, Large 10 →
    /// 11, Huge 9 → 7, Massive 0 → 2. Thirteen moved, and every move is to the size of the split the
    /// plan's AUTHOR wrote down: the five <c>PLAN_epic_0N</c> files (four story headings each, called
    /// AsItIs) → Small; four plans with three or four epic headings → Medium or Large, including one the
    /// old rule called Large on length (<c>PLAN_the_server_says_what_it_did</c>, 641 lines, three epics) →
    /// Medium; <c>PLAN_family_ci_hardening</c> (five epics) → Huge; the two email-service plans → Massive
    /// and Large; and <c>PLAN_consult_on_a_cadence</c> itself (six epics) → Massive. The heuristic now
    /// echoes a split that was already made instead of contradicting it.</para>
    /// </remarks>
    public Split Verdict => Epics > 0
        ? ByEpics(Epics)
        : (Split)Math.Max((int)BySteps(Math.Max(Steps, Stories)), (int)ByLength(Lines));

    private static Split ByEpics(int epics) => epics switch
    {
        >= 6 => Split.Massive,
        5 => Split.Huge,
        4 => Split.Large,
        >= 2 => Split.Medium,
        _ => Split.Small,
    };

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
    /// <remarks>The heading counts are said only when there ARE headings — epic or story, the two that can
    /// decide the size (CodeRabbit on #540: ten story headings sized a plan Large while the line said
    /// "0 build steps") — so every order recorded before them reads byte for byte as it did.</remarks>
    public string Numbers =>
        $"{Lines} lines, {Steps} build step(s), {Files} file(s) named, {Areas} area(s) touched"
        + (Epics > 0 || Stories > 0 ? $", {Epics} epic heading(s), {Stories} story heading(s)" : string.Empty);

    /// <summary>The size and the numbers in one line — what a round records it measured.</summary>
    public string Described => $"{Verdict}: {Numbers}";
}

/// <summary>
/// Reading a plan's shape out of its text.
/// </summary>
/// <remarks>
/// <b>The extraction rules are written down here rather than implied</b>, because a plan is markdown
/// somebody typed and a heuristic that silently reads zero from an unfamiliar layout would call a
/// large plan small. Raised in the first rule's own plan round. Where the structure is absent the
/// verdict falls back to what can still be counted — the length — which is why the rule has a length
/// axis at all.
/// </remarks>
public static class PlanShapeReader
{
    private static readonly Regex NumberedItem = new(@"^\s{0,3}\d+[.)]\s", RegexOptions.Multiline);
    private static readonly Regex BuildHeading = new(@"^(##+)\s*(build order|steps|implementation)\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex NextHeading = new(@"^(#+)\s", RegexOptions.Multiline);
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
        // By PATH, not by base name: `src/a.cs` and `tests/a.cs` are two files. Reported with the
        // size, no longer part of deciding it (issue #131). (codex, the first rule's code round.)
        var files = FilePath.Matches(text)
            .Select(m => m.Value.Replace('\\', '/').TrimStart('.', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var areas = Area.Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Count();

        var outline = PlanOutlineReader.Of(text);

        return new PlanShape(lines, StepsIn(text), files, areas, outline.Epics.Count, outline.Stories);
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
        var next = SectionEnd(after, heading.Groups[1].Length);

        return NumberedItem.Matches(after[..next]).Count;
    }

    /// <summary>Where the build-order section ends: the first heading of its own level or higher.</summary>
    /// <remarks>
    /// Not the first heading of ANY level: a build order written in phases carries "### Phase 1" under
    /// "## Build order", and ending there counted zero steps — harmless while files decided the size,
    /// wrong since issue #131 made the steps decide it. (#131's code review.)
    /// </remarks>
    private static int SectionEnd(string after, int level) =>
        NextHeading.Matches(after).FirstOrDefault(m => m.Groups[1].Length <= level)?.Index ?? after.Length;

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

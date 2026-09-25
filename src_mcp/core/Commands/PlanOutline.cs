using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Commands;

/// <summary>One epic a plan names in a heading.</summary>
/// <param name="Number">Its number as the plan writes it.</param>
/// <param name="Title">What follows the number, without the separator; empty when nothing does.</param>
public sealed record OutlineEpic(int Number, string Title);

/// <summary>The epics and stories a plan names in its own headings.</summary>
/// <param name="Epics">Distinct by number, in number order; the first heading for a number names it.</param>
/// <param name="Stories">How many <c>Story N</c> / <c>Story N.M</c> headings it has.</param>
public sealed record PlanOutline(IReadOnlyList<OutlineEpic> Epics, int Stories)
{
    /// <summary>A plan with no such headings.</summary>
    public static PlanOutline Empty { get; } = new([], 0);

    /// <summary>Whether the plan names any epic.</summary>
    public bool HasEpics => Epics.Count > 0;

    /// <summary>The plan's first epic number, or 1 when it names none.</summary>
    public int FirstNumber => HasEpics ? Epics[0].Number : 1;

    /// <summary>The plan's last epic number, or 0 when it names none.</summary>
    public int LastNumber => HasEpics ? Epics[^1].Number : 0;

    /// <summary>The epics of a group as an order quotes them: <c>Epic 4 — title; Epic 5 — title</c>.</summary>
    public string TitlesOf(int first, int last) => string.Join("; ", Epics
        .Where(epic => epic.Number >= first && epic.Number <= last)
        .Select(epic => epic.Title.Length > 0 ? $"Epic {epic.Number} — {epic.Title}" : $"Epic {epic.Number}"));
}

/// <summary>
/// Reading a plan's outline out of its text.
/// </summary>
/// <remarks>
/// <para><c>todo/PLAN_consult_on_a_cadence.md</c>, D8. Measured on email-service's two plans on
/// 2026-09-25: the 10-epic one has 5 numbered build steps and the 4-epic one 31, and the longer file is
/// the smaller plan. Steps and length both invert; a plan that names its epics has answered the question.</para>
/// <para>A heading is a line that STARTS with two to four <c>#</c> (five for a story) — prose that mentions
/// an epic is not one. Any usual separator may follow the number: an em-dash, an en-dash, a hyphen, a colon
/// or a full stop (epic 1's plan round, gemini). Time-bounded like every pattern here (Sonar S6444).</para>
/// </remarks>
public static partial class PlanOutlineReader
{
    private const int MatchTimeoutMs = 1000;

    public static PlanOutline Of(string planText)
    {
        var text = OutsideFences(planText ?? string.Empty);
        var epics = EpicHeading().Matches(text)
            .Select(m => new OutlineEpic(
                int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                m.Groups[2].Value.Trim()))
            .GroupBy(epic => epic.Number)
            .Select(same => same.First())
            .OrderBy(epic => epic.Number)
            .ToList();

        return new PlanOutline(epics, StoryHeading().Count(text));
    }

    /// <summary>
    /// The text with every fenced code block blanked, line for line — a plan that SHOWS an example outline
    /// must not be sized by it (epic 1's code round, codex). The CommonMark rules (CodeRabbit on #540): a
    /// fence is three or more backticks or tildes at the start of a line; a backtick fence's info string may
    /// not contain a backtick, or the line is prose; it CLOSES only on the same character, at least as long,
    /// with nothing but whitespace after it — so a <c>```js</c> line inside an open block is part of the
    /// block. An unclosed fence runs to the end, as Markdown renders it.
    /// </summary>
    private static string OutsideFences(string text)
    {
        var lines = text.Split('\n');
        var open = string.Empty;
        for (var i = 0; i < lines.Length; i++)
        {
            var fence = Fence().Match(lines[i]);
            var inside = open.Length > 0;
            if (fence.Success && (inside ? Closes(fence, open) : Opens(fence)))
            {
                open = inside ? string.Empty : fence.Groups[1].Value;
            }
            if (inside || open.Length > 0)
            {
                lines[i] = string.Empty;
            }
        }

        return string.Join('\n', lines);
    }

    private static bool Opens(Match fence) => fence.Groups[1].Value[0] == '~' || !fence.Groups[2].Value.Contains('`');

    private static bool Closes(Match fence, string open) =>
        fence.Groups[1].Value[0] == open[0]
        && fence.Groups[1].Value.Length >= open.Length
        && fence.Groups[2].Value.Trim().Length == 0;

    [GeneratedRegex(@"^[ ]{0,3}(`{3,}|~{3,})([^\n]*)$", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Fence();

    // [0-9], not \d: .NET's \d is every Unicode decimal digit, which int.Parse then refuses with an
    // exception where the line should simply not be a heading (epic 1's code round).
    [GeneratedRegex(@"^#{2,4}[ \t]+Epic[ \t]+([0-9]{1,3})\b[ \t]*(?:[—–:.\-][ \t]*)?([^\r\n]*)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex EpicHeading();

    [GeneratedRegex(@"^#{2,5}[ \t]+Story[ \t]+[0-9]{1,3}(?:\.[0-9]{1,3})?\b",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex StoryHeading();
}

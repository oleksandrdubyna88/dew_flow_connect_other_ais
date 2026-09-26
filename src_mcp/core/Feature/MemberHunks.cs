using System.Text;
using CoaiMcp.Core.Outlining;

namespace CoaiMcp.Core.Feature;

/// <summary>The changed lines of one changed member, ready to be shown or named.</summary>
/// <param name="Added">Lines the member gained — all of them, even when <see cref="Text"/> was truncated.</param>
/// <param name="Truncated">The text stops at <see cref="FeatureBudget.MaxHunkBytesPerMember"/> and says how many changed lines it left out.</param>
public sealed record MemberUnit(string Path, OutlineEntry Member, int Added, int Deleted, string Text, bool Truncated = false)
{
    /// <summary>Added plus deleted lines — the order units are chosen and cut in.</summary>
    public int Size => Added + Deleted;

    public CutHunk AsCut => new(Path, Member.StartLine, Member.EndLine, Added, Deleted);
}

/// <summary>The outline section with the member hunks that fit, and every one that did not.</summary>
/// <param name="Kept">The units shown, in the order they are shown — largest change first.</param>
public sealed record FittedHunks(string Section, IReadOnlyList<MemberUnit> Kept, IReadOnlyList<CutHunk> Cut);

/// <summary>
/// D22's second half: under the outline, the changed hunks of every changed member — attributed to the
/// innermost changed member, each capped at <see cref="FeatureBudget.MaxHunkBytesPerMember"/>, chosen
/// smallest change first so the LARGEST changes are the ones cut, and every cut named.
/// </summary>
/// <remarks>
/// <para><b>Attribution is the feature-pack trial's arm D</b> (2026-09-26), which is what confirmed D22;
/// <b>the cap and the order are its arm F's lesson</b>. The behaviour is the trial's; the code is not.</para>
/// <list type="bullet">
/// <item>Only CHANGED members contribute: those marked <c>*</c>, or every member of an added file.</item>
/// <item>A diff line belongs to the innermost changed member containing it — the deepest, then the
/// narrowest — so a method's edit is shown under the method, not under its class. Lines outside every
/// changed member (imports, file headers) are not shown.</item>
/// <item>A member whose lines are all context is no unit: nothing in it changed.</item>
/// <item><b>Capped per member.</b> Arm D kept largest change first, and on the trial's tsx2 six units took
/// all 62 KB of hunk room while 664 members of 20 changed lines or fewer were cut; arm F then found 0 of
/// the 4 planted defects whose member was cut. A member's hunk past the cap is truncated with
/// "[N more changed lines — ask for source]", so one enormous member is still SHOWN and can no longer
/// starve the rest.</item>
/// <item><b>Chosen smallest change first, strictly</b>: at the first unit that does not fit, it and every
/// larger one are cut — named, with their spans, under "What this context left out". On ts2 every unit
/// the old largest-first fill cut had 13 changed lines or fewer, which is exactly the shape of a body
/// edit, planted or real. The units that ARE shown are listed largest change first, so a reader still
/// meets the biggest edits first.</item>
/// </list>
/// </remarks>
public static class MemberHunks
{
    public const string Heading = "## Changed hunks — every changed member's diff, largest change first";

    public static readonly string Legend =
        "> From `git diff -U3 -M base..head`, restricted to the changed members — those marked `*` in the outline (for an "
        + "added file, all of its members); a file elided from the outline for the budget keeps its hunks here. Each diff "
        + "line is shown under the innermost changed member that contains it; lines outside every changed member (imports, "
        + "file headers, members' surroundings) are not shown. `@@ +N @@` gives the line at head where the run starts. "
        + "This section and the outline above share the outline's byte budget, and the hunks are placed first, up to a "
        + "third of it: they were chosen smallest change first, so the largest changes are the ones cut, each named under "
        + $"\"What this context left out\"; a member's hunk longer than {FeatureBudget.MaxHunkBytesPerMember / 1024} KB is "
        + "shown truncated and ends with \"[N more changed lines — ask for source]\".";

    /// <summary>Slack under the budget before the fill starts, so the cut note's own digits never tip it over.</summary>
    private const int Slack = 16;

    /// <summary>Every changed member of <paramref name="file"/> with at least one changed line in it, each within <paramref name="cap"/> bytes.</summary>
    public static IReadOnlyList<MemberUnit> Units(OutlinedFile file, int cap = FeatureBudget.MaxHunkBytesPerMember)
    {
        var changed = file.Outline.Entries.Where(e => file.File.Change == FileChange.Added || OutlineComposer.IsMarked(file, e)).ToList();
        var owned = file.Hunks
            .SelectMany(line => Innermost(changed, line.NewLine).Select(owner => (Line: line, Owner: owner)))
            .GroupBy(p => p.Owner);

        return [.. owned
            .Select(g => Unit(file.File.Path, g.Key, [.. g.Select(p => p.Line)], cap))
            .Where(u => u.Size > 0)];
    }

    /// <summary>Fits the units under <paramref name="outline"/> within <paramref name="budget"/> bytes.</summary>
    public static FittedHunks Fit(string outline, IReadOnlyList<MemberUnit> units, int budget)
    {
        var ordered = Ordered(units);
        if (ordered.Count == 0)
        {
            return new FittedHunks(outline, [], []);
        }

        var room = budget - OutlineComposer.Bytes(Render(outline, [], cut: 1)) - Slack;
        var (kept, cut) = Fill(ordered, room);

        return Shrink(outline, kept, cut, budget);
    }

    /// <summary>
    /// How many bytes of a section the units that fit <paramref name="reserve"/> will take — what the
    /// outline must leave them (<see cref="FeatureBudget.HunkReserveBytes"/>). Zero when not even the
    /// smallest unit fits beside the section's own heading, legend and note.
    /// </summary>
    /// <remarks>
    /// Counted exactly as <see cref="Fit"/> counts: the fixed overhead of the hunk section (its heading,
    /// its legend, a one-digit cut note, the blank line after the outline and the slack the note's digits
    /// may need), then each unit that the smallest-first fill takes. An outline cut to the budget minus
    /// this is therefore an outline beside which every one of those units fits again in <see cref="Fit"/>
    /// — the same prefix of the same order — and any room the outline did not need goes to further units.
    /// </remarks>
    public static int Reserved(IReadOnlyList<MemberUnit> units, int reserve)
    {
        var overhead = OutlineComposer.Bytes(Render(string.Empty, [], cut: 1)) + Separator + Slack;
        var (kept, _) = Fill(Ordered(units), reserve - overhead);

        return kept.Count == 0 ? 0 : overhead + kept.Sum(u => OutlineComposer.Bytes(u.Text) + 1);
    }

    /// <summary>The blank line between the outline and the hunk section's heading.</summary>
    private const int Separator = 2;

    private static List<MemberUnit> Ordered(IReadOnlyList<MemberUnit> units) =>
        [.. units.OrderBy(u => u.Size).ThenBy(u => u.Path, StringComparer.Ordinal).ThenBy(u => u.Member.StartLine)];

    /// <summary>Smallest change first; the first that does not fit stops the fill, and it and every larger one are cut.</summary>
    private static (List<MemberUnit> Kept, List<MemberUnit> Cut) Fill(IReadOnlyList<MemberUnit> ordered, int room)
    {
        var (kept, cut) = (new List<MemberUnit>(), new List<MemberUnit>());
        var (left, stopped) = (room, false);
        foreach (var unit in ordered)
        {
            var bytes = OutlineComposer.Bytes(unit.Text) + 1;
            var fits = !stopped && bytes <= left;
            (fits ? kept : cut).Add(unit);
            left -= fits ? bytes : 0;
            stopped |= !fits;
        }

        return (kept, cut);
    }

    /// <summary>The last guard: while the rendered section is over, the largest kept unit goes — the fill's own order, continued.</summary>
    private static FittedHunks Shrink(string outline, List<MemberUnit> kept, List<MemberUnit> cut, int budget)
    {
        var section = Render(outline, kept, cut.Count);
        while (OutlineComposer.Bytes(section) > budget && kept.Count > 0)
        {
            cut.Add(kept[^1]);
            kept.RemoveAt(kept.Count - 1);
            section = Render(outline, kept, cut.Count);
        }

        // Not even the heading and the note fit beside the outline: the section is the outline alone,
        // and every unit is still named as cut.
        var final = OutlineComposer.Bytes(section) <= budget ? section : outline;

        return new FittedHunks(final, ShownOrder(kept), [.. cut.Select(u => u.AsCut).OrderBy(c => c.Path, StringComparer.Ordinal).ThenBy(c => c.Start)]);
    }

    /// <summary>The units a reader sees, largest change first — chosen smallest first, shown biggest first.</summary>
    private static IReadOnlyList<MemberUnit> ShownOrder(IEnumerable<MemberUnit> kept) =>
        [.. kept.OrderByDescending(u => u.Size).ThenBy(u => u.Path, StringComparer.Ordinal).ThenBy(u => u.Member.StartLine)];

    private static string Render(string outline, IReadOnlyList<MemberUnit> kept, int cut)
    {
        var text = new StringBuilder(outline);
        text.Append(outline.Length > 0 ? "\n\n" : string.Empty).Append(Heading).Append("\n\n").Append(Legend).Append("\n\n");
        text.AppendJoin("\n", ShownOrder(kept).Select(u => u.Text));
        if (cut > 0)
        {
            text.Append($"\n> {cut} further member hunk(s) did not fit the budget; every one is named under \"What this context left out\".\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// The deepest changed member containing <paramref name="line"/>, the narrowest on a tie, the first
    /// of equals — or nothing, when no changed member contains it (an import, a file header).
    /// </summary>
    private static IEnumerable<OutlineEntry> Innermost(IReadOnlyList<OutlineEntry> changed, int line)
    {
        var containing = changed.Where(e => e.StartLine <= line && line <= e.EndLine).ToList();

        return containing.Count == 0 ? [] : [containing.Aggregate((best, entry) => Deeper(entry, best) ? entry : best)];
    }

    private static bool Deeper(OutlineEntry entry, OutlineEntry than) =>
        entry.Depth > than.Depth || (entry.Depth == than.Depth && entry.EndLine - entry.StartLine < than.EndLine - than.StartLine);

    /// <summary>One member's unit: its heading, then its hunk runs in a fenced diff — truncated at the cap.</summary>
    private static MemberUnit Unit(string path, OutlineEntry member, IReadOnlyList<DiffLine> lines, int cap)
    {
        var added = lines.Count(l => l.Kind == '+');
        var deleted = lines.Count(l => l.Kind == '-');
        var heading = $"#### {path} — {member.Signature} [{member.StartLine}-{member.EndLine}] (+{added}/-{deleted})\n\n";
        var rows = Rows(lines);
        var fence = Fence(string.Join("\n", rows.Select(r => r.Text)));
        var whole = Block(heading, fence, rows.Select(r => r.Text));
        if (OutlineComposer.Bytes(whole) <= cap)
        {
            return new MemberUnit(path, member, added, deleted, whole);
        }

        // The marker is measured with the largest number it could carry, so its digits never break the cap.
        var taken = Within(rows, cap - OutlineComposer.Bytes(Block(heading, fence, [Marker(added + deleted)])));
        var left = rows.Skip(taken).Count(r => r.Changed);

        return new MemberUnit(path, member, added, deleted, Block(heading, fence, [.. rows.Take(taken).Select(r => r.Text), Marker(left)]), Truncated: true);
    }

    /// <summary>The hunk runs as rows: a <c>@@ +N @@</c> row opening each run, then its lines.</summary>
    private static IReadOnlyList<(string Text, bool Changed)> Rows(IReadOnlyList<DiffLine> lines) =>
        [.. lines.GroupBy(l => l.Hunk).OrderBy(g => g.Key).SelectMany(g =>
            new[] { ($"@@ +{g.First().NewLine} @@", false) }.Concat(g.Select(l => (l.Text, l.Kind != ' '))))];

    /// <summary>How many rows fit in <paramref name="room"/> bytes, each with the newline that joins it.</summary>
    private static int Within(IReadOnlyList<(string Text, bool Changed)> rows, int room)
    {
        var (taken, used) = (0, 0);
        while (taken < rows.Count && used + OutlineComposer.Bytes(rows[taken].Text) + 1 <= room)
        {
            used += OutlineComposer.Bytes(rows[taken].Text) + 1;
            taken++;
        }

        return taken;
    }

    private static string Marker(int changedLinesLeft) => $"[{changedLinesLeft} more changed lines — ask for source]";

    private static string Block(string heading, string fence, IEnumerable<string> rows) =>
        $"{heading}{fence}diff\n{string.Join("\n", rows)}\n{fence}\n";

    /// <summary>A code fence longer than any run of backticks in the body, so the body cannot close it.</summary>
    private static string Fence(string body)
    {
        var (longest, run) = (0, 0);
        foreach (var c in body)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return new string('`', Math.Max(3, longest + 1));
    }
}

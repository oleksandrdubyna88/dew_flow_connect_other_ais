using System.Text;
using CoaiMcp.Core.Outlining;

namespace CoaiMcp.Core.Feature;

/// <summary>The outline section as composed, and what composing it left out.</summary>
/// <param name="Section">Outlines, then member hunks — never more UTF-8 bytes than the budget it was composed for.</param>
/// <param name="Outlined">How many files the section outlines.</param>
/// <param name="Notes">Sentences for what was shortened rather than cut — member hunks truncated at the cap.</param>
public sealed record ComposedOutline(
    string Section,
    int Outlined,
    IReadOnlyList<CollapsedFile> Collapsed,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<CutHunk> CutHunks,
    IReadOnlyList<string> Notes);

/// <summary>
/// Composes the reviewer's view of the code: every outlined file's declarations with the changed ones
/// marked, then the changed hunks of each changed member — all inside ONE byte budget, cut
/// deterministically, every cut named (plan §4.6–4.7, D22).
/// </summary>
/// <remarks>
/// <para><b>The order of cutting is the trial's</b> (the feature-pack trial of 2026-09-26, whose arm D
/// confirmed D22): first large files lose their UNCHANGED members, largest file first, down to
/// <see cref="FeatureBudget.CollapseAboveBytes"/>; then whole files drop, smallest change first; the
/// member hunks are each capped and chosen smallest change first (<see cref="MemberHunks"/>).</para>
/// <para><b>The hunks go first, up to their reserve</b> (<see cref="FeatureBudget.HunkReserveBytes"/>,
/// S2.2b): S2.2a let the outline take the budget and gave the hunks what it left, and on the median and
/// larger features that was nothing — the hybrid silently became the outline-only arm the trial had
/// found worse. Now the hunks that fit the reserve are placed first, the outline is cut to what they
/// leave, a reserve the hunks did not need flows back to the outline, and room the outline did not need
/// flows on to further hunks. Past the reserve the outline, the map a reviewer navigates by, is still
/// never cut to make room for code.</para>
/// <para>Pure and deterministic: files arrive in any order and are ordered here by
/// <see cref="ChangedFile.Ordered(IEnumerable{ChangedFile})"/>, so the same range composes the same bytes every time.</para>
/// </remarks>
public static class OutlineComposer
{
    public const string OutlineHeading = "## Outlines — every changed file at head, largest change first";

    public const string OutlineLegend =
        "> `*` marks a declaration that intersects a changed hunk; `(A, new, …)` marks an added file (none of its "
        + "members are starred). Signatures only — no bodies. Line numbers are at head.";

    /// <summary>Composes the section for <paramref name="files"/> within <paramref name="budget"/> bytes.</summary>
    public static ComposedOutline Compose(IReadOnlyList<OutlinedFile> files, int budget, int collapseAbove)
    {
        var rendered = ChangedFile.Ordered(files, f => f.File).Select(f => Rendered.Of(f, Render(f, collapse: false), 0)).ToList();
        IReadOnlyList<MemberUnit> units = [.. rendered.SelectMany(r => MemberHunks.Units(r.File))];

        // The hunks are placed FIRST, inside their reserve, and the outline is cut to what they leave
        // (FeatureBudget.HunkReserveBytes): composed the other way round, an outline that alone overfilled
        // the budget left no room for a single hunk. A dropped file's hunks stay candidates — dropping goes
        // smallest change first, which is exactly where the small edits live.
        var outlineBudget = budget - MemberHunks.Reserved(units, FeatureBudget.HunkReserveFor(budget));
        var (smaller, collapsed) = Collapse(rendered, outlineBudget, collapseAbove);
        var (kept, dropped) = Drop(smaller, outlineBudget);
        var fitted = MemberHunks.Fit(Section(kept), units, budget);

        var truncated = fitted.Kept.Count(u => u.Truncated);

        return new ComposedOutline(fitted.Section, kept.Count, collapsed, dropped, fitted.Cut, truncated == 0 ? [] :
        [
            $"{truncated} member hunk(s) longer than {FeatureBudget.MaxHunkBytesPerMember / 1024} KB are shown truncated; each ends "
            + "with how many changed lines it left out — ask for source by symbol or span.",
        ]);
    }

    /// <summary>Whether an entry is marked: it intersects a changed span, in a file that is not new.</summary>
    public static bool IsMarked(OutlinedFile file, OutlineEntry entry) =>
        file.File.Change != FileChange.Added && file.Changed.Any(s => s.Intersects(entry.StartLine, entry.EndLine));

    /// <summary>The outline section text for <paramref name="kept"/>, or empty when there is nothing to outline.</summary>
    public static string Section(IReadOnlyList<Rendered> kept) =>
        kept.Count == 0 ? string.Empty : Preamble + string.Join("\n", kept.Select(r => r.Text));

    public static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static readonly string Preamble = $"{OutlineHeading}\n\n{OutlineLegend}\n\n";

    /// <summary>What <see cref="Section"/> would weigh — computed, so cutting never re-joins the whole section.</summary>
    private static int Total(IReadOnlyList<Rendered> kept) =>
        kept.Count == 0 ? 0 : Bytes(Preamble) + kept.Sum(r => r.Bytes) + (kept.Count - 1);

    /// <summary>First the largest files lose their unchanged members, until the section fits or no large file is left.</summary>
    private static (List<Rendered> Files, IReadOnlyList<CollapsedFile> Collapsed) Collapse(List<Rendered> files, int budget, int collapseAbove)
    {
        var result = files.ToList();
        var collapsed = new List<CollapsedFile>();
        var largestFirst = Enumerable.Range(0, files.Count).OrderByDescending(i => files[i].Bytes).ThenBy(i => i);
        foreach (var i in largestFirst)
        {
            if (Total(result) <= budget || files[i].Bytes <= collapseAbove)
            {
                break;
            }

            result[i] = Rendered.Of(files[i].File, Render(files[i].File, collapse: true), Elided(files[i].File));
            collapsed.Add(new CollapsedFile(files[i].File.File.Path, result[i].Elided));
        }

        return (result, collapsed);
    }

    /// <summary>Then whole files drop, the smallest change first, until the section fits.</summary>
    private static (IReadOnlyList<Rendered> Kept, IReadOnlyList<string> Dropped) Drop(List<Rendered> files, int budget)
    {
        var kept = files.ToList();
        var dropped = new List<string>();
        while (kept.Count > 0 && Total(kept) > budget)
        {
            dropped.Add(kept[^1].File.File.Path);
            kept.RemoveAt(kept.Count - 1);
        }

        return (kept, dropped);
    }

    /// <summary>One file's outline: its heading, then one line per declaration, marked where it changed.</summary>
    /// <remarks>
    /// Collapsed, a file keeps its marked entries, its top-level entries, and every ancestor of a marked
    /// entry — so the path from the file to a change is always visible — and each run of the rest becomes
    /// one "N unchanged members elided" line.
    /// </remarks>
    private static string Render(OutlinedFile file, bool collapse)
    {
        var entries = file.Outline.Entries;
        var keep = collapse ? Kept(file) : [.. entries.Select(_ => true)];
        var text = new StringBuilder(Heading(file.File)).Append('\n');
        var run = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            run = keep[i] ? Flush(text, run) : run + 1;
            if (keep[i])
            {
                text.Append(Line(file, entries[i])).Append('\n');
            }
        }

        Flush(text, run);

        return text.ToString();
    }

    private static int Elided(OutlinedFile file) => Kept(file).Count(k => !k);

    private static bool[] Kept(OutlinedFile file)
    {
        var entries = file.Outline.Entries;
        var keep = entries.Select(e => e.Depth == 0 || IsMarked(file, e)).ToArray();
        for (var i = 0; i < entries.Length; i++)
        {
            if (IsMarked(file, entries[i]))
            {
                KeepAncestors(entries, keep, i);
            }
        }

        return keep;
    }

    private static void KeepAncestors(IReadOnlyList<OutlineEntry> entries, bool[] keep, int marked)
    {
        var depth = entries[marked].Depth;
        for (var j = marked - 1; j >= 0 && depth > 0; j--)
        {
            if (entries[j].Depth < depth)
            {
                keep[j] = true;
                depth = entries[j].Depth;
            }
        }
    }

    private static int Flush(StringBuilder text, int run)
    {
        if (run > 0)
        {
            text.Append("    … ").Append(run).Append(" unchanged members elided\n");
        }

        return 0;
    }

    private static string Line(OutlinedFile file, OutlineEntry entry) =>
        (IsMarked(file, entry) ? "* " : "  ") + new string(' ', entry.Depth * 2) + $"{entry.Signature} [{entry.StartLine}-{entry.EndLine}]";

    /// <summary><c>### path (M, +3/-1)</c> — with <c>, new</c> for an added file and the old name for a rename.</summary>
    public static string Heading(ChangedFile file) =>
        $"### {file.Path} ({file.Letter}{(file.Change == FileChange.Added ? ", new" : string.Empty)}, +{file.Added}/-{file.Deleted})"
        + (file.Change == FileChange.Renamed ? $" — renamed from {file.RenamedFrom}" : string.Empty);

    /// <summary>A file with its rendered outline, as the budget sees it.</summary>
    public sealed record Rendered(OutlinedFile File, string Text, int Elided, int Bytes)
    {
        public static Rendered Of(OutlinedFile file, string text, int elided) => new(file, text, elided, OutlineComposer.Bytes(text));
    }
}

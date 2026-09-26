using System.Text;

namespace CoaiMcp.Core.Feature;

/// <summary>
/// "Files not outlined" and "What this context left out" — the two sections that are never cut, held
/// inside <see cref="FeatureBudget.OmissionsReserveBytes"/> by saying the same thing more briefly.
/// </summary>
/// <remarks>
/// <para><b>Brevity, never truncation.</b> A list that stopped at the reserve would drop exactly the
/// names the section exists to give, so the renderer tries four levels of detail and takes the first
/// that fits: every file with its size and reason; every path grouped by reason; paths folded into
/// their directories, deepest first; and last, counts by reason — the one level that no longer names
/// each file, and it says so in as many words. The context's own lines (the plan was cut, the lessons
/// were cut) are kept verbatim at every level: they are few, and they are about the reviewer's inputs,
/// not about files.</para>
/// <para>Pure: the same omissions render the same bytes, in the order they arrive — the builder hands
/// them over in change-size order and cut hunks by path, so that order is the reviewer's too.</para>
/// </remarks>
public static class OmissionsRenderer
{
    public const string NotOutlinedHeading = "## Files not outlined";

    public const string LeftOutHeading = "## What this context left out";

    public const string NothingLeftOut = "- Nothing: every changed file is outlined above or listed as not outlined.";

    /// <summary>How finely the lists are spelled, from the most detailed down.</summary>
    public enum Detail
    {
        Full,
        ByReason,
        ByDirectory3,
        ByDirectory2,
        ByDirectory1,
        Counts,
    }

    /// <summary>The two sections at the most detailed level that fits in <paramref name="reserve"/> bytes.</summary>
    public static string Render(FeatureOmissions omissions, IReadOnlyList<string> contextLines, int reserve) =>
        Enum.GetValues<Detail>().Select(d => Render(omissions, contextLines, d)).FirstOrDefault(t => Bytes(t) <= reserve)
        ?? Render(omissions, contextLines, Detail.Counts);

    /// <summary>The two sections at one level of detail — exposed so a test can hold each level to its promise.</summary>
    public static string Render(FeatureOmissions omissions, IReadOnlyList<string> contextLines, Detail detail)
    {
        var text = new StringBuilder(NotOutlinedHeading).Append("\n\n");
        text.Append(omissions.NotOutlined.Count == 0 ? "- none\n" : NotOutlinedList(omissions.NotOutlined, detail));
        text.Append('\n').Append(LeftOutHeading).Append("\n\n");

        var lines = contextLines.Concat(LeftOutLines(omissions, detail)).ToList();
        text.AppendJoin("\n", lines.Count == 0 ? [NothingLeftOut] : lines).Append('\n');

        return text.ToString();
    }

    public static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static string NotOutlinedList(IReadOnlyList<NotOutlined> files, Detail detail) => detail switch
    {
        Detail.Full => string.Concat(files.Select(f => $"- {f.Path}{Measured(f)} — {f.Reason}\n")),
        Detail.Counts => string.Concat(files.GroupBy(f => f.Reason).Select(g => $"- {g.Key}: {g.Count()} file(s)\n"))
            + $"- ({files.Count} file(s) in all; too many to name inside the {FeatureBudget.OmissionsReserveBytes / 1024} KB this section is given — ask for the file list of a directory)\n",
        _ => string.Concat(files.GroupBy(f => f.Reason).Select(g => $"- {g.Key} ({g.Count()}): {Names([.. g.Select(f => f.Path)], detail)}\n")),
    };

    private static string Measured(NotOutlined file) =>
        (file.Bytes >= 0 ? $" — {file.Bytes} bytes" : string.Empty) + (file.Lines >= 0 ? $", {file.Lines} lines" : string.Empty);

    private static IEnumerable<string> LeftOutLines(FeatureOmissions omissions, Detail detail)
    {
        if (omissions.Collapsed.Count > 0)
        {
            yield return $"- Unchanged members collapsed in {omissions.Collapsed.Count} large file(s): "
                + (detail == Detail.Full ? string.Join(", ", omissions.Collapsed.Select(c => $"{c.Path} ({c.MembersElided})")) : Names([.. omissions.Collapsed.Select(c => c.Path)], detail));
        }

        if (omissions.Dropped.Count > 0)
        {
            yield return $"- Elided — ask for source by name ({omissions.Dropped.Count} file(s), smallest change first): {Names(omissions.Dropped, detail)}";
        }

        foreach (var line in CutLines(omissions.CutHunks, detail).Concat(omissions.Notes.Select(n => $"- {n}")))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> CutLines(IReadOnlyList<CutHunk> cut, Detail detail)
    {
        if (cut.Count == 0)
        {
            yield break;
        }

        var byFile = cut.GroupBy(c => c.Path).ToList();
        var prefix = $"- Member hunks cut for the outline budget ({cut.Count} member(s) in {byFile.Count} file(s), the largest changes cut first; each named by its outline line span — ask for source by symbol or span): ";
        yield return prefix + detail switch
        {
            Detail.Full => string.Join("; ", byFile.Select(g => $"{g.Key}: {string.Join(", ", g.Select(c => $"[{c.Start}-{c.End}] +{c.Added}/-{c.Deleted}"))}")),
            Detail.ByReason => string.Join("; ", byFile.Select(g => $"{g.Key} ({g.Count()})")),
            _ => Names([.. byFile.Select(g => g.Key)], detail),
        };
    }

    /// <summary>Paths as themselves, or folded into directories at the level's depth, with counts.</summary>
    private static string Names(IReadOnlyList<string> paths, Detail detail) => detail switch
    {
        Detail.Full or Detail.ByReason => string.Join(", ", paths),
        Detail.Counts => $"{paths.Count} path(s)",
        _ => string.Join(", ", paths.GroupBy(p => Folder(p, Depth(detail))).Select(g => g.Count() == 1 ? g.First() : $"{g.Key}/** ({g.Count()})")),
    };

    private static int Depth(Detail detail) => detail switch
    {
        Detail.ByDirectory3 => 3,
        Detail.ByDirectory2 => 2,
        _ => 1,
    };

    /// <summary>The first <paramref name="depth"/> directories of a path — the file's own name never counts.</summary>
    private static string Folder(string path, int depth)
    {
        var parts = path.Split('/');

        return parts.Length <= 1 ? path : string.Join('/', parts.Take(Math.Min(depth, parts.Length - 1)));
    }
}

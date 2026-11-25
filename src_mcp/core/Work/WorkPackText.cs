using System.Globalization;
using System.Text;

namespace CoaiMcp.Core.Work;

/// <summary>The words of a pack: the line Not sent, and one fenced section for each file. The fence is longer than any run of backticks in the file, so a file cannot end its own section.</summary>
internal static class WorkPackText
{
    private const int MinimumFence = 3;

    internal static string Compose(IReadOnlyList<WorkPackFiles.Section> sections, string notSent) =>
        notSent + "\n\n" + string.Join("\n", sections.Select(Section));

    private static string Section(WorkPackFiles.Section section)
    {
        var fence = new string('`', Math.Max(MinimumFence, LongestRun(section.Text) + 1));

        return $"## {section.Path}\n\n{fence}\n{section.Text}{(section.Text.EndsWith('\n') ? string.Empty : "\n")}{fence}\n";
    }

    /// <summary>The longest run of backticks in <paramref name="text"/>.</summary>
    private static int LongestRun(string text)
    {
        int longest = 0, run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    /// <summary>
    /// The line that says what the reviewers were NOT given: the architecture files of the feature and the free markdown files beside the nodes, each with its size, and how many nodes have no file in the pack. What is named here was not reviewed
    /// and is not part of the approval.
    /// </summary>
    internal static string NotSent(IListedWorkFolder folder, WorkFeature feature, WorkPackOrder.Layout layout, string featureFolder)
    {
        var sizes = folder.Files.ToDictionary(file => file.Path, file => file.Size, StringComparer.Ordinal);
        var packed = layout.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var architecture = feature.Architecture.Select(name => Sized(featureFolder, name, sizes)).ToList();
        var named = feature.Architecture.ToHashSet(StringComparer.Ordinal);
        var free = sizes.Keys.Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !packed.Contains(path) && !named.Contains(path)).OrderBy(path => path, StringComparer.Ordinal).Select(path => Sized(featureFolder, path, sizes)).ToList();
        var parts = new List<string>();
        AddGroup(parts, "architecture files", architecture);
        AddGroup(parts, "markdown files beside the nodes", free);
        parts.Add($"nodes outside the scope: {layout.NodesNotSent.ToString(CultureInfo.InvariantCulture)}");

        return $"**Not sent** (not reviewed, and not part of the approval): {string.Join("; ", parts)}.";
    }

    private static void AddGroup(List<string> parts, string name, List<string> items)
    {
        if (items.Count > 0)
        {
            parts.Add($"{name}: {string.Join(", ", items)}");
        }
    }

    private static string Sized(string featureFolder, string path, Dictionary<string, long> sizes) =>
        sizes.TryGetValue(path, out var size) ? $"{featureFolder}/{path} ({size.ToString(CultureInfo.InvariantCulture)} bytes)" : $"{featureFolder}/{path} (not in the commit)";
}

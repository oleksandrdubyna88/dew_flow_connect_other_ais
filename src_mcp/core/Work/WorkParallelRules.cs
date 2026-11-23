namespace CoaiMcp.Core.Work;

/// <summary>
/// Two tasks of one story may run at the same time only when nothing makes one wait for the other and they touch different files. For every pair of <c>parallel: true</c>
/// siblings: no dependency path between them (<c>parallel-dependent</c>) and no overlapping <c>files</c> (<c>parallel-overlap</c>: equal, or one a path prefix of the other,
/// case-insensitive). A parallel task must name its files and may not use a glob (<c>parallel-files</c>), because an overlap cannot be decided for a pattern.
/// </summary>
internal static class WorkParallelRules
{
    private static readonly char[] GlobCharacters = ['*', '?', '[', ']', '{', '}'];

    internal static void Check(WorkFeature feature, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, List<WorkError> errors)
    {
        foreach (var story in feature.AllStories)
        {
            var parallel = story.Tasks.Where(task => task.Loaded && task.Parallel).ToList();
            var decidable = parallel.Where(task => FilesAreDecidable(task, errors)).ToList();
            for (var i = 0; i < decidable.Count; i++)
            {
                for (var j = i + 1; j < decidable.Count; j++)
                {
                    Pair(decidable[i], decidable[j], edges, errors);
                }
            }
        }
    }

    private static bool FilesAreDecidable(WorkTask task, List<WorkError> errors)
    {
        if (task.Files.Count == 0)
        {
            errors.Add(new WorkError(WorkErrorCodes.ParallelFiles, task.Path, task.Node, "a parallel task must list its files; without them no overlap can be ruled out"));

            return false;
        }

        if (task.Files.FirstOrDefault(file => file.IndexOfAny(GlobCharacters) >= 0) is { } glob)
        {
            errors.Add(new WorkError(WorkErrorCodes.ParallelFiles, task.Path, task.Node, $"the files entry '{glob}' is a glob; a parallel task must name files or folders"));

            return false;
        }

        return true;
    }

    private static void Pair(WorkTask a, WorkTask b, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, List<WorkError> errors)
    {
        if (Reaches(a.Node, b.Node, edges) || Reaches(b.Node, a.Node, edges))
        {
            errors.Add(new WorkError(WorkErrorCodes.ParallelDependent, b.Path, b.Node, $"'{a.Folder}' and '{b.Folder}' are both parallel but one waits for the other, directly or through other tasks"));
        }

        if (Overlap(a.Files, b.Files) is { } shared)
        {
            errors.Add(new WorkError(WorkErrorCodes.ParallelOverlap, b.Path, b.Node, $"'{a.Folder}' and '{b.Folder}' are both parallel but their files overlap: {shared}"));
        }
    }

    private static bool Reaches(string from, string to, IReadOnlyDictionary<string, IReadOnlyList<string>> edges)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { from };
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            foreach (var next in edges.GetValueOrDefault(queue.Dequeue(), []))
            {
                if (next == to)
                {
                    return true;
                }

                if (seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return false;
    }

    private static string? Overlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        foreach (var left in a)
        {
            foreach (var right in b.Where(right => Contains(left, right) || Contains(right, left)))
            {
                return $"'{left}' and '{right}'";
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="outer"/> is <paramref name="inner"/> or a folder above it, compared whole path part by whole path part and without regard to case.</summary>
    private static bool Contains(string outer, string inner)
    {
        var a = Normal(outer);
        var b = Normal(inner);

        return a.Length > 0 && (b == a || b.StartsWith(a + "/", StringComparison.Ordinal));
    }

    private static string Normal(string path)
    {
        var text = path.Trim().Replace('\\', '/').ToLowerInvariant();
        while (text.StartsWith("./", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        return text.TrimEnd('/');
    }
}

namespace CoaiMcp.Runners.Worktrees;

/// <summary>One line of <c>git worktree list</c>: where, on what branch (empty when detached), and whether its folder is gone.</summary>
internal sealed record ListedWorktree(string Path, string Branch, bool Prunable);

/// <summary>
/// <c>git worktree list --porcelain -z</c> read as data. With <c>-z</c> every field ends in NUL and an entry ends in one more, so a path with a newline or
/// a quote is read exactly; without it git quotes such a path and the text would have to be un-quoted by hand.
/// </summary>
internal static class CoderWorktreeList
{
    private const string EntryEnd = "\0\0";

    internal static IReadOnlyList<ListedWorktree> Parse(string output) =>
        [.. output.Split(EntryEnd, StringSplitOptions.RemoveEmptyEntries).Select(Read).Where(static listed => listed.Path.Length > 0)];

    private static ListedWorktree Read(string entry)
    {
        var fields = entry.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        return new ListedWorktree(
            Value(fields, "worktree "),
            Value(fields, "branch "),
            fields.Any(static field => field == "prunable" || field.StartsWith("prunable ", StringComparison.Ordinal)));
    }

    private static string Value(string[] fields, string key) =>
        fields.Where(field => field.StartsWith(key, StringComparison.Ordinal)).Select(field => field[key.Length..]).FirstOrDefault() ?? string.Empty;
}

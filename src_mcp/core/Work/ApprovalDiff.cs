namespace CoaiMcp.Core.Work;

/// <summary>
/// The first thing that differs between two approvals of a plan folder (epoch 3 E3 s3 t4): the lines of what the reviewers read at the commit of the round and the lines of what a pack reads now. Pure. The comparison is by KEY — a file's path, a
/// dependency's node — so a file that is only moved in the order, because a node was added, is not a difference; what is named is the first approved line in pack order whose file changed or went, then the first thing the pack reads now that it
/// did not, files before dependencies.
/// </summary>
public static class ApprovalDiff
{
    /// <summary>The sentence for the first difference, or empty when the two list the same files with the same blobs and the same dependencies with the same titles.</summary>
    public static string First(IReadOnlyList<ApprovalLine> approved, IReadOnlyList<ApprovalLine> now)
    {
        var files = Compare(approved.Where(line => line.Kind == "F").ToList(), now.Where(line => line.Kind == "F").ToList(), File);

        return files.Length > 0 ? files : Compare(approved.Where(line => line.Kind == "D").ToList(), now.Where(line => line.Kind == "D").ToList(), Dependency);
    }

    private static string Compare(List<ApprovalLine> approved, List<ApprovalLine> now, Func<ApprovalLine?, ApprovalLine?, string> say)
    {
        var current = now.ToDictionary(line => line.Key, StringComparer.Ordinal);
        foreach (var line in approved)
        {
            if (!current.TryGetValue(line.Key, out var other) || other.Value != line.Value)
            {
                return say(line, current.GetValueOrDefault(line.Key));
            }
        }

        var before = approved.Select(line => line.Key).ToHashSet(StringComparer.Ordinal);

        return now.FirstOrDefault(line => !before.Contains(line.Key)) is { } added ? say(null, added) : string.Empty;
    }

    private static string File(ApprovalLine? approved, ApprovalLine? now) =>
        (approved, now) switch
        {
            (not null, null) => $"{approved.Key} was read by the reviewers and is not in the pack any more",
            (null, not null) => $"{now.Key} is read by the pack now, and was not part of what the reviewers read",
            _ => $"{approved!.Key} is not the revision the reviewers read (blob {Short(approved.Value)} then, {Short(now!.Value)} now)",
        };

    private static string Dependency(ApprovalLine? approved, ApprovalLine? now) =>
        (approved, now) switch
        {
            (not null, null) => $"the node {approved.Key}, which a node of the scope depended on, is not a dependency now (it was '{approved.Value}')",
            (null, not null) => $"the node {now.Key} is a dependency of the scope now ('{now.Value}'), and was not when the reviewers read it",
            _ => $"the node {approved!.Key}, which a node of the scope depends on, was '{approved.Value}' for the reviewers and is '{now!.Value}' now",
        };

    private static string Short(string id) => id[..Math.Min(12, id.Length)];
}

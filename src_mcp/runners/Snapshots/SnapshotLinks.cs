namespace CoaiMcp.Runners.Snapshots;

/// <summary>
/// The one walk that asks whether anything between a root and a file is a link — shared by the copier (just before
/// each copy), <see cref="ReviewSnapshot.Resolve"/> and <see cref="ReviewSnapshot.Read"/>.
/// </summary>
internal static class SnapshotLinks
{
    /// <summary>The first component from <paramref name="root"/> — the root itself included — down to <paramref name="relative"/> (forward slashes) that is a reparse point, or empty.</summary>
    /// <remarks>
    /// The root is asked first (finding D of the risk consultations): the walk checked every descendant and never the
    /// directory they hang from, so a snapshot root renamed aside and replaced by a junction served the junction's
    /// target under every manifest name.
    /// </remarks>
    public static string FirstLinkOnTheWay(string root, string relative)
    {
        if (IsLink(root))
        {
            return root;
        }

        var at = root;
        foreach (var part in relative.Split('/'))
        {
            at = Path.Combine(at, part);
            if (IsLink(at))
            {
                return at;
            }
        }

        return string.Empty;
    }

    /// <summary>Fails CLOSED: a component that cannot be inspected counts as a link, because it is not known to be anything else.</summary>
    private static bool IsLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone, or unreadable by this process: whatever it is, it is not a file this snapshot vouches for.
            return true;
        }
    }
}

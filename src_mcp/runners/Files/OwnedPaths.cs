namespace CoaiMcp.Runners.Files;

/// <summary>
/// The link test for a folder coai OWNS and is about to create in, write into or delete — a consultant's homes folder, a
/// conversation's folder, a snapshot's parent (R2 of the S3 risk consultation, todo/PLAN_vibe_and_kimi_runtimes.md).
/// </summary>
/// <remarks>
/// <para>Fails CLOSED like <c>SnapshotLinks.IsLink</c>, whose question it asks: a path that is there and cannot be
/// inspected counts as a link. Unlike it, a path that is NOT there is no link — a folder about to be made is not a
/// reason to refuse.</para>
/// <para><b>What it does not close.</b> It is a check at one moment, by path. A folder swapped for a link between this
/// check and the create, write or delete that follows is not seen — no deletion here is done by handle. It narrows the
/// race to that moment; it does not remove it.</para>
/// </remarks>
public static class OwnedPaths
{
    /// <summary>Whether <paramref name="path"/> is a reparse point, or is there and cannot be inspected; false when it is not there.</summary>
    public static bool IsLinkIfThere(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>The first of <paramref name="paths"/> that is a link (<see cref="IsLinkIfThere"/>), or empty.</summary>
    public static string FirstLink(params string[] paths) =>
        paths.FirstOrDefault(IsLinkIfThere) ?? string.Empty;
}

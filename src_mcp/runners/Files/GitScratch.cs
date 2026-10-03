namespace CoaiMcp.Runners.Files;

/// <summary>Removing a directory git has been in.</summary>
/// <remarks>
/// Extracted from <c>PanelService</c> when the consultant check (epic 4 of
/// PLAN_the_consultant_works_on_every_vendor.md) came to need the same thing for its scratch repositories —
/// a second copy of the read-only dance would have been the defect the reuse rule names.
/// </remarks>
public static class GitScratch
{
    /// <summary>Deletes <paramref name="dir"/> and everything in it, read-only files included.</summary>
    /// <remarks>
    /// <c>Directory.Delete(recursive: true)</c> refuses a READ-ONLY file with
    /// <c>UnauthorizedAccessException</c>, and git marks every object file read-only — so a scratch
    /// directory that ever held a clone could not be swept, the exception was caught, and the
    /// leftovers accumulated in silence. Measured 2026-09-05: 5,476 undeletable directories, almost
    /// all of them a test's clone, the oldest five days old.
    /// </remarks>
    public static void DeleteEvenIfReadOnly(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(dir, recursive: true);
    }
}

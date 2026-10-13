namespace CoaiMcp.Runners.Files;

/// <summary>Removing a directory git has been in.</summary>
/// <remarks>
/// Extracted from <c>PanelService</c> when the consultant check (epic 4 of
/// PLAN_the_consultant_works_on_every_vendor.md) came to need the same thing for its scratch repositories —
/// a second copy of the read-only dance would have been the defect the reuse rule names.
/// </remarks>
public static class GitScratch
{
    /// <summary>Deletes <paramref name="dir"/> and everything in it, read-only files included — and never anything a link in it points at.</summary>
    /// <remarks>
    /// <para><c>Directory.Delete(recursive: true)</c> refuses a READ-ONLY file with
    /// <c>UnauthorizedAccessException</c>, and git marks every object file read-only — so a scratch
    /// directory that ever held a clone could not be swept, the exception was caught, and the
    /// leftovers accumulated in silence. Measured 2026-09-05: 5,476 undeletable directories, almost
    /// all of them a test's clone, the oldest five days old.</para>
    /// <para><b>A link is removed by its own name and never entered</b> (D19 and D20 of
    /// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>, landed with the consultant home sweep). The walk was
    /// <c>EnumerateFiles(…, AllDirectories)</c>, which ENTERS a directory reparse point: on a tree holding a junction to
    /// somebody else's folder — a Kimi home's <c>credentials</c> is one by design — it cleared the read-only attribute of
    /// the target's files and then failed on the junction itself ("Access to the path 'credentials' is denied",
    /// <c>DeletingAnOwnedTreeNeverEntersALinkTests</c>, red first). The walk is by hand now: an entry that is a link — or
    /// cannot be inspected, which <c>SnapshotLinks.IsLink</c> counts as one — is unlinked in place, and only a real
    /// directory is descended.</para>
    /// </remarks>
    public static void DeleteEvenIfReadOnly(string dir)
    {
        if (Snapshots.SnapshotLinks.IsLink(dir))
        {
            Unlink(dir);

            return;
        }

        Writable(dir);
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            DeleteEntry(entry);
        }

        Directory.Delete(dir);
    }

    /// <summary>
    /// A real folder's own ReadOnly attribute cleared before its entries go — Vibe unpacks its plugin package with read-only
    /// FOLDERS (research/RESULTS_vibe_confinement.md §26), and a read-only folder refused its own deletion, so a round's
    /// <c>vh-*</c> home outlived every sweep. Never asked of a link, which is unlinked before this is reached.
    /// </summary>
    private static void Writable(string dir)
    {
        var attributes = File.GetAttributes(dir);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(dir, attributes & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>One entry of a real directory: a link unlinked, a directory descended, a file deleted whatever its attribute.</summary>
    private static void DeleteEntry(string entry)
    {
        if (Snapshots.SnapshotLinks.IsLink(entry))
        {
            Unlink(entry);
        }
        else if (Directory.Exists(entry))
        {
            DeleteEvenIfReadOnly(entry);
        }
        else
        {
            DeleteFile(entry);
        }
    }

    /// <summary>
    /// The link itself — a directory link by a NON-recursive delete, a file link as a file — told apart by the LINK's own
    /// attributes, so a link whose target is gone is removed too; its target is never read.
    /// </summary>
    private static void Unlink(string link)
    {
        if (File.GetAttributes(link).HasFlag(FileAttributes.Directory))
        {
            Directory.Delete(link, recursive: false);
        }
        else
        {
            File.Delete(link);
        }
    }

    private static void DeleteFile(string file)
    {
        var attributes = File.GetAttributes(file);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }

        File.Delete(file);
    }
}

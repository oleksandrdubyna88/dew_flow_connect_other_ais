namespace CoaiMcp.Runners.Files;

/// <summary>Removing a directory git has been in.</summary>
/// <remarks>
/// Extracted from <c>PanelService</c> when the consultant check (epic 4 of
/// PLAN_the_consultant_works_on_every_vendor.md) came to need the same thing for its scratch repositories —
/// a second copy of the read-only dance would have been the defect the reuse rule names.
/// </remarks>
public static class GitScratch
{
    /// <summary>TEST ONLY — null in the product: runs between a link's unlink and the second read of its target, so a test can make the loss happen in that window. Per async flow, so parallel tests do not see each other's.</summary>
    internal static Action<string>? AfterUnlink
    {
        get => _afterUnlink.Value;
        set => _afterUnlink.Value = value;
    }

    private static readonly AsyncLocal<Action<string>?> _afterUnlink = new();

    /// <summary>TEST ONLY — null in the product: runs after the pause and before the second listing of a target that seemed to lose a name, so a test can make a concurrent rename settle in that window.</summary>
    internal static Action<string>? BeforeRelist
    {
        get => _beforeRelist.Value;
        set => _beforeRelist.Value = value;
    }

    private static readonly AsyncLocal<Action<string>?> _beforeRelist = new();

    /// <summary>TEST ONLY — null in the product: told the path of every target the deleter is about to READ, so a test can prove which targets it never touches.</summary>
    internal static Action<string>? Reading
    {
        get => _reading.Value;
        set => _reading.Value = value;
    }

    private static readonly AsyncLocal<Action<string>?> _reading = new();

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
    /// directory is descended, and the target of every link is checked to have lost nothing, or the deletion stops with the names
    /// (<see cref="LinkTargetLostException"/>; E4.1 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>).</para>
    /// </remarks>
    public static void DeleteEvenIfReadOnly(string dir)
    {
        if (Snapshots.SnapshotLinks.IsLink(dir))
        {
            Unlink(dir);

            return;
        }

        Writable(dir);
        foreach (var entry in LinksFirst(Directory.EnumerateFileSystemEntries(dir)))
        {
            DeleteEntry(entry);
        }

        Directory.Delete(dir);
    }

    /// <summary>
    /// The links of a folder before anything else in it — D19's order ("the junction first"), so that a link whose target lost a
    /// file stops the deletion BEFORE the folder's own files are gone, not after the ones that sort ahead of it by name.
    /// </summary>
    private static IEnumerable<string> LinksFirst(IEnumerable<string> entries) =>
        entries.OrderByDescending(Snapshots.SnapshotLinks.IsLink);

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
    /// attributes, so a link whose target is gone is removed too. The target is READ (its entry names, through the link's own
    /// reparse data) before and after, never entered, and a name that is gone stops the deletion.
    /// </summary>
    private static void Unlink(string link)
    {
        var isDirectory = File.GetAttributes(link).HasFlag(FileAttributes.Directory);
        var target = Junctions.Target(link);
        var before = Look(target, isDirectory).Without(ItsOwnNameInItsParent(link, target));

        if (isDirectory)
        {
            Directory.Delete(link, recursive: false);
        }
        else
        {
            File.Delete(link);
        }

        AfterUnlink?.Invoke(link);
        Verify(link, target, before, isDirectory);
    }

    /// <summary>
    /// What the folder a link points at held — for a file link, whether the file is there — as the entry names it could be
    /// listed by. <see cref="Held.Known"/> is false when there was nothing to read (a dangling link, an unreadable folder):
    /// the unlink by name was always safe, and nothing is said. A target that is not on a local drive (a share, a device path, a
    /// relative name, a mapped network drive) is NEVER read: reading one connects out — Windows sends NTLM — or waits out the SMB
    /// timeout, on every sweep, from a link anybody could plant in a scratch folder.
    /// </summary>
    internal static Held Look(string target, bool isDirectory) =>
        !Junctions.IsLocal(target) ? Held.Unread : Readable(target, isDirectory ? () => Listed(target) : () => OneFile(target));

    private static Held Readable(string target, Func<Held> read)
    {
        try
        {
            Reading?.Invoke(target);

            return read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Held.Unread;
        }
    }

    private static Held Listed(string folder) =>
        new(true, [.. Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>()]);

    private static Held OneFile(string file) => File.Exists(file) ? new Held(true, [Path.GetFileName(file)]) : Held.Unread;

    private static void Verify(string link, string target, Held before, bool isDirectory)
    {
        if (StillThere(link))
        {
            throw new LinkTargetLostException($"{link} still exists after its unlink — nothing more was deleted");
        }

        var missing = StillMissing(target, before, isDirectory);
        if (missing.Count > 0)
        {
            throw new LinkTargetLostException(
                $"the folder {target} that {link} pointed at changed while the link was removed: {string.Join(", ", missing)} no longer there — nothing more was deleted; check that folder before deleting {link}'s tree again");
        }
    }

    /// <summary>
    /// The names missing from the target — and, when any seem to be, asked AGAIN after a short pause and only the ones still
    /// missing reported: a Kimi refresh rotates a credentials file by temp file and rename, so a name can be absent for a moment
    /// that is not a loss.
    /// </summary>
    private static List<string> StillMissing(string target, Held before, bool isDirectory)
    {
        var missing = Missing(before, Look(target, isDirectory));
        if (missing.Count == 0)
        {
            return missing;
        }

        Thread.Sleep(SettlePause);
        BeforeRelist?.Invoke(target);

        return Missing(before, Look(target, isDirectory));
    }

    private static readonly TimeSpan SettlePause = TimeSpan.FromMilliseconds(200);

    /// <summary>A link that points at its own parent folder lists itself there; its own name is the unlink, not a loss.</summary>
    private static string ItsOwnNameInItsParent(string link, string target) =>
        Junctions.SameFolder(target, Path.GetDirectoryName(Path.GetFullPath(link)) ?? string.Empty) ? Path.GetFileName(link) : string.Empty;

    private static bool StillThere(string link) => OwnedPaths.IsLinkIfThere(link) || Directory.Exists(link);

    /// <summary>The names that were there before and are not now — none when nothing could be read before.</summary>
    private static List<string> Missing(Held before, Held after) =>
        before.Known ? [.. before.Names.Except(after.Names, StringComparer.Ordinal)] : [];

    /// <summary>What a link's target held at one moment.</summary>
    internal sealed record Held(bool Known, IReadOnlyList<string> Names)
    {
        public static readonly Held Unread = new(false, []);

        public Held Without(string name) => this with { Names = [.. Names.Where(known => !string.Equals(known, name, StringComparison.Ordinal))] };
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

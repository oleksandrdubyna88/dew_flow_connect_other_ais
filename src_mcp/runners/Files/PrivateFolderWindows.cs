namespace CoaiMcp.Runners.Files;

/// <summary>What opening a folder's handle came to: a held handle, or the Win32 error that says why not.</summary>
internal abstract record ShellOpen
{
    private ShellOpen()
    {
    }

    /// <summary>The handle, to be disposed to let go.</summary>
    public sealed record Held(IDisposable Handle) : ShellOpen;

    /// <summary>The open failed with this Win32 error.</summary>
    public sealed record Failed(int Error) : ShellOpen;
}

/// <summary>
/// Everything <see cref="PrivateFolderWindows"/> needs from the operating system, and nothing else: bytes in, bytes out. The one
/// real implementation is <c>PrivateFolderWindowsNative</c> (P/Invoke and handles, run only on Windows, where the
/// <c>windows-latest</c> job runs <c>PrivateFolderWindowsTests</c>); the Linux suite drives the same decisions through a fake.
/// </summary>
internal interface IFolderShell
{
    /// <summary>The person's SID.</summary>
    Sid Me { get; }

    /// <summary>Creates <paramref name="path"/> atomically WITH this protected DACL.</summary>
    void Create(string path, AclFacts wanted);

    /// <summary>
    /// Opens the folder ONCE, for reading its security (and for replacing the DACL when the person may), sharing read and write
    /// but never delete — so the folder cannot be renamed or deleted while the handle is held.
    /// </summary>
    ShellOpen Open(string path);

    /// <summary>The final path the kernel holds for an open handle (a link shows as a different path).</summary>
    string FinalPathOf(IDisposable handle);

    /// <summary>The owner and DACL of the open handle; <see cref="IOException"/> when unreadable.</summary>
    AclFacts Read(IDisposable handle);

    /// <summary>Replaces the DACL through the handle with <paramref name="wanted"/>'s, protected; whether the system took it.</summary>
    bool Replace(IDisposable handle, AclFacts wanted);

    /// <summary>A name for a SID (<c>NT AUTHORITY\SYSTEM</c>), or its text when none maps; <c>unknown</c> for none.</summary>
    string Name(Sid who);

    /// <summary>The owner and DACL of a descendant, by path; an IO or access exception when it cannot be read.</summary>
    AclFacts AclOf(string entry, bool isFolder);

    /// <summary>Resets a descendant to inherit from its parent: protection off, its explicit entries removed.</summary>
    void ResetToInherit(string entry, bool isFolder);
}

/// <summary>
/// The Windows half of <see cref="PrivateFolder"/>, as decisions over an <see cref="IFolderShell"/>. A missing folder is created
/// WITH a protected DACL of the person, SYSTEM and Administrators; because <c>CreateDirectory</c> returns silently on an existing
/// one, every folder is then verified BY HANDLE — final path (a link shows as a different one), owner, DACL — and a folder of
/// the person's with a wrong DACL is re-ACLed through that same handle, its descendants walked and reset to inherit.
/// </summary>
internal static class PrivateFolderWindows
{
    /// <summary>The folder asked for: the root and name it was asked by, the full path, and where the one line about a re-ACL goes.</summary>
    internal sealed record Target(string Root, string Name, string Path, Action<string> Note);

    public static PrivateFolderOutcome Open(IFolderShell shell, Target target)
    {
        if (!Directory.Exists(target.Path))
        {
            shell.Create(target.Path, PrivateAcl.Wanted(shell.Me));
        }

        // Verified every time: the create above returns silently on an existing folder, so it proved nothing.
        var opened = shell.Open(target.Path);
        if (opened is ShellOpen.Failed failed)
        {
            return new PrivateFolderOutcome.Refused(PrivateAcl.NotOpened(target.Path, failed.Error, OwnedPaths.IsLinkIfThere(target.Path)));
        }

        var handle = ((ShellOpen.Held)opened).Handle;

        return Settled(handle, Checked(shell, handle, target), target.Path);
    }

    /// <summary>The verdict for the held handle: kept open on a pass, closed on a refusal or a failure to read.</summary>
    private static PrivateFolderOutcome Checked(IFolderShell shell, IDisposable handle, Target target)
    {
        try
        {
            return Verdict(shell, handle, target);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static PrivateFolderOutcome Settled(IDisposable handle, PrivateFolderOutcome verdict, string path)
    {
        if (verdict is PrivateFolderOutcome.Opened)
        {
            return new PrivateFolderOutcome.Opened(handle, path);
        }

        handle.Dispose();

        return verdict;
    }

    private static PrivateFolderOutcome Verdict(IFolderShell shell, IDisposable handle, Target target)
    {
        var expected = Path.Combine(HandleRead.CanonicalDirectory(target.Root), target.Name);
        if (!HandleRead.SameByComponents(shell.FinalPathOf(handle), expected, StringComparison.OrdinalIgnoreCase))
        {
            return new PrivateFolderOutcome.Refused(PrivateAcl.NotTheFolder(target.Path));
        }

        var facts = shell.Read(handle);

        return !PrivateAcl.OwnerIsOurs(facts, shell.Me) ? new PrivateFolderOutcome.Refused(PrivateAcl.ForeignOwner(target.Path, shell.Name(facts.Owner), PrivateAcl.Exposure(facts, shell.Me, shell.Name)))
            : PrivateAcl.IsExactlyOurs(facts, shell.Me) ? new PrivateFolderOutcome.Opened(new PrivateFolder.NothingHeld(), target.Path)
            : ReAcled(shell, handle, target, facts);
    }

    /// <summary>The folder is the person's but its DACL is not ours: made so through the handle, re-read, and said once.</summary>
    private static PrivateFolderOutcome ReAcled(IFolderShell shell, IDisposable handle, Target target, AclFacts before)
    {
        var exposure = PrivateAcl.Exposure(before, shell.Me, shell.Name);
        if (!shell.Replace(handle, PrivateAcl.Wanted(shell.Me)) || !PrivateAcl.IsExactlyOurs(shell.Read(handle), shell.Me))
        {
            return new PrivateFolderOutcome.Refused(PrivateAcl.NotMadePrivate(target.Path, exposure));
        }

        if (MadePrivateBelow(shell, target.Path) is { Length: > 0 } child)
        {
            return new PrivateFolderOutcome.Refused(child);
        }

        target.Note(PrivateAcl.MadePrivate(target.Path, exposure));

        return new PrivateFolderOutcome.Opened(new PrivateFolder.NothingHeld(), target.Path);
    }

    // -------------------------------------------------------------------------------------------------------------
    // The descendants of a folder that was re-ACLed
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Propagation from the parent replaces the INHERITED entries of the children only: an explicit entry and a protected DACL
    /// stay, so a transcript could stay readable under a parent that now says it is private. Every descendant is therefore walked
    /// (a link is never entered and never touched — propagation does not follow one either) and, when it holds an explicit entry
    /// or is protected, reset to inherit from its parent and re-read. The sentence names the first child that cannot be.
    /// </summary>
    internal static string MadePrivateBelow(IFolderShell shell, string folder)
    {
        var pending = new Stack<string>([folder]);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var problem = SettledEntry(shell, entry, pending);
                if (problem.Length > 0)
                {
                    return problem;
                }
            }
        }

        return string.Empty;
    }

    private static string SettledEntry(IFolderShell shell, string entry, Stack<string> pending)
    {
        var attributes = File.GetAttributes(entry);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return string.Empty;
        }

        var isFolder = (attributes & FileAttributes.Directory) != 0;
        if (isFolder)
        {
            pending.Push(entry);
        }

        return InheritsExactly(shell, entry, isFolder);
    }

    private static string InheritsExactly(IFolderShell shell, string entry, bool isFolder)
    {
        try
        {
            var before = shell.AclOf(entry, isFolder);

            return PrivateAcl.OwnerProblem(entry, before, shell.Me, shell.Name) is { Length: > 0 } foreign ? foreign
                : PrivateAcl.NeedsReset(before) ? ResetAndChecked(shell, entry, isFolder)
                : string.Empty;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return PrivateAcl.ChildCouldNotBeMade(entry, e.Message);
        }
    }

    private static string ResetAndChecked(IFolderShell shell, string entry, bool isFolder)
    {
        shell.ResetToInherit(entry, isFolder);

        return PrivateAcl.Inherited(shell.AclOf(entry, isFolder), shell.Me)
            ? string.Empty
            : PrivateAcl.ChildCouldNotBeMade(entry, "it did not inherit the three entries after a reset");
    }
}

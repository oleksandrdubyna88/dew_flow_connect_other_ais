using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Files;

/// <summary>What opening a private folder came to — a closed union, so a refusal is never an exception and never a pass.</summary>
public abstract record PrivateFolderOutcome
{
    private PrivateFolderOutcome()
    {
    }

    /// <summary>The folder is private to the person and its handle is <paramref name="Held"/> (dispose it to let go).</summary>
    public sealed record Opened(IDisposable Held, string Path) : PrivateFolderOutcome;

    /// <summary>It is not, or could not be made or read so; the sentence says why and what cures it.</summary>
    public sealed record Refused(string Sentence) : PrivateFolderOutcome;
}

/// <summary>
/// The ONE entry into <c>&lt;homes root&gt;\coai-*</c> (D7 of <c>research/PLAN_vibe_live_round_fixes.md</c>, as the security review of
/// 2026-10-15 left it): coai's folders under the homes root hold session transcripts of the reviewed code and its copies of the
/// person's tree, so they must be a folder only the person can open — and the root may be one the person picked (a drive's
/// root inherits <c>Authenticated Users: Modify</c>).
/// </summary>
/// <remarks>
/// <para><b>The root</b> must be absolute, not a UNC or <c>\\?\</c> path, and must EXIST — coai never creates it. The root's own
/// DACL and everything above it belong to the person and are not judged.</para>
/// <para><b>Windows.</b> A missing folder is created atomically WITH a protected DACL of the person, SYSTEM and Administrators
/// (SIDs, never names; the owner is not set). <c>CreateDirectory</c> returns silently on an existing folder without applying
/// the DACL, so creating proves nothing: every time, the folder is then verified BY HANDLE — opened (a link is followed, so a
/// link shows as a different final path), its final path must equal <c>&lt;canonical root&gt;\&lt;name&gt;</c> component by
/// component, its OWNER must be the person or Administrators (an elevated session owns what it makes as Administrators), and its
/// DACL must be exactly the three Allow entries. A folder of the person's whose DACL is wrong is RE-ACLed through that handle
/// (which migrates a 0.53/0.54 <c>coai-vibe</c> tree, children included) and one line says who could read or modify it until
/// now; a folder whose owner is another is refused. A failed open or read is a refusal, never a pass.</para>
/// <para><b>One handle, verified and held.</b> The folder is opened ONCE — read-control, attributes, list-directory (the kernel
/// records a share mode only for a handle that holds a data right) and, when the person may, WRITE_DAC — sharing read and write but
/// never delete. Its final path, owner and DACL are checked through that handle, the re-ACL is applied through it, and the same handle
/// is what the reservation holds, so nobody can rename or delete the folder while it is held (<c>PrivateFolderWindowsTests.E7</c>,
/// <c>E10</c>) and a folder somebody already holds open for delete cannot be opened at all.</para>
/// <para><b>The children of a re-ACLed folder.</b> Propagation replaces only INHERITED entries below; an explicit entry or a
/// protected DACL stays. So after a re-ACL every descendant is walked (a link is never entered or touched) and reset to inherit,
/// and a child that cannot be is refused by name. The walk runs only when the parent itself needed the re-ACL — a parent that is
/// already private is not walked on later opens (a stated limit: whoever could have planted a wide child there could not have).</para>
/// <para><b>Unix.</b> Created <c>0700</c>; an existing one must not be a symlink. One with a group or other bit is MIGRATED as on
/// Windows: chmod 0700 (which only its owner or root can do — success is the owner check), the mode read back and required clean, one line
/// logged; a chmod that fails refuses it, naming the mode. No walk of the children: a 0700 parent stops anyone else traversing it.</para>
/// <para><b>Limits, stated.</b> A local administrator and SYSTEM are outside the threat model. Re-ACLing does not undo contents
/// planted while the folder was wide — the per-launch home check judges those. A child whose OWNER is not the person or Administrators
/// refuses the folder by name (the walk checks each child's owner), because an owner holds an implicit WRITE_DAC and could widen the
/// DACL again right after it was reset. A
/// <c>coai-*</c> folder another user made first on a shared, root-of-drive root cannot be deleted by the person: the cure is another
/// folder.</para>
/// <para><b>Where the Windows half lives.</b> This file is the door and the Unix leg. The Windows rules are pure functions of plain
/// DACL facts in <c>PrivateAcl</c>; the flow (create, open one handle, verify, re-ACL, walk the children) is
/// <c>PrivateFolderWindows</c> over an <c>IFolderShell</c>; and <c>PrivateFolderWindowsNative</c> is the shell — P/Invoke and handles,
/// bytes in and out. All but the shell run in the Linux suite, which is where SonarCloud measures coverage.</para>
/// </remarks>
public static class PrivateFolder
{
    /// <summary>How the person's setting is named in every refusal sentence.</summary>
    internal const string Setting = "ConnectOtherAIs (COAI_CONSULTANT_HOMES)";

    /// <summary><c>&lt;root&gt;\&lt;name&gt;</c> — the one place a folder under the homes root is spelled; pure.</summary>
    public static string PathOf(string root, string name) => Path.Combine(root, name);

    /// <summary>The folder <paramref name="name"/> under <paramref name="root"/>, made or verified private and held — or why not.</summary>
    /// <param name="note">Where the one line about a re-ACLed folder goes.</param>
    public static PrivateFolderOutcome Open(string root, string name, Action<string>? note = null)
    {
        if (RootRefusal(root) is { Length: > 0 } why)
        {
            return new PrivateFolderOutcome.Refused(why);
        }

        try
        {
            var target = new PrivateFolderWindows.Target(root, name, PathOf(root, name), note ?? (static _ => { }));

            return OperatingSystem.IsWindows() ? PrivateFolderWindowsNative.Open(target) : Unix.Open(target.Path, target.Note);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new PrivateFolderOutcome.Refused($"{PathOf(root, name)} could not be made or read private ({e.Message}) — choose another Vibe homes folder in {Setting}");
        }
    }

    /// <summary>Whether <paramref name="root"/> is spelled as a full local path — the question before "does it exist" has any meaning.</summary>
    public static bool IsFullLocalPath(string root) =>
        Path.IsPathFullyQualified(root) && !root.Replace('/', '\\').StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>Why <paramref name="root"/> cannot hold coai's folders — not a full local path, or not there — or empty.</summary>
    public static string RootRefusal(string root)
    {
        if (!IsFullLocalPath(root))
        {
            return $"the Vibe homes folder {root} must be a full local path, not a network share or a \\\\?\\ path — name a short local folder of your own in {Setting}";
        }

        return Directory.Exists(root)
            ? string.Empty
            : $"the Vibe homes folder {root} does not exist — create a short local folder of your own and name it in {Setting}";
    }

    /// <summary>What a folder that needs no handle is held by — nothing; its <see cref="IDisposable.Dispose"/> does nothing.</summary>
    internal sealed class NothingHeld : IDisposable
    {
        public void Dispose()
        {
        }
    }

    /// <summary>The Unix judgement over a mode and what chmod does, as a pure function (the real chmod is handed in).</summary>
    internal static PrivateFolderOutcome UnixVerdict(string path, UnixFileMode mode, Func<UnixFileMode> makePrivate, Action<string> note) =>
        Unix.Judged(path, mode, makePrivate, note);

    private static class Unix
    {
        private const UnixFileMode Wider = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        [UnsupportedOSPlatform("windows")]
        public static PrivateFolderOutcome Open(string path, Action<string> note)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return Verdict(path, note);
        }

        [UnsupportedOSPlatform("windows")]
        private static PrivateFolderOutcome Verdict(string path, Action<string> note)
        {
            if (new DirectoryInfo(path).LinkTarget is not null)
            {
                return new PrivateFolderOutcome.Refused($"{path} is a link, and Vibe's homes are never made through one — delete the link, or choose another Vibe homes folder in {Setting}");
            }

            return Judged(path, File.GetUnixFileMode(path), () => MadePrivate(path), note);
        }

        /// <summary>chmod 0700 and read the mode back — what a wide folder of the person's own is migrated with.</summary>
        [UnsupportedOSPlatform("windows")]
        private static UnixFileMode MadePrivate(string path)
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            return File.GetUnixFileMode(path);
        }

        /// <summary>
        /// A folder with no group or other bit is the person's. A wider one is MIGRATED, as the same case is on Windows (a folder of the
        /// person's with a wrong DACL is re-ACLed): chmod 0700 succeeds only for its owner or root, so its success IS the owner check;
        /// the mode is read back and must be clean; one line says what it was. A chmod that fails (EPERM — somebody else's folder) or
        /// leaves bits behind refuses the folder, naming the mode. There is no walk of the children, unlike Windows: a 0700 parent
        /// stops anyone else traversing it, and Unix has no bypass-traverse privilege for a normal user.
        /// </summary>
        internal static PrivateFolderOutcome Judged(string path, UnixFileMode mode, Func<UnixFileMode> makePrivate, Action<string> note)
        {
            if ((mode & Wider) == 0)
            {
                return new PrivateFolderOutcome.Opened(new NothingHeld(), path);
            }

            try
            {
                if ((makePrivate() & Wider) == 0)
                {
                    note($"made {path} private; mode {Convert.ToString((int)mode, 8)} until now");

                    return new PrivateFolderOutcome.Opened(new NothingHeld(), path);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // Not the owner: refused below, with the mode it has.
            }

            return new PrivateFolderOutcome.Refused($"{path} is not private to you (mode {Convert.ToString((int)mode, 8)}) — coai keeps Vibe's homes, their transcripts and its copies of your tree only in a folder only you can open; "
                + $"delete it, or choose another Vibe homes folder in {Setting}");
        }
    }
}

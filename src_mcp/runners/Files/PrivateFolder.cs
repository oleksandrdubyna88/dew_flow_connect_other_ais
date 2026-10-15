using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

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
/// The ONE entry into <c>&lt;homes root&gt;\coai-*</c> (D7 of <c>todo/PLAN_vibe_live_round_fixes.md</c>, as the security review of
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
/// <para><b>Unix.</b> Created <c>0700</c>; an existing one must not be a symlink and must have no group or other bits, else
/// it is refused naming its mode. The owner uid is not checked.</para>
/// <para><b>Limits, stated.</b> A local administrator and SYSTEM are outside the threat model. Re-ACLing does not undo contents
/// planted while the folder was wide — the per-launch home check judges those — and a child somebody else planted keeps its OWNER,
/// and an owner holds an implicit WRITE_DAC, so it can widen that child again (the walk resets its DACL, not its owner). A
/// <c>coai-*</c> folder another user made first on a shared, root-of-drive root cannot be deleted by the person: the cure is another
/// folder.</para>
/// </remarks>
public static partial class PrivateFolder
{
    private const string Setting = "ConnectOtherAIs (COAI_CONSULTANT_HOMES)";

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
            return OperatingSystem.IsWindows()
                ? Windows.Open(root, name, note ?? (static _ => { }))
                : Unix.Open(PathOf(root, name));
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

    private sealed class NothingHeld : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static class Unix
    {
        private const UnixFileMode Wider = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        [UnsupportedOSPlatform("windows")]
        public static PrivateFolderOutcome Open(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return Verdict(path);
        }

        [UnsupportedOSPlatform("windows")]
        private static PrivateFolderOutcome Verdict(string path)
        {
            if (new DirectoryInfo(path).LinkTarget is not null)
            {
                return new PrivateFolderOutcome.Refused($"{path} is a link, and Vibe's homes are never made through one — delete the link, or choose another Vibe homes folder in {Setting}");
            }

            var mode = File.GetUnixFileMode(path);

            return (mode & Wider) == 0
                ? new PrivateFolderOutcome.Opened(new NothingHeld(), path)
                : new PrivateFolderOutcome.Refused($"{path} is not private to you (mode {Convert.ToString((int)mode, 8)}) — coai keeps Vibe's homes, their transcripts and its copies of your tree only in a folder only you can open; "
                    + $"delete it, or choose another Vibe homes folder in {Setting}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static partial class Windows
    {
        private const uint ReadControl = 0x20000;
        private const uint WriteDac = 0x40000;
        private const uint FileReadAttributes = 0x80;
        private const uint FileListDirectory = 0x1;
        private const uint ShareReadWrite = 0x1 | 0x2;
        private const uint OpenExisting = 3;
        private const int SharingViolation = 32;
        private const uint BackupSemantics = 0x02000000;
        private const int FileObject = 1;
        private const uint OwnerAndDacl = 0x1 | 0x4;
        private const uint ReplaceDacl = 0x4 | 0x80000000;

        public static PrivateFolderOutcome Open(string root, string name, Action<string> note)
        {
            var path = PathOf(root, name);
            var me = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("the current user has no SID");
            if (!Directory.Exists(path))
            {
                new DirectoryInfo(path).Create(Ours(me));
            }

            // Verified every time: the create above returns silently on an existing folder, so it proved nothing.
            var handle = OpenHeld(path);
            if (handle.IsInvalid)
            {
                return new PrivateFolderOutcome.Refused(NotOpened(path, Marshal.GetLastWin32Error()));
            }

            return Settled(handle, Checked(handle, root, name, path, me, note), path);
        }

        /// <summary>
        /// The ONE handle: verified, re-ACLed through, and then HELD — so what was checked is what is kept. It asks for a data right
        /// (the kernel records a share mode only for a handle that holds one — E7) and WRITE_DAC to re-ACL, without it when the
        /// person may not have that; it shares read and write but never delete, so a rename or a delete of the folder is refused
        /// while it is open, and a folder somebody else already holds open for delete cannot be opened at all.
        /// </summary>
        private static SafeFileHandle OpenHeld(string path)
        {
            var handle = OpenHandle(path, ReadControl | FileReadAttributes | FileListDirectory | WriteDac);

            return handle.IsInvalid ? OpenHandle(path, ReadControl | FileReadAttributes | FileListDirectory) : handle;
        }

        private static string NotOpened(string path, int error) => OwnedPaths.IsLinkIfThere(path)
            ? $"{path} is a link, and its target could not be opened (error {error}) — Vibe's homes are never made through one; delete it, or choose another Vibe homes folder in {Setting}"
            : NotOpenedHere(path, error);

        private static string NotOpenedHere(string path, int error) => error == SharingViolation
            ? $"{path} is open elsewhere in a way that lets it be renamed or deleted under coai, so it cannot be held — close what holds it, or choose another Vibe homes folder in {Setting}"
            : $"{path} could not be opened to check that it is private (error {error}) — choose another Vibe homes folder in {Setting}";

        /// <summary>The verdict for the held handle: kept open on a pass, closed on a refusal or a failure to read.</summary>
        private static PrivateFolderOutcome Checked(SafeFileHandle handle, string root, string name, string path, SecurityIdentifier me, Action<string> note)
        {
            try
            {
                return Verdict(handle, root, name, path, me, note);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static PrivateFolderOutcome Settled(SafeFileHandle handle, PrivateFolderOutcome verdict, string path)
        {
            if (verdict is PrivateFolderOutcome.Opened)
            {
                return new PrivateFolderOutcome.Opened(handle, path);
            }

            handle.Dispose();

            return verdict;
        }

        private static SafeFileHandle OpenHandle(string path, uint access) =>
            CreateFileW(path, access, ShareReadWrite, 0, OpenExisting, BackupSemantics, 0);

        private static PrivateFolderOutcome Verdict(SafeFileHandle handle, string root, string name, string path, SecurityIdentifier me, Action<string> note)
        {
            var expected = Path.Combine(HandleRead.CanonicalDirectory(root), name);
            if (!HandleRead.SameByComponents(HandleRead.FinalPathOf(handle), expected, StringComparison.OrdinalIgnoreCase))
            {
                return new PrivateFolderOutcome.Refused($"{path} is not the folder it names — it is a link, or it was swapped — and coai does not use or make folders through one; "
                    + $"delete it, or choose another Vibe homes folder in {Setting}");
            }

            var security = Read(handle);

            return !OwnerIsOurs(security, me) ? Refuse(path, security)
                : IsExactlyOurs(security, me) ? new PrivateFolderOutcome.Opened(new NothingHeld(), path)
                : ReAcled(handle, path, security, me, note);
        }

        /// <summary>The owner may be the person or Administrators — an elevated session creates folders owned by the group.</summary>
        private static bool OwnerIsOurs(DirectorySecurity security, SecurityIdentifier me)
        {
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

            return owner is not null && (owner == me || owner == Admins);
        }

        private static SecurityIdentifier System => new(WellKnownSidType.LocalSystemSid, null);

        private static SecurityIdentifier Admins => new(WellKnownSidType.BuiltinAdministratorsSid, null);

        /// <summary>The three entries, and nothing else: SIDs, full control, container- and object-inheriting, protected.</summary>
        private static DirectorySecurity Ours(SecurityIdentifier me)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { me, System, Admins })
            {
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            }

            return security;
        }

        private static bool IsExactlyOurs(DirectorySecurity security, SecurityIdentifier me)
        {
            var rules = Rules(security);

            return security.AreAccessRulesProtected
                && rules.Count == 3
                && rules.All(IsFullControlAllow)
                && rules.Select(rule => rule.IdentityReference.Value).Order().SequenceEqual(new[] { me, System, Admins }.Select(sid => sid.Value).Order());
        }

        private static bool IsFullControlAllow(FileSystemAccessRule rule) =>
            rule.AccessControlType == AccessControlType.Allow && !rule.IsInherited
            && rule.FileSystemRights == FileSystemRights.FullControl
            && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
            && rule.PropagationFlags == PropagationFlags.None;

        private static List<FileSystemAccessRule> Rules(FileSystemSecurity security) =>
            [.. security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()];

        /// <summary>The folder is the person's but its DACL is not ours: made so through the handle, re-read, and said once.</summary>
        private static PrivateFolderOutcome ReAcled(SafeFileHandle handle, string path, DirectorySecurity before, SecurityIdentifier me, Action<string> note)
        {
            if (!Replaced(handle, Ours(me)) || !IsExactlyOurs(Read(handle), me))
            {
                return new PrivateFolderOutcome.Refused($"{path} is not private to you and coai could not make it so — {Exposure(before)}; "
                    + $"delete it, or choose another Vibe homes folder in {Setting}");
            }

            if (MadePrivateBelow(path, me) is { Length: > 0 } child)
            {
                return new PrivateFolderOutcome.Refused(child);
            }

            note($"made {path} private; {Exposure(before)} until now");

            return new PrivateFolderOutcome.Opened(new NothingHeld(), path);
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
        private static string MadePrivateBelow(string folder, SecurityIdentifier me)
        {
            var pending = new Stack<string>([folder]);
            while (pending.Count > 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    var problem = Settled(entry, me, pending);
                    if (problem.Length > 0)
                    {
                        return problem;
                    }
                }
            }

            return string.Empty;
        }

        private static string Settled(string entry, SecurityIdentifier me, Stack<string> pending)
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return string.Empty;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                pending.Push(entry);
            }

            return InheritsExactly(entry, (attributes & FileAttributes.Directory) != 0, me);
        }

        private static string InheritsExactly(string entry, bool isFolder, SecurityIdentifier me)
        {
            try
            {
                if (!NeedsReset(SecurityOf(entry, isFolder)))
                {
                    return string.Empty;
                }

                Reset(entry, isFolder);

                return Inherited(SecurityOf(entry, isFolder), me) ? string.Empty : CouldNotBeMade(entry, "it did not inherit the three entries after a reset");
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                return CouldNotBeMade(entry, e.Message);
            }
        }

        private static string CouldNotBeMade(string entry, string why) =>
            $"{entry} inside the re-ACLed folder could not be made private ({why}) — delete it, or choose another Vibe homes folder in {Setting}";

        private static FileSystemSecurity SecurityOf(string entry, bool isFolder) =>
            isFolder ? new DirectoryInfo(entry).GetAccessControl() : new FileInfo(entry).GetAccessControl();

        private static bool NeedsReset(FileSystemSecurity security) =>
            security.AreAccessRulesProtected || security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Count > 0;

        private static void Reset(string entry, bool isFolder)
        {
            var security = SecurityOf(entry, isFolder);
            security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
            foreach (FileSystemAccessRule explicitRule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleSpecific(explicitRule);
            }

            if (isFolder)
            {
                new DirectoryInfo(entry).SetAccessControl((DirectorySecurity)security);
            }
            else
            {
                new FileInfo(entry).SetAccessControl((FileSecurity)security);
            }
        }

        /// <summary>Not protected, no explicit entry, and exactly the person, SYSTEM and Administrators — all inherited.</summary>
        private static bool Inherited(FileSystemSecurity security, SecurityIdentifier me)
        {
            var rules = Rules(security);

            return !security.AreAccessRulesProtected
                && rules.All(rule => rule.IsInherited)
                && rules.Select(rule => rule.IdentityReference.Value).Distinct().Order().SequenceEqual(new[] { me, System, Admins }.Select(sid => sid.Value).Order());
        }

        private static PrivateFolderOutcome Refuse(string path, DirectorySecurity security) =>
            new PrivateFolderOutcome.Refused($"{path} is not private to you (owner {Name(security.GetOwner(typeof(SecurityIdentifier)))}; {Exposure(security)}) — "
                + "coai keeps Vibe's homes, their transcripts and its copies of your tree only in a folder only you can open; "
                + $"delete it, or choose another Vibe homes folder in {Setting}");

        /// <summary>Who — beyond the three — could do what: <c>Everyone could read it</c>; a sentence of its own when no foreign Allow entry exists.</summary>
        private static string Exposure(DirectorySecurity security)
        {
            var mine = new[] { WindowsIdentity.GetCurrent().User, System, Admins }.Select(sid => sid?.Value).ToHashSet();
            var others = Rules(security).Where(rule => rule.AccessControlType == AccessControlType.Allow && !mine.Contains(rule.IdentityReference.Value)).ToList();

            return others.Count == 0
                ? "nobody else is listed as able to use it, but its protection or entries were not exactly the person's"
                : string.Join("; ", others.GroupBy(rule => rule.IdentityReference.Value).Select(group => $"{Name(group.First().IdentityReference)} could {Rights(group)} it"));
        }

        private static string Rights(IEnumerable<FileSystemAccessRule> rules) =>
            rules.Any(rule => (rule.FileSystemRights & (FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete | FileSystemRights.WriteAttributes)) != 0) ? "modify" : "read";

        private static string Name(IdentityReference? who)
        {
            if (who is null)
            {
                return "unknown";
            }

            try
            {
                return who.Translate(typeof(NTAccount)).Value;
            }
            catch (Exception e) when (e is IdentityNotMappedException or SystemException)
            {
                return who.Value;
            }
        }

        // -------------------------------------------------------------------------------------------------------------
        // The handle's security descriptor — read and replaced through the verified handle, never by path
        // -------------------------------------------------------------------------------------------------------------

        private static DirectorySecurity Read(SafeFileHandle handle)
        {
            if (GetSecurityInfo(handle, FileObject, OwnerAndDacl, out _, out _, out _, out _, out var descriptor) != 0 || descriptor == 0)
            {
                throw new IOException("the folder's owner and DACL could not be read");
            }

            try
            {
                var bytes = new byte[GetSecurityDescriptorLength(descriptor)];
                Marshal.Copy(descriptor, bytes, 0, bytes.Length);
                var security = new DirectorySecurity();
                security.SetSecurityDescriptorBinaryForm(bytes);

                return security;
            }
            finally
            {
                LocalFree(descriptor);
            }
        }

        private static unsafe bool Replaced(SafeFileHandle handle, DirectorySecurity wanted)
        {
            var bytes = wanted.GetSecurityDescriptorBinaryForm();
            fixed (byte* descriptor = bytes)
            {
                return GetSecurityDescriptorDacl((nint)descriptor, out var present, out var dacl, out _) && present
                    && SetSecurityInfo(handle, FileObject, ReplaceDacl, 0, 0, dacl, 0) == 0;
            }
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        private static partial SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        private static partial uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint information, out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        private static partial uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint information, nint owner, nint group, nint dacl, nint sacl);

        [LibraryImport("advapi32.dll")]
        private static partial uint GetSecurityDescriptorLength(nint descriptor);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetSecurityDescriptorDacl(nint descriptor, [MarshalAs(UnmanagedType.Bool)] out bool present, out nint dacl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);

        [LibraryImport("kernel32.dll")]
        private static partial nint LocalFree(nint memory);
    }
}

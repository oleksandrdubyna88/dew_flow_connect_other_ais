using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// The native half of <see cref="PrivateFolderWindows"/>: opening a directory handle, reading and replacing its security
/// descriptor through that handle, and the ACL calls on descendants. It decides NOTHING — every verdict and every sentence is in
/// <see cref="PrivateAcl"/> and <see cref="PrivateFolderWindows"/>, which the Linux suite runs through a fake — it only carries
/// bytes between the system and those. This file is the one entry in <c>sonar.coverage.exclusions</c> for the private-folder
/// work: SonarCloud's analysis host is Linux, this code runs only on Windows, and <c>PrivateFolderWindowsTests</c> exercise it
/// in the <c>windows-latest</c> job.
/// </summary>
/// <remarks>No unsafe code: the descriptor is a pinned managed array, and every native allocation is freed in a <c>finally</c>.</remarks>
[SupportedOSPlatform("windows")]
internal sealed partial class PrivateFolderWindowsNative : IFolderShell
{
    private const uint ReadControl = 0x20000;
    private const uint WriteDac = 0x40000;
    private const uint FileReadAttributes = 0x80;
    private const uint FileListDirectory = 0x1;
    private const uint ShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const int FileObject = 1;
    private const uint OwnerAndDacl = 0x1 | 0x4;
    private const uint ReplaceDacl = 0x4 | 0x80000000;

    /// <summary>The Windows leg of <see cref="PrivateFolder.Open"/>: the decisions (<see cref="PrivateFolderWindows"/>) over this shell.</summary>
    public static PrivateFolderOutcome Open(PrivateFolderWindows.Target target) => PrivateFolderWindows.Open(new PrivateFolderWindowsNative(), target);

    public Sid Me { get; } = new((WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("the current user has no SID")).Value);

    public void Create(string path, AclFacts wanted) => new DirectoryInfo(path).Create(Security(wanted));

    /// <summary>
    /// The ONE handle: verified, re-ACLed through, and then HELD — so what was checked is what is kept. It asks for a data right
    /// (the kernel records a share mode only for a handle that holds one) and WRITE_DAC to re-ACL, without it when the person may
    /// not have that; it shares read and write but never delete, so a rename or a delete of the folder is refused while it is
    /// open, and a folder somebody else already holds open for delete cannot be opened at all.
    /// </summary>
    public ShellOpen Open(string path)
    {
        var handle = OpenHandle(path, ReadControl | FileReadAttributes | FileListDirectory | WriteDac);
        if (handle.IsInvalid)
        {
            handle = OpenHandle(path, ReadControl | FileReadAttributes | FileListDirectory);
        }

        return handle.IsInvalid ? new ShellOpen.Failed(Marshal.GetLastWin32Error()) : new ShellOpen.Held(handle);
    }

    public string FinalPathOf(IDisposable handle) => HandleRead.FinalPathOf((SafeFileHandle)handle);

    public AclFacts Read(IDisposable handle)
    {
        if (GetSecurityInfo((SafeFileHandle)handle, FileObject, OwnerAndDacl, out _, out _, out _, out _, out var descriptor) != 0 || descriptor == 0)
        {
            throw new IOException("the folder's owner and DACL could not be read");
        }

        try
        {
            var bytes = new byte[GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);

            return Facts(bytes);
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    /// <summary>The wanted DACL lives in a pinned copy of the descriptor for exactly the call that applies it.</summary>
    public bool Replace(IDisposable handle, AclFacts wanted)
    {
        var bytes = Security(wanted).GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return GetSecurityDescriptorDacl(pinned.AddrOfPinnedObject(), out var present, out var dacl, out _) && present
                && SetSecurityInfo((SafeFileHandle)handle, FileObject, ReplaceDacl, 0, 0, dacl, 0) == 0;
        }
        finally
        {
            pinned.Free();
        }
    }

    public string Name(Sid who)
    {
        if (who == Sid.None)
        {
            return "unknown";
        }

        try
        {
            return new SecurityIdentifier(who.Value).Translate(typeof(NTAccount)).Value;
        }
        catch (Exception e) when (e is IdentityNotMappedException or SystemException)
        {
            return who.Value;
        }
    }

    public AclFacts AclOf(string entry, bool isFolder) => Facts(SecurityOf(entry, isFolder).GetSecurityDescriptorBinaryForm());

    public void ResetToInherit(string entry, bool isFolder)
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

    /// <summary>A system descriptor as facts: its owner, whether inheritance is cut, and each Allow or Deny entry (any other kind is not judged).</summary>
    private static AclFacts Facts(byte[] descriptor)
    {
        var raw = new RawSecurityDescriptor(descriptor, 0);
        var aces = raw.DiscretionaryAcl is null ? [] : raw.DiscretionaryAcl.OfType<QualifiedAce>().SelectMany(AceFactOf).ToList();

        return new AclFacts(
            raw.Owner is null ? Sid.None : new Sid(raw.Owner.Value),
            (raw.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0,
            raw.DiscretionaryAcl is not null,
            aces);
    }

    private static IEnumerable<AceFact> AceFactOf(QualifiedAce ace) =>
        ace.AceQualifier is AceQualifier.AccessAllowed or AceQualifier.AccessDenied
            ? [new AceFact(new Sid(ace.SecurityIdentifier.Value), ace.AceQualifier == AceQualifier.AccessAllowed, ace.IsInherited, ace.AccessMask, (AceInheritance)(int)(ace.AceFlags & ~AceFlags.Inherited))]
            : [];

    /// <summary>The wanted facts as the system's own object: protected, with one Allow rule per entry (the same construction the folder was always made with).</summary>
    private static DirectorySecurity Security(AclFacts wanted)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: wanted.Protected, preserveInheritance: false);
        foreach (var ace in wanted.Aces)
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(ace.Sid.Value), (FileSystemRights)ace.Mask,
                InheritanceOf(ace.Inheritance), PropagationFlags.None, AccessControlType.Allow));
        }

        return security;
    }
    /// <summary>The wanted entries only ever pass on to subfolders and files, with no limit; <c>AceInheritance</c> has the bits in the other order from <c>InheritanceFlags</c>.</summary>
    private static InheritanceFlags InheritanceOf(AceInheritance inheritance) =>
        (inheritance.HasFlag(AceInheritance.ContainerInherit) ? InheritanceFlags.ContainerInherit : InheritanceFlags.None)
        | (inheritance.HasFlag(AceInheritance.ObjectInherit) ? InheritanceFlags.ObjectInherit : InheritanceFlags.None);

    private static FileSystemSecurity SecurityOf(string entry, bool isFolder) =>
        isFolder ? new DirectoryInfo(entry).GetAccessControl() : new FileInfo(entry).GetAccessControl();

    private static SafeFileHandle OpenHandle(string path, uint access) =>
        CreateFileW(path, access, ShareReadWrite, 0, OpenExisting, BackupSemantics, 0);

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

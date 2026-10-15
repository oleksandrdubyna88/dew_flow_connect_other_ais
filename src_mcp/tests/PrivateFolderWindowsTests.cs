using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one type that enters <c>&lt;homes root&gt;\coai-*</c> (D7 of <c>todo/PLAN_vibe_live_round_fixes.md</c>, per the security
/// review of 2026-10-15): the folder is made with a protected DACL of the person, SYSTEM and Administrators — or an existing one
/// is verified BY HANDLE (its path, its owner, its DACL), re-ACLed when only its DACL is wrong, and refused when its owner is
/// another — and the handle is held for the reservation's life.
/// </summary>
/// <remarks>
/// Windows only (E1–E8), plus E9 on the Unix legs. Every root is a <c>testsToDelete-vh-*</c> folder, never the machine's own. The
/// CI runner is ELEVATED, so a folder it makes may be owned by Administrators rather than the user: E1 asserts the owner is either.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class PrivateFolderWindowsTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("testsToDelete-vh-private-");

    public void Dispose() => _root.Dispose();

    private static SecurityIdentifier Me => WindowsIdentity.GetCurrent().User!;
    private static SecurityIdentifier System => new(WellKnownSidType.LocalSystemSid, null);
    private static SecurityIdentifier Admins => new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static SecurityIdentifier Everyone => new(WellKnownSidType.WorldSid, null);

    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows ACLs: the Unix leg is E9");

    private static DirectorySecurity Security(string path) => new DirectoryInfo(path).GetAccessControl();

    private static void Reset(string path, DirectorySecurity security) => new DirectoryInfo(path).SetAccessControl(security);

    private static PrivateFolderOutcome.Opened Opened(PrivateFolderOutcome outcome) =>
        outcome.Should().BeOfType<PrivateFolderOutcome.Opened>(outcome is PrivateFolderOutcome.Refused r ? r.Sentence : string.Empty).Subject;

    /// <summary>The DACL as exactly the three full-control Allow ACEs, container- and object-inheriting, protected.</summary>
    private static void ShouldBeExactlyThePersonSystemAndAdministrators(string path)
    {
        var security = Security(path);
        security.AreAccessRulesProtected.Should().BeTrue("inheritance from the parent is cut");
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        rules.Select(rule => rule.IdentityReference.Value).Should().BeEquivalentTo([Me.Value, System.Value, Admins.Value]);
        rules.Should().OnlyContain(rule => rule.AccessControlType == AccessControlType.Allow && !rule.IsInherited
            && rule.FileSystemRights == FileSystemRights.FullControl
            && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
            && rule.PropagationFlags == PropagationFlags.None);
    }

    [Fact]
    public void E1_AFreshFolder_IsMadeWithAProtectedDaclOfExactlyThePersonSystemAndAdministrators()
    {
        OnWindows();

        var opened = Opened(PrivateFolder.Open(_root, "coai-vh"));
        using var held = opened.Held;

        opened.Path.Should().Be(Path.Combine(_root, "coai-vh"));
        ShouldBeExactlyThePersonSystemAndAdministrators(opened.Path);
        Security(opened.Path).GetOwner(typeof(SecurityIdentifier))!.Value.Should().BeOneOf(Me.Value, Admins.Value, "an elevated session owns what it makes as Administrators");
    }

    [Fact]
    public void E2_AFolderWithAnExtraAce_IsMadePrivate_AndTheLogNamesWhoCouldReadIt()
    {
        OnWindows();
        var path = Directory.CreateDirectory(Path.Combine(_root, "coai-vh")).FullName;
        var wide = Security(path);
        wide.AddAccessRule(new FileSystemAccessRule(Everyone, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        Reset(path, wide);
        var notes = new List<string>();

        using var held = Opened(PrivateFolder.Open(_root, "coai-vh", notes.Add)).Held;

        ShouldBeExactlyThePersonSystemAndAdministrators(path);
        notes.Should().ContainSingle().Which.Should().Contain(path).And.Contain("made").And.Contain("private").And.Contain("Everyone").And.Contain("read");
    }

    [Fact]
    public void E2b_ADenyAce_IsGone_AfterTheFolderIsMadePrivate()
    {
        OnWindows();
        var path = Directory.CreateDirectory(Path.Combine(_root, "coai-vh")).FullName;
        var denied = Security(path);
        denied.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
        Reset(path, denied);

        using var held = Opened(PrivateFolder.Open(_root, "coai-vh")).Held;

        ShouldBeExactlyThePersonSystemAndAdministrators(path);
    }

    /// <summary>E3: the %LOCALAPPDATA% shape — an upgrade's existing <c>coai-vibe\&lt;id&gt;\h\file</c> — is migrated, children included.</summary>
    [Fact]
    public void E3_AnExistingTreeInTheLocalAppDataShape_IsMigrated_ItsChildFileIncluded()
    {
        OnWindows();
        var file = Path.Combine(_root, "coai-vibe", "0123456789abcdef0123456789abcdef", "h", "vibehistory");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "Hello Vibe!");

        using var held = Opened(PrivateFolder.Open(_root, "coai-vibe")).Held;

        ShouldBeExactlyThePersonSystemAndAdministrators(Path.Combine(_root, "coai-vibe"));
        var child = new FileInfo(file).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        child.Select(rule => rule.IdentityReference.Value).Should().BeEquivalentTo([Me.Value, System.Value, Admins.Value], "the new DACL reached the existing child");
    }

    [Fact]
    public async Task E4_AFolderThatIsAJunctionToAPrivateOne_IsRefused_AndNothingIsMadeThroughIt()
    {
        OnWindows();
        using var target = TempDir.For("testsToDelete-vh-target-");
        await DirectoryLink.MakeAsync(new ProcessLauncher(), Path.Combine(_root, "coai-vh"), target);

        var refused = PrivateFolder.Open(_root, "coai-vh").Should().BeOfType<PrivateFolderOutcome.Refused>().Subject;

        refused.Sentence.Should().Contain(Path.Combine(_root, "coai-vh"));
        Directory.EnumerateFileSystemEntries(target).Should().BeEmpty();
        DirectoryLink.Remove(Path.Combine(_root, "coai-vh"));
    }

    [Fact]
    public void E5_AFolderOwnedBySomeoneElse_IsRefused_NamingTheOwnerAndTheSetting()
    {
        OnWindows();
        var path = Directory.CreateDirectory(Path.Combine(_root, "coai-vh")).FullName;
        if (!Privileges.TryOwnAs(path, System))
        {
            Assert.Skip("setting another owner needs SeRestorePrivilege, which this session does not hold (it is not elevated)");
        }

        var refused = PrivateFolder.Open(_root, "coai-vh").Should().BeOfType<PrivateFolderOutcome.Refused>().Subject;

        refused.Sentence.Should().Contain("not private to you").And.Contain("NT AUTHORITY\\SYSTEM").And.Contain("COAI_CONSULTANT_HOMES");
    }

    /// <summary>E6: a conversation and its sweep, on a root whose coai-vibe is a junction, are refused and sweep nothing.</summary>
    [Fact]
    public async Task E6_AConversationAndItsSweep_AreRefused_OnARootWhoseCoaiVibeIsAJunction()
    {
        OnWindows();
        using var target = TempDir.For("testsToDelete-vh-target-");
        Directory.CreateDirectory(Path.Combine(target, "0123456789abcdef0123456789abcdef"));
        var link = Path.Combine(_root, ConsultantHomes.Vibe);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), link, target);
        var places = new ConsultantPlaces(_root, "store", new ProcessLauncher(), _ => { });

        using var place = await places.ForConversationAsync(new VibeConsultant(new VibeRuntime()), ConsultationStore.NewId(), _root, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        var warnings = new List<string>();
        var swept = ConsultantHomeSweep.Sweep(new HomeSweepInputs(_root, "store", _ => null, DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1)), warnings.Add);

        place.Refusal.Should().Contain(link);
        swept.Should().Be(0);
        warnings.Should().NotBeEmpty().And.OnlyContain(said => said.Contains(link, StringComparison.Ordinal));
        Directory.EnumerateFileSystemEntries(target).Should().HaveCount(1, "nothing was made or removed through the link");
        DirectoryLink.Remove(link);
    }

    /// <summary>E7: the claim is measured, not assumed — a folder whose handle is held cannot be renamed.</summary>
    [Fact]
    public void E7_AFolderWhoseHandleIsHeld_CannotBeRenamed()
    {
        OnWindows();
        var opened = Opened(PrivateFolder.Open(_root, "coai-vh"));

        var moving = () => Directory.Move(opened.Path, Path.Combine(_root, "coai-vh2"));

        moving.Should().Throw<IOException>("the handle is held without FILE_SHARE_DELETE, so the folder cannot be swapped under the reservation");
        opened.Held.Dispose();
        Directory.Move(opened.Path, Path.Combine(_root, "coai-vh2"));
    }

    /// <summary>Enabling SeRestorePrivilege and setting an owner — what a test needs to play "somebody else made this folder".</summary>
    private static class Privileges
    {
        public static bool TryOwnAs(string path, SecurityIdentifier owner)
        {
            if (!Enable("SeRestorePrivilege"))
            {
                return false;
            }

            try
            {
                var security = Security(path);
                security.SetOwner(owner);
                Reset(path, security);

                return true;
            }
            catch (Exception e) when (e is UnauthorizedAccessException or InvalidOperationException or IOException)
            {
                return false;
            }
        }

        private static bool Enable(string name)
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token) || !LookupPrivilegeValueW(null, name, out var luid))
            {
                return false;
            }

            try
            {
                var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };

                return AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0) && Marshal.GetLastWin32Error() == 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public uint Count;
            public long Luid;
            public uint Attributes;
        }

        [DllImport("kernel32.dll")]
        private static extern nint GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(nint process, uint access, out nint token);

        [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges state, uint length, nint previous, nint returned);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(nint handle);
    }
}

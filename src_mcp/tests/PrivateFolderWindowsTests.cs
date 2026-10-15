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
[Collection("process-privileges")]
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

    /// <summary>
    /// The helper behind E5, on a privilege every standard token holds but keeps disabled: enabling it must work, or E5 can never run
    /// (its first layout of TOKEN_PRIVILEGES made it skip on every machine).
    /// </summary>
    [Fact]
    public void ThePrivilegeHelper_EnablesAPrivilegeTheTokenHolds_AndPutsItBack()
    {
        OnWindows();

        using var enabled = TokenPrivilege.Enable("SeIncreaseWorkingSetPrivilege");

        enabled.Should().NotBeNull("a standard user token holds SeIncreaseWorkingSetPrivilege, disabled; the helper turns it on");
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
        using var restoring = TokenPrivilege.Enable("SeRestorePrivilege");
        if (restoring is null)
        {
            Assert.Skip("setting another owner needs SeRestorePrivilege, which this session does not hold (it is not elevated)");
        }

        var security = Security(path);
        security.SetOwner(System);
        Reset(path, security);

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

    private static FileSystemAccessRule EveryoneReads() =>
        new(Everyone, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);

    /// <summary>A parent that is not ours (an Everyone entry), so the next Open must re-ACL it.</summary>
    private string WideFolder(string name)
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var wide = Security(path);
        wide.AddAccessRule(EveryoneReads());
        Reset(path, wide);

        return path;
    }

    private static List<FileSystemAccessRule> RulesOf(FileSystemSecurity security) =>
        [.. security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()];

    /// <summary>A descendant after the re-ACL: not protected, no explicit entry, exactly the person, SYSTEM and Administrators, all inherited.</summary>
    private static void ShouldInheritExactlyTheThree(FileSystemSecurity security, string what)
    {
        security.AreAccessRulesProtected.Should().BeFalse($"{what} inherits from its parent again");
        var rules = RulesOf(security);
        rules.Select(rule => rule.IdentityReference.Value).Should().BeEquivalentTo([Me.Value, System.Value, Admins.Value], what);
        rules.Should().OnlyContain(rule => rule.IsInherited, $"{what} holds no explicit entry");
    }

    /// <summary>
    /// The race the first version had: it verified one handle (no data right, so no share mode, so it blocked nothing) and then
    /// held ANOTHER one, opened by path and never verified. The folder renamed away and an ordinary wide one put in its place
    /// DURING the re-ACL was then handed back as the private one. Now ONE handle is verified and held: the rename fails.
    /// </summary>
    [Fact]
    public void E10_AFolderRenamedAndReplacedWhileItIsBeingMadePrivate_IsNeverHandedBack()
    {
        OnWindows();
        var path = WideFolder("coai-vh");
        var renameRefused = false;

        var outcome = PrivateFolder.Open(_root, "coai-vh", _ =>
        {
            try
            {
                Directory.Move(path, path + "-aside");
                var replacement = Directory.CreateDirectory(path).FullName;
                var wide = Security(replacement);
                wide.AddAccessRule(EveryoneReads());
                Reset(replacement, wide);
            }
            catch (IOException)
            {
                renameRefused = true;
            }
        });

        using var held = outcome is PrivateFolderOutcome.Opened opened ? opened.Held : null;
        (renameRefused || outcome is PrivateFolderOutcome.Refused).Should().BeTrue(
            "the verified folder was swapped for a wide one during the re-ACL and Open still returned it as private");
    }

    [Fact]
    public void E10b_TheHandleHeldAfterAReAcl_IsTheOneThatWasVerified_SoTheFolderCannotBeRenamed()
    {
        OnWindows();
        var path = WideFolder("coai-vh");

        var opened = Opened(PrivateFolder.Open(_root, "coai-vh"));

        var moving = () => Directory.Move(path, path + "-aside");
        moving.Should().Throw<IOException>("the re-ACL path used to hold nothing at all");
        opened.Held.Dispose();
    }

    [Fact]
    public void E10c_AFolderSomebodyElseHoldsOpenForDeletion_IsRefused()
    {
        OnWindows();
        var path = Directory.CreateDirectory(Path.Combine(_root, "coai-vh")).FullName;
        using var other = OpenDirectoryHandle(path);

        PrivateFolder.Open(_root, "coai-vh").Should().BeOfType<PrivateFolderOutcome.Refused>("a delete-sharing opener means the folder can be removed under the reservation");
    }

    /// <summary>A directory handle holding DELETE access (DeleteOnClose asks for it) and sharing delete, the way something that can remove the folder holds it.</summary>
    private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenDirectoryHandle(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.DeleteOnClose | (FileOptions)0x02000000);

    [Fact]
    public void E3b_AnExplicitEntryOnAChild_AndAProtectedChild_AreMadePrivateToo()
    {
        OnWindows();
        var parent = WideFolder("coai-vibe");
        var explicitChild = Path.Combine(parent, "id", "h", "transcript.jsonl");
        var protectedChild = Path.Combine(parent, "id", "h", "protected.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(explicitChild)!);
        File.WriteAllText(explicitChild, "the reviewed code");
        File.WriteAllText(protectedChild, "the reviewed code");
        var one = new FileInfo(explicitChild).GetAccessControl();
        one.AddAccessRule(new FileSystemAccessRule(Everyone, FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(explicitChild).SetAccessControl(one);
        var two = new FileInfo(protectedChild).GetAccessControl();
        two.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        two.AddAccessRule(new FileSystemAccessRule(Everyone, FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(protectedChild).SetAccessControl(two);

        using var held = Opened(PrivateFolder.Open(_root, "coai-vibe")).Held;

        ShouldInheritExactlyTheThree(new FileInfo(explicitChild).GetAccessControl(), "a child with an explicit Everyone entry");
        ShouldInheritExactlyTheThree(new FileInfo(protectedChild).GetAccessControl(), "a child with a protected DACL");
        ShouldInheritExactlyTheThree(new DirectoryInfo(Path.Combine(parent, "id", "h")).GetAccessControl(), "a child folder");
    }

    [Fact]
    public void E3c_AnInheritableEntryOnTheParent_IsGoneFromTheChildAfterwards()
    {
        OnWindows();
        var parent = WideFolder("coai-vibe");
        var file = Path.Combine(parent, "id", "h", "vibehistory");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");
        RulesOf(new FileInfo(file).GetAccessControl()).Select(rule => rule.IdentityReference.Value).Should().Contain(Everyone.Value, "the fixture is wide to begin with");

        using var held = Opened(PrivateFolder.Open(_root, "coai-vibe")).Held;

        ShouldInheritExactlyTheThree(new FileInfo(file).GetAccessControl(), "a child of a parent that had an inheritable Everyone entry");
    }

    [Fact]
    public async Task E3d_AJunctionInsideTheFolder_IsNeverFollowed_ItsTargetsSecurityIsUnchanged()
    {
        OnWindows();
        var parent = WideFolder("coai-vibe");
        using var target = TempDir.For("testsToDelete-vh-target-");
        File.WriteAllText(target.At("outside.txt"), "not ours");
        var targetBefore = new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        var fileBefore = new FileInfo(target.At("outside.txt")).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        var link = Path.Combine(parent, "id", "link");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), link, target);

        using var held = Opened(PrivateFolder.Open(_root, "coai-vibe")).Held;

        new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All).Should().Be(targetBefore);
        new FileInfo(target.At("outside.txt")).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All).Should().Be(fileBefore);
        DirectoryLink.Remove(link);
    }

    /// <summary>The documented limit: the children are walked only when the parent itself needed the re-ACL.</summary>
    [Fact]
    public void E3e_ASecondOpenOfAnAlreadyPrivateParent_DoesNotWalkItsChildren()
    {
        OnWindows();
        var parent = Path.Combine(_root, "coai-vibe");
        using (Opened(PrivateFolder.Open(_root, "coai-vibe")).Held)
        {
        }

        var file = Path.Combine(parent, "later.jsonl");
        File.WriteAllText(file, "x");
        var wide = new FileInfo(file).GetAccessControl();
        wide.AddAccessRule(new FileSystemAccessRule(Everyone, FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(file).SetAccessControl(wide);

        using var held = Opened(PrivateFolder.Open(_root, "coai-vibe")).Held;

        RulesOf(new FileInfo(file).GetAccessControl()).Select(rule => rule.IdentityReference.Value).Should().Contain(Everyone.Value,
            "an already-private parent is not walked: only a re-ACL walks the children (a stated limit)");
    }
}

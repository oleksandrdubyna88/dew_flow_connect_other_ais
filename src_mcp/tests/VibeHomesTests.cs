using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one-shot homes' folder, its owner and its sweep (D1, D3 of <c>todo/PLAN_vibe_live_round_fixes.md</c>; scenarios A12 and B1–B5).
/// Every root is a <c>testsToDelete-vh-*</c> folder, never the machine's own.
/// </summary>
public sealed class VibeHomesTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("testsToDelete-vh-sweep-");

    public void Dispose() => _root.Dispose();

    private static readonly DateTime Now = DateTime.UtcNow;

    /// <summary>A folder as a crashed owner leaves it: <c>coai-vh\&lt;8&gt;\&lt;6&gt;</c> with a read-only plugin folder, written <paramref name="age"/> ago.</summary>
    private string Leftover(TimeSpan age, string id = "0ddba11c")
    {
        var folder = VibeHomes.OneShotIn(_root, id);
        var plugin = Path.Combine(folder, "a1b2c3", "logs", "session", "plugins", "packages", "6b", "x", "skills", "vibe");
        Directory.CreateDirectory(plugin);
        File.WriteAllText(Path.Combine(plugin, "SKILL.md"), "x");
        var when = Now - age;
        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Prepend(folder))
        {
            Directory.SetLastWriteTimeUtc(path, when);
        }

        File.SetAttributes(plugin, FileAttributes.ReadOnly);

        return folder;
    }

    [Fact]
    public void AnOldFolderNobodyHolds_IsRemoved_ReadOnlyPluginFoldersIncluded()
    {
        var folder = Leftover(TimeSpan.FromHours(7));
        var warnings = new List<string>();

        VibeHomes.SweepOneShot(_root, Now - TimeSpan.FromHours(6), warnings.Add).Should().Be(1);

        Directory.Exists(folder).Should().BeFalse("older than the cutoff and its lock free: removed, a read-only plugin folder or not");
        warnings.Should().NotContain(said => !said.StartsWith("made ", StringComparison.Ordinal), "only the one line about the folder being made private (a temp root passes its ACL down)");
    }

    [Fact]
    public void AYoungFolder_IsKept()
    {
        var folder = Leftover(TimeSpan.FromHours(1));

        VibeHomes.SweepOneShot(_root, Now - TimeSpan.FromHours(6), _ => { }).Should().Be(0);

        Directory.Exists(folder).Should().BeTrue("written an hour ago, inside the six-hour window");
    }

    [Fact]
    public void AFolderWhoseLockAnOwnerHolds_IsKeptHoweverOld_AndTakenOnceTheOwnerEnds()
    {
        var folder = Leftover(TimeSpan.FromDays(2));
        var lockPath = Path.Combine(folder, VibeHomes.LockName);
        var owner = HeldFile.Take(lockPath).Should().BeOfType<Hold.Taken>().Subject;

        VibeHomes.SweepOneShot(_root, Now - TimeSpan.FromHours(6), _ => { }).Should().Be(0, "its owner is alive — in this process or another server's");
        Directory.Exists(folder).Should().BeTrue();
        owner.Stream.Dispose();

        VibeHomes.SweepOneShot(_root, Now - TimeSpan.FromHours(6), _ => { }).Should().Be(1, "the owner is gone, and the lock file's own write is not what the age reads");
        Directory.Exists(folder).Should().BeFalse();
    }

    [Fact]
    public void ALinkUnderCoaiVh_IsSaidAndLeftAlone_AndNeverEntered()
    {
        using var target = TempDir.For("testsToDelete-vh-target-");
        File.WriteAllText(target.At("precious.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_root, VibeHomes.OneShotFolder));
        var link = VibeHomes.OneShotIn(_root, "0badf00d");
        SnapshotTestKit.LinkNow(link, target);
        Directory.SetLastWriteTimeUtc(target, Now - TimeSpan.FromDays(9));
        var warnings = new List<string>();

        VibeHomes.SweepOneShot(_root, Now - TimeSpan.FromHours(6), warnings.Add).Should().Be(0);

        File.Exists(target.At("precious.txt")).Should().BeTrue("the target of the link is untouched");
        warnings.Where(said => !said.StartsWith("made ", StringComparison.Ordinal)).Should().ContainSingle().Which.Should().Contain(link).And.Contain("link");
    }

    [Fact]
    public void CoaiVhItselfALink_SweepsNothing_AndIsRefusedForAReservation_NothingMadeUnderIt()
    {
        using var target = TempDir.For("testsToDelete-vh-target-");
        var vh = Path.Combine(_root, VibeHomes.OneShotFolder);
        SnapshotTestKit.LinkNow(vh, target);
        var warnings = new List<string>();

        VibeHomes.SweepOneShot(_root, Now + TimeSpan.FromDays(1), warnings.Add).Should().Be(0);
        var reserved = VibeHomes.ReserveOneShot(_root);

        warnings.Should().ContainSingle().Which.Should().Contain(vh);
        reserved.Should().BeOfType<OneShotReservation.Refused>().Which.Sentence.Should().Contain(vh).And.Contain("link");
        Directory.EnumerateFileSystemEntries(target).Should().BeEmpty("nothing was made through the link");
    }

    /// <summary>A junction whose target is gone: `Directory.Exists` is false for it, which used to make the sweep silent about it.</summary>
    [Fact]
    public void CoaiVhADanglingJunction_IsSaid_NotSweptInSilence()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a junction whose target is gone");
        var target = Directory.CreateDirectory(_root.At("gone-soon")).FullName;
        var vh = Path.Combine(_root, VibeHomes.OneShotFolder);
        SnapshotTestKit.LinkNow(vh, target);
        Directory.Delete(target);
        var warnings = new List<string>();

        VibeHomes.SweepOneShot(_root, Now + TimeSpan.FromDays(1), warnings.Add).Should().Be(0);

        warnings.Should().ContainSingle().Which.Should().Contain(vh).And.Contain("link");
    }

    [Fact]
    public void AnEmptyRoot_ReservesNothing_AndSweepsNothing()
    {
        VibeHomes.ReserveOneShot(string.Empty).Should().BeSameAs(OneShotReservation.None.Instance);
        VibeHomes.SweepOneShot(string.Empty, Now, _ => { }).Should().Be(0);
    }

    [Fact]
    public void AReservation_IsAFolderUnderCoaiVhHoldingItsLock_AndDisposingTakesItWholeWithAReadOnlyFolder()
    {
        var reserved = VibeHomes.ReserveOneShot(_root).Should().BeOfType<OneShotReservation.Reserved>().Subject;
        var folder = reserved.Home.Folder;

        Path.GetDirectoryName(folder).Should().Be(Path.Combine(_root, VibeHomes.OneShotFolder));
        Path.GetFileName(folder).Should().HaveLength(8);
        HeldFile.IsHeld(Path.Combine(folder, VibeHomes.LockName)).Should().BeTrue("the owner holds the lock for its life");
        var plugin = Directory.CreateDirectory(Path.Combine(folder, "a1b2c3", "logs", "session", "plugins", "packages", "6b", "x", "skills", "vibe"));
        File.SetAttributes(plugin.FullName, FileAttributes.ReadOnly);

        reserved.Home.Dispose();

        Directory.Exists(folder).Should().BeFalse();
        reserved.Home.Dispose();
    }

    /// <summary>B6: the product's six-hour scratch sweep takes a crash leftover under the homes root too.</summary>
    [Fact]
    public void TheScratchSweep_TakesAOneShotLeftoverUnderTheHomesRoot()
    {
        var folder = Leftover(TimeSpan.FromHours(7));
        Server.PanelService.ForgetTheLastSweep();

        Server.PanelService.PruneOldAnswerDirs(_root);

        Directory.Exists(folder).Should().BeFalse("PanelService.PruneOldAnswerDirs sweeps <root>/coai-vh as well as coai-answers-* in temp");
    }

    /// <summary>B5: the conversation-home sweep never touches a round's home — <c>coai-vh</c> is not one of the folders it walks.</summary>
    [Fact]
    public void TheConversationHomeSweep_LeavesCoaiVhAlone()
    {
        var folder = Leftover(TimeSpan.FromDays(8));
        var inputs = new Server.HomeSweepInputs(_root, "store", _ => null, Now, TimeSpan.FromMinutes(30));

        Server.ConsultantHomeSweep.Sweep(inputs, _ => { }).Should().Be(0);

        Directory.Exists(folder).Should().BeTrue("coai-vh is outside ConsultantHomes.All, so D20 cannot judge a round's home as a foreign one");
        ConsultantHomes.All.Should().NotContain(VibeHomes.OneShotFolder);
    }
}

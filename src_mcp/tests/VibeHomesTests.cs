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

    /// <summary>A link (a junction, a symlink) whose target is gone: `Directory.Exists` is false for it, which used to make the sweep silent about it.</summary>
    [Fact]
    public void CoaiVhADanglingJunction_IsSaid_NotSweptInSilence()
    {
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

    private sealed class Pin : IDisposable
    {
        public bool Released { get; private set; }

        public void Dispose() => Released = true;
    }

    /// <summary>
    /// CodeRabbit on PR #823: the pinned <c>coai-vh</c> handle was leaked when making the home folder threw — the reservation
    /// answered "refused" and the caller never held the pin, so nothing could ever dispose it.
    /// </summary>
    [Fact]
    public void WhenMakingTheHomeFolderThrows_ThePinnedCoaiVhHandleIsReleased_AndTheReservationIsRefused()
    {
        var pin = new Pin();
        var steps = new OneShotSteps(
            (root, name, _) => new PrivateFolderOutcome.Opened(pin, PrivateFolder.PathOf(root, name)),
            _ => throw new IOException("the disk is full"));

        var reserved = VibeHomes.ReserveOneShot(_root, _ => { }, steps);

        reserved.Should().BeOfType<OneShotReservation.Refused>().Which.Sentence.Should().Contain("the disk is full");
        pin.Released.Should().BeTrue("a handle nobody holds is a handle nobody will ever let go of");
    }

    [Fact]
    public void WhenMakingTheHomeFolderThrowsSomethingElse_ThePinIsStillReleased_AndItReachesTheCaller()
    {
        var pin = new Pin();
        var steps = new OneShotSteps(
            (root, name, _) => new PrivateFolderOutcome.Opened(pin, PrivateFolder.PathOf(root, name)),
            _ => throw new InvalidOperationException("not an IO failure"));

        var reserve = () => VibeHomes.ReserveOneShot(_root, _ => { }, steps);

        reserve.Should().Throw<InvalidOperationException>();
        pin.Released.Should().BeTrue();
    }

    /// <summary>The same leak with the REAL handle: on Windows a held handle makes the folder impossible to rename.</summary>
    [Fact]
    public void WhenMakingTheHomeFolderThrows_TheRealHandleIsLetGo_SoCoaiVhCanBeRenamed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows keeps a folder from being renamed while a handle is held");
        var steps = OneShotSteps.Real with { Make = _ => throw new IOException("the disk is full") };

        VibeHomes.ReserveOneShot(_root, _ => { }, steps).Should().BeOfType<OneShotReservation.Refused>();

        var vh = Path.Combine(_root, VibeHomes.OneShotFolder);
        var rename = () => Directory.Move(vh, vh + "-aside");
        rename.Should().NotThrow("nothing holds coai-vh any more");
    }

    [Fact]
    public void WhenTheLockCannotBeTaken_ThePinIsReleased_AndTheReservationIsRefusedNamingTheFolder()
    {
        var pin = new Pin();
        string? folder = null;
        var steps = new OneShotSteps(
            (root, name, _) => new PrivateFolderOutcome.Opened(pin, PrivateFolder.PathOf(root, name)),
            made =>
            {
                folder = Directory.CreateDirectory(made).FullName;
                Directory.CreateDirectory(Path.Combine(made, VibeHomes.LockName));
            });

        var reserved = VibeHomes.ReserveOneShot(_root, _ => { }, steps);

        reserved.Should().BeOfType<OneShotReservation.Refused>().Which.Sentence.Should().Contain(folder!).And.Contain("could not be locked");
        pin.Released.Should().BeTrue();
    }

    [Fact]
    public void ARefusedCoaiVh_IsReportedAsItsSentence_AndNothingIsMade()
    {
        var made = false;
        var steps = new OneShotSteps(
            (_, _, _) => new PrivateFolderOutcome.Refused("coai-vh is not private to you"),
            _ => made = true);

        VibeHomes.ReserveOneShot(_root, _ => { }, steps).Should().BeOfType<OneShotReservation.Refused>().Which.Sentence.Should().Be("coai-vh is not private to you");
        made.Should().BeFalse();
    }

    [Fact]
    public void AFolderThatCannotBeListed_IsSaid_AndSweepsNothing_UntilTheNextPass()
    {
        Leftover(TimeSpan.FromHours(7));
        var warnings = new List<string>();
        var io = HomeIo.Real with { List = _ => throw new IOException("the disk went away") };

        VibeHomes.SweepOneShot(_root, Now - TimeSpan.FromHours(6), warnings.Add, io).Should().Be(0);

        warnings.Should().Contain(said => said.Contains("could not be listed", StringComparison.Ordinal) && said.Contains("the disk went away", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOldFolderThatCannotBeRemoved_IsSaid_AndKept_AndTheOtherOneStillGoes()
    {
        var stuck = Leftover(TimeSpan.FromHours(7), "5701c4ed");
        var fine = Leftover(TimeSpan.FromHours(8), "0ddba11c");
        var warnings = new List<string>();
        var io = HomeIo.Real with
        {
            Delete = entry =>
            {
                if (entry.StartsWith(stuck, StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException("the test holds this one");
                }

                GitScratch.DeleteEvenIfReadOnly(entry);
            },
        };

        VibeHomes.SweepOneShot(_root, Now - TimeSpan.FromHours(6), warnings.Add, io).Should().Be(1);

        Directory.Exists(fine).Should().BeFalse("the pass goes on past the folder it could not remove");
        warnings.Should().ContainSingle(said => said.Contains(stuck, StringComparison.Ordinal) && said.Contains("next sweep will try again", StringComparison.Ordinal));
    }

    [Fact]
    public void AHomeThatCannotBeDeleted_WhenItsOwnerEnds_IsSaid_NotThrown_AndTheScratchSweepTakesItLater()
    {
        var folder = Directory.CreateDirectory(VibeHomes.OneShotIn(_root, "c0ffee00")).FullName;
        var warnings = new List<string>();
        var released = false;
        var home = new OneShotHome(folder, new Pin(), warnings.Add) { Remove = _ => throw new IOException("in use") };

        var ending = () =>
        {
            home.Dispose();
            released = true;
        };

        ending.Should().NotThrow();
        released.Should().BeTrue();
        warnings.Should().ContainSingle().Which.Should().Contain(folder).And.Contain("in use").And.Contain("scratch sweep");
        Directory.Exists(folder).Should().BeTrue("the failed delete left it for the sweep");
    }
}

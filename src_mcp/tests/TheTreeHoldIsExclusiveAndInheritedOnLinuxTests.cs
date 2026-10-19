using System.Diagnostics;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// L-N1 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c> (DL8; S-K4 and S-K5) on the PRODUCT's tree hold — <c>KimiCredentialsLock.HoldTree</c>, keyed by the sign-in folder's identity — in
/// a real second process (<c>FakeCli tree-hold</c>), on Linux CI with no stand-in. <see cref="HeldFileInheritableOnLinuxTests"/> asks the same two questions of the bare primitive;
/// this asks them of what the shim actually takes: the sign-in's file and the home's.
/// </summary>
/// <remarks>Each arm is its own test, so a red names exactly the premise that broke: (a) the hold is EXCLUSIVE — a non-inheritable and a second inheritable take are both refused; (b) the descriptor is INHERITED — it outlives its opener's SIGKILL while a child started after the take lives.</remarks>
public sealed class TheTreeHoldIsExclusiveAndInheritedOnLinuxTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-treehold-");

    private readonly List<Process> _children = [];

    private string Locks => _dir.At("locks");

    private string Persons => _dir.At("persons", "credentials");

    private string Home => _dir.At("owned", "kh-x");

    public TheTreeHoldIsExclusiveAndInheritedOnLinuxTests()
    {
        Directory.CreateDirectory(Persons);
        Directory.CreateDirectory(Home);
    }

    public void Dispose()
    {
        foreach (var child in _children.Where(child => !child.HasExited))
        {
            child.Kill();
        }

        _children.ForEach(child => child.Dispose());
        _dir.Dispose();
    }

    private async Task<(Process Shim, Process Grandchild)> HolderAsync()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "L-N1 asks what .NET does on Linux; the Windows share mode is the kernel's (KimiCredentialsLockTests covers it)");
        var (shim, grandchild) = await ShimChild.StartAsync("tree", Locks, Persons, Home);
        _children.AddRange([shim, grandchild]);

        return (shim, grandchild);
    }

    private string TreeFile()
    {
        using var rooted = KimiCredentialsLock.RootFor(Locks);

        return KimiCredentialsLock.TreePathFor(Persons);
    }

    [Fact]
    public async Task L_N1a_ANonInheritableTake_BesideTheShimsHold_IsBusy()
    {
        await HolderAsync();

        var hold = HeldFile.Take(TreeFile());
        try
        {
            hold.Should().BeOfType<Hold.Busy>("a non-inheritable take beside an inheritable hold succeeded — the hold is not a lock at all");
        }
        finally
        {
            if (hold is Hold.Taken taken)
            {
                taken.Stream.Dispose();
            }
        }
    }

    [Fact]
    public async Task L_N1a_ASecondShimOnTheSameSignIn_IsBusy_TheLockIsExclusive()
    {
        await HolderAsync();

        var second = await ShimChild.FailsAsync("tree", Locks, Persons, Home);

        second.Exit.Should().Be(69, "a second take of an inheritable hold succeeded — the lock is shared, not exclusive (S-K4)");
        second.Error.Should().Contain("Busy");
    }

    [Fact]
    public async Task L_N1b_TheHold_SurvivesItsOpenersSigkill_WhileTheGrandchildLives_AndIsFreeAfterwards()
    {
        var (shim, grandchild) = await HolderAsync();
        var tree = TreeFile();
        var homeHold = Path.Combine(Home, KimiCredentialsLock.HomeHoldName);

        await ShimChild.EndAsync(shim);

        (await ShimChild.StaysHeldAsync(tree)).Should().BeTrue("the hold did not survive its opener's SIGKILL while the grandchild lived — the descriptor was not inherited (O_CLOEXEC) (S-K5)");
        (await ShimChild.StaysHeldAsync(homeHold)).Should().BeTrue("the per-home hold did not survive its opener's SIGKILL either");

        await ShimChild.EndAsync(grandchild);

        (await Polls.Until(() => HeldFile.Probe(tree) == LockProbe.Free && HeldFile.Probe(homeHold) == LockProbe.Free, ShimChild.Patience))
            .Should().BeTrue("both holds should be free once the last inheritor is gone");
    }
}

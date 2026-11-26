using System.Diagnostics;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server.Keys;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The third security judgement of s7 (cb82b7ee..9392d443, <c>v2/epoch-3/e2-coder-one-task/RESULTS_s7_security_judgement_part3.md</c>): an exposed mark is never lost.</summary>
public sealed partial class AKeyHardeningTests
{
    private static RoleKeyRoad RoadOf(KeyWorld world) =>
        RoleKeyRoad.For(world.DataDir, world.Clock, offered =>
        {
            world.Notices.Add(offered);

            return true;
        }, _ => null);

    private static RoleLaunched LaunchThatPrintedItsKey(string keyId) =>
        new(new ReviewerLaunch(null, "answer", Usage.None, string.Empty, null), null, RoleModelLauncher.RuntimeOf("claude").Runtime, null, string.Empty, KeyId: keyId, KeyExposed: true);

    /// <summary>Makes every call that takes <c>pool.lock</c> fail at once: the lock path is a folder, which the pool never locks through.</summary>
    private static void BreakTheLock(KeyWorld world)
    {
        var path = Path.Combine(world.KeysRoot, "pool.lock");
        File.Delete(path);
        Directory.CreateDirectory(path);
    }

    private static void MendTheLock(KeyWorld world) => Directory.Delete(Path.Combine(world.KeysRoot, "pool.lock"));

    [Fact]
    public async Task P3_TheExposedMark_WaitsForALockThatAnotherCallHoldsPastAPicksWindow_AndTheKeyIsDeadWhenItIsDone()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("A", "a");
        var held = KeySession.Open(world.Access().Folder, create: false);
        held.Ok.Should().BeTrue(held.Refusal);
        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3.5), TestContext.Current.CancellationToken);
            held.Value!.Dispose();
        }, TestContext.Current.CancellationToken);
        var watch = Stopwatch.StartNew();

        var recorded = new KeyPool(world.Access()).Record(row.Id, new KeyLaunchReport(KeyLaunchEnd.Exposed));

        watch.Stop();
        await release;
        recorded.Ok.Should().BeTrue($"a mark that the key was exposed is waited for, not given up at the 2 s window of a pick: {recorded.Refusal}");
        watch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(3), "the lock was held that long, so the call really waited");
        var after = world.Admin.List(false).Value!.Rows.Single();
        after.State.Should().Be(KeyStates.Dead);
        after.DeadReason.Should().Be(KeyDeadReasons.Exposed);
        world.Notices.Select(notice => notice.Code).Should().Equal([ServerNoticeCodes.ClaudeKeyExposed], "the one notice of the exposure");
    }

    [Fact]
    public void P3_AnOrdinaryRecord_StillGivesUpAtAPicksWindow_SoTheLongWaitIsForTheExposedMarkAlone()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("A", "a");
        using var held = KeySession.Open(world.Access().Folder, create: false).Value!;
        var watch = Stopwatch.StartNew();

        var recorded = new KeyPool(world.Access()).Record(row.Id, new KeyLaunchReport(KeyLaunchEnd.Succeeded));

        watch.Stop();
        recorded.Ok.Should().BeFalse("the lock is held and a record of a good launch is not worth a minute");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void P3_AnExposedMarkThatCannotBeWritten_RaisesOneNotice_NamingTheKeyAsStillEnabled_AndNeverTheText()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("A", "a");
        BreakTheLock(world);

        var marked = RoadOf(world).Exposed(row.Id);

        marked.Should().BeFalse("the mark was not written");
        var notice = world.Notices.Should().ContainSingle().Subject;
        notice.Code.Should().Be(ServerNoticeCodes.ClaudeKeyExposed);
        notice.Subject.Should().Be(row.Id);
        $"{notice.Title} {notice.Detail} {notice.Cure}".Should().Contain("still enabled").And.Contain($"--keys disable --id {row.Id}").And.NotContain(KeyWorld.Sentinel);
        MendTheLock(world);
        world.Admin.List(false).Value!.Rows.Single().State.Should().Be(KeyStates.Enabled, "the companion: the key really was left enabled, so the notice told the truth");
    }

    [Fact]
    public void P3_ALaunchThatPrintedItsKey_WhoseMarkCannotBeWritten_RaisesOneNotice_AndTheLaunchIsStillReadAsExposed()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("A", "a");
        BreakTheLock(world);
        var launched = LaunchThatPrintedItsKey(row.Id);
        var stop = RoleFailover.StopOfLaunch(new ModelRef("sonnet", "claude"), launched);

        RoadOf(world).Ended(launched, stop);

        stop.Should().NotBeNull();
        stop!.Word.Should().Be(RoleStopWords.KeyInOutput, "the job fails, whether or not the mark could be written");
        world.Notices.Should().ContainSingle().Which.Code.Should().Be(ServerNoticeCodes.ClaudeKeyExposed);
    }

    [Fact]
    public void P3_AnExposedMarkThatIsWritten_RaisesTheOrdinaryNoticeAlone_AndAKeyThatLeftThePoolIsKeptAsExposed()
    {
        using var world = new KeyWorld();
        var row = world.AddKey("A", "a");
        var road = RoadOf(world);

        road.Exposed(row.Id).Should().BeTrue();
        world.Admin.Remove(row.Id).Ok.Should().BeTrue();
        road.Exposed(row.Id).Should().BeTrue("a key that was removed while its session ran is still a key whose id is kept as exposed: that is a mark that was written");

        world.Notices.Should().ContainSingle("the second call changes nothing and says nothing more").Which.Title.Should().NotContain("still enabled");
    }
}

using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The aggregate cap (D6, plan gate 2026-10-12): the process counts the bytes its live snapshots hold, and a new one is
/// refused — with both numbers — past a ceiling or when the temp volume's free space would fall under a floor; a
/// finished snapshot gives its bytes back. The volume reader is a fake, so the floor is tested on any machine.
/// </summary>
public sealed class SnapshotBudgetTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _repo = null!;
    private TempDir _scratch = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-snap-repo-");
        _scratch = TempDir.For("coai-answers-snap-");
        await _repo.WriteAsync("hundred.txt", new string('h', 100));
        await _repo.CommitAsync("a hundred bytes");
    }

    public async ValueTask DisposeAsync()
    {
        _scratch.Dispose();
        await _repo.DisposeAsync();
    }

    private SnapshotSource.Pinned Pinned => new(_repo.Path);

    [Fact]
    public async Task ASecondSnapshotPastTheCeiling_IsRefusedWithBothNumbers()
    {
        var budget = new SnapshotBudget(ceilingBytes: 150, freeFloorBytes: 0, static _ => long.MaxValue / 2);
        var snapshots = SnapshotTestKit.Snapshots(_launcher, budget: budget);
        using var first = await SnapshotTestKit.MadeAsync(snapshots, Pinned, _scratch);
        budget.Held.Should().Be(100);

        var reason = await SnapshotTestKit.RefusedAsync(snapshots, Pinned, _scratch);

        reason.Should().Contain("100").And.Contain("150", "what is held and the ceiling are both named");
        budget.Held.Should().Be(100, "a refused snapshot holds nothing");
        Directory.GetDirectories(_scratch).Should().ContainSingle("the first snapshot, and no partial second");
    }

    [Fact]
    public async Task AFreeSpaceFloor_TheVolumeWouldFallUnder_Refuses()
    {
        var budget = new SnapshotBudget(ceilingBytes: long.MaxValue / 2, freeFloorBytes: 2_000, static _ => 2_050);
        var snapshots = SnapshotTestKit.Snapshots(_launcher, budget: budget);

        var reason = await SnapshotTestKit.RefusedAsync(snapshots, Pinned, _scratch);

        reason.Should().Contain("2,050").And.Contain("2,000", "the free space and the floor are both named");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    [Fact]
    public async Task AFreeSpaceReaderThatCannotAnswer_RefusesRatherThanGuessing()
    {
        var budget = new SnapshotBudget(long.MaxValue / 2, 0, static path => throw new IOException($"no such volume for {path}"));
        var snapshots = SnapshotTestKit.Snapshots(_launcher, budget: budget);

        var reason = await SnapshotTestKit.RefusedAsync(snapshots, Pinned, _scratch);

        reason.Should().Contain("no such volume", "an unmeasured volume is not a full one and not an empty one");
    }

    [Fact]
    public async Task AFinishedSnapshot_GivesItsBytesBack()
    {
        var budget = new SnapshotBudget(ceilingBytes: 150, freeFloorBytes: 0, static _ => long.MaxValue / 2);
        var snapshots = SnapshotTestKit.Snapshots(_launcher, budget: budget);
        var first = await SnapshotTestKit.MadeAsync(snapshots, Pinned, _scratch);
        first.Dispose();
        budget.Held.Should().Be(0);
        budget.Count.Should().Be(0);

        using var second = await SnapshotTestKit.MadeAsync(snapshots, Pinned, _scratch);

        budget.Held.Should().Be(100, "the room the first one took is free again");
    }

    [Fact]
    public async Task DisposingTwice_GivesTheBytesBackOnce()
    {
        var budget = SnapshotTestKit.RoomyBudget();
        var snapshot = await SnapshotTestKit.MadeAsync(SnapshotTestKit.Snapshots(_launcher, budget: budget), Pinned, _scratch);

        snapshot.Dispose();
        snapshot.Dispose();

        budget.Held.Should().Be(0);
        budget.Count.Should().Be(0);
    }

    [Fact]
    public void TheDefaults_AreTheOnesThePlanNames()
    {
        SnapshotBudget.DefaultCeilingBytes.Should().Be(2L * 1024 * 1024 * 1024);
        SnapshotBudget.DefaultFreeFloorBytes.Should().Be(5L * 1024 * 1024 * 1024);
        SnapshotOptions.DefaultMaxBytes.Should().Be(512L * 1024 * 1024);
        SnapshotOptions.DefaultMaxFiles.Should().Be(50_000);
        SnapshotOptions.DefaultMaxElapsed.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TheRealVolumeReader_AnswersForTheTempDirectory()
    {
        SnapshotBudget.FreeBytesOn(Path.GetTempPath()).Should().BeGreaterThan(0, "the machine running this has some room");
    }
}

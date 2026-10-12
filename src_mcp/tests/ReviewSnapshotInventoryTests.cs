using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The whole inventory is checked, not only each file (D6 as amended by the plan gate, codex, 2026-10-12): per-file
/// stamps let a LIVE copy publish a mix of times — a file copied early stays unchanged while a later one changes before
/// its own copy, or a file is added or removed while the copy runs. After the last file the live listing and every
/// copied file's stamp are taken again; a path added, removed or changed anywhere since the copy began retries the whole
/// copy once, then refuses — a make publishes nothing, a refresh keeps the old snapshot whole.
/// </summary>
/// <remarks>
/// The seam fires on <c>z.txt</c>, the last TRACKED file git lists (<c>a.txt</c>, <c>b.txt</c> come before it), so
/// whatever the seam does to the early files happens AFTER their own copy passed its per-file check — which is exactly
/// the gap. The mutation repeats on the retry, so the second attempt refuses rather than settles.
/// </remarks>
public sealed class ReviewSnapshotInventoryTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly SnapshotBudget _budget = SnapshotTestKit.RoomyBudget();
    private readonly List<int> _attempts = [];
    private TempGitRepo _repo = null!;
    private TempDir _scratch = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-snap-repo-");
        _scratch = TempDir.For("coai-answers-snap-");
        await Write("a.txt", "a, copied first");
        await Write("b.txt", "b, copied second");
        await Write("z.txt", "z, copied last");
        await _repo.CommitAsync("three files");
    }

    public async ValueTask DisposeAsync()
    {
        _scratch.Dispose();
        await _repo.DisposeAsync();
    }

    private SnapshotSource.Live Live => new(_repo.Path);

    private string At(string relative) => Path.Combine(_repo.Path, relative);

    private async Task Write(string relative, string text) => await File.WriteAllTextAsync(At(relative), text);

    /// <summary>The seam: on the last tracked file's copy, <paramref name="mutate"/> the tree — every attempt, unless gated by the switch file.</summary>
    private SnapshotOptions Mutating(Action<int> mutate, bool onlyWhenSwitched = false) =>
        SnapshotOptions.Default with
        {
            BeforeCopyOf = (attempt, path) =>
            {
                if (path != "z.txt" || (onlyWhenSwitched && !File.Exists(At("switch"))))
                {
                    return;
                }

                _attempts.Add(attempt);
                mutate(attempt);
            },
        };

    private Task<string> RefusedMakeAsync(SnapshotOptions options) =>
        SnapshotTestKit.RefusedAsync(SnapshotTestKit.Snapshots(_launcher, options, _budget), Live, _scratch);

    private async Task<(ReviewSnapshot Snapshot, string Reason)> RefusedRefreshAsync(SnapshotOptions options)
    {
        var snapshot = await SnapshotTestKit.MadeAsync(SnapshotTestKit.Snapshots(_launcher, options, _budget), Live, _scratch);
        await Write("switch", "the seam acts from here on");
        var outcome = await snapshot.RefreshAsync(Live, TestContext.Current.CancellationToken);

        return (snapshot, outcome.Should().BeOfType<SnapshotRefresh.Refused>("a tree that keeps moving is not refreshed").Which.Reason);
    }

    // ---------------------------------------------------------------------------------------
    // MakeAsync, live tree
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AFileChangedAfterItsOwnCopy_ButBeforeTheCopyEnds_RetriesOnce_ThenRefusesTheMake()
    {
        var reason = await RefusedMakeAsync(Mutating(attempt => File.AppendAllText(At("a.txt"), $" +{attempt}")));

        reason.Should().Contain("a.txt", "the early file is what moved");
        _attempts.Should().Equal([1, 2], "one retry, then the refusal");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    [Fact]
    public async Task AFileAddedDuringTheCopy_RetriesOnce_ThenRefusesTheMake()
    {
        var reason = await RefusedMakeAsync(Mutating(attempt => File.WriteAllText(At($"added-{attempt}.txt"), "arrived mid-copy")));

        reason.Should().Contain("added-2.txt", "the retry's plan held added-1.txt; added-2.txt arrived under it");
        _attempts.Should().Equal(1, 2);
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    [Fact]
    public async Task AFileRemovedDuringTheCopy_RetriesOnce_ThenRefusesTheMake()
    {
        var reason = await RefusedMakeAsync(Mutating(attempt => File.Delete(At(attempt == 1 ? "a.txt" : "b.txt"))));

        reason.Should().Contain("b.txt", "a.txt went on the first attempt, b.txt on the retry");
        _attempts.Should().Equal(1, 2);
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    // ---------------------------------------------------------------------------------------
    // RefreshAsync, the old snapshot kept whole
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AFileChangedAfterItsOwnCopy_ButBeforeTheCopyEnds_RetriesOnce_ThenRefusesTheRefresh()
    {
        var (snapshot, reason) = await RefusedRefreshAsync(Mutating(attempt => File.AppendAllText(At("a.txt"), $" +{attempt}"), onlyWhenSwitched: true));
        using var _ = snapshot;

        reason.Should().Contain("a.txt");
        _attempts.Should().Equal(1, 2);
        SnapshotTestKit.TextAt(snapshot, "a.txt").Should().Be("a, copied first", "the previous copy stands");
        Directory.GetDirectories(_scratch).Should().ContainSingle("nothing beside the root");
    }

    [Fact]
    public async Task AFileAddedDuringTheCopy_RetriesOnce_ThenRefusesTheRefresh()
    {
        var (snapshot, reason) = await RefusedRefreshAsync(Mutating(attempt => File.WriteAllText(At($"added-{attempt}.txt"), "arrived mid-copy"), onlyWhenSwitched: true));
        using var _ = snapshot;
        var before = snapshot.Manifest;

        reason.Should().Contain("added-2.txt");
        _attempts.Should().Equal(1, 2);
        snapshot.Manifest.Should().BeSameAs(before);
        SnapshotTestKit.Has(snapshot, "added-1.txt").Should().BeFalse("nothing of the refused copy landed");
        Directory.GetDirectories(_scratch).Should().ContainSingle();
    }

    [Fact]
    public async Task AFileRemovedDuringTheCopy_RetriesOnce_ThenRefusesTheRefresh()
    {
        var (snapshot, reason) = await RefusedRefreshAsync(Mutating(attempt => File.Delete(At(attempt == 1 ? "a.txt" : "b.txt")), onlyWhenSwitched: true));
        using var _ = snapshot;

        reason.Should().Contain("b.txt");
        _attempts.Should().Equal(1, 2);
        SnapshotTestKit.TextAt(snapshot, "a.txt").Should().Be("a, copied first", "the previous copy, a.txt included, stands");
        SnapshotTestKit.TextAt(snapshot, "b.txt").Should().Be("b, copied second");
        Directory.GetDirectories(_scratch).Should().ContainSingle();
    }
}

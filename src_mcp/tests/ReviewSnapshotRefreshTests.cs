using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A conversation keeps ONE snapshot path (D6 as amended 2026-10-12): a resumed Vibe session stays bound to the folder
/// it was created in — a read in a new <c>--workdir</c> was <c>tool_denied</c> — while the same path re-filled in place
/// was read afresh 3 of 3 (research/RESULTS_vibe_confinement.md §22, B/D/E). So the live tree is copied INTO the
/// existing root again before each turn, and a refused refresh leaves the previous snapshot whole.
/// </summary>
public sealed class ReviewSnapshotRefreshTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly SnapshotBudget _budget = SnapshotTestKit.RoomyBudget();
    private TempGitRepo _repo = null!;
    private TempDir _scratch = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-snap-repo-");
        _scratch = TempDir.For("coai-answers-snap-");
        await Write("kept.txt", "kept");
        await Write("changing.txt", "turn 1");
        await Write("going.txt", "present at turn 1");
        await _repo.CommitAsync("turn 1");
    }

    public async ValueTask DisposeAsync()
    {
        _scratch.Dispose();
        await _repo.DisposeAsync();
    }

    private SnapshotSource.Live Live => new(_repo.Path);

    private Task<ReviewSnapshot> MadeAsync(SnapshotOptions? options = null) =>
        SnapshotTestKit.MadeAsync(SnapshotTestKit.Snapshots(_launcher, options, _budget), Live, _scratch);

    private async Task Write(string relative, string text)
    {
        var full = Path.Combine(_repo.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, text);
    }

    private static async Task<SnapshotManifest> RefreshedAsync(ReviewSnapshot snapshot, SnapshotSource.Live source)
    {
        var outcome = await snapshot.RefreshAsync(source, TestContext.Current.CancellationToken);

        return outcome switch
        {
            SnapshotRefresh.Refreshed refreshed => refreshed.Manifest,
            SnapshotRefresh.Refused refused => throw new Xunit.Sdk.XunitException($"the refresh was refused: {refused.Reason}"),
            _ => throw new Xunit.Sdk.XunitException("the union is closed"),
        };
    }

    private static async Task<string> RefusedAsync(ReviewSnapshot snapshot, SnapshotSource.Live source)
    {
        var outcome = await snapshot.RefreshAsync(source, TestContext.Current.CancellationToken);

        return outcome.Should().BeOfType<SnapshotRefresh.Refused>("a refresh over a tree that cannot be copied refuses").Which.Reason;
    }

    [Fact]
    public async Task ARefresh_WritesNewAndChangedFiles_RemovesDeleted_AndKeepsTheRootPath()
    {
        using var snapshot = await MadeAsync();
        var root = snapshot.Root;
        await Write("changing.txt", "turn 2");
        await Write("new.txt", "arrived at turn 2");
        await Write("dir/new-deep.txt", "deep");
        File.Delete(Path.Combine(_repo.Path, "going.txt"));

        var manifest = await RefreshedAsync(snapshot, Live);

        snapshot.Root.Should().Be(root, "a resumed session is bound to the folder it was created in");
        SnapshotTestKit.TextAt(snapshot, "changing.txt").Should().Be("turn 2");
        SnapshotTestKit.TextAt(snapshot, "new.txt").Should().Be("arrived at turn 2");
        SnapshotTestKit.TextAt(snapshot, "dir/new-deep.txt").Should().Be("deep");
        SnapshotTestKit.TextAt(snapshot, "kept.txt").Should().Be("kept");
        SnapshotTestKit.Has(snapshot, "going.txt").Should().BeFalse("what the live tree no longer has is removed");
        snapshot.Manifest.Should().BeSameAs(manifest, "the manifest is replaced");
        manifest.Files.Should().BeEquivalentTo(["kept.txt", "changing.txt", "new.txt", "dir/new-deep.txt"]);
        Directory.GetDirectories(_scratch).Should().ContainSingle("the new tree was swapped in, and nothing was left beside the root");
    }

    [Fact]
    public async Task ARefresh_StillExcludes_AndStillMapsBack()
    {
        using var snapshot = await MadeAsync();
        await Write("AGENTS.md", "orders arriving at turn 2");
        await Write("nested/.vibe/hooks.toml", "hook arriving at turn 2");

        var manifest = await RefreshedAsync(snapshot, Live);

        SnapshotTestKit.Has(snapshot, "AGENTS.md").Should().BeFalse();
        SnapshotTestKit.Has(snapshot, "nested/.vibe/hooks.toml").Should().BeFalse();
        manifest.Excluded.Should().BeEquivalentTo(["AGENTS.md", "nested/.vibe/hooks.toml"]);
        snapshot.MapBack(Path.Combine(snapshot.Root, "kept.txt")).Should().Be("kept.txt");
    }

    [Fact]
    public async Task ARefusedRefresh_LeavesTheOldSnapshotReadable_AndItsManifestUnchanged()
    {
        using var snapshot = await MadeAsync();
        var before = snapshot.Manifest;
        await Write("changing.txt", "turn 2 — must not land");
        using var outside = TempDir.For("coai-snap-outside-");
        // A file behind the link: git lists through a junction as through a directory, and lists nothing under an empty
        // one — an empty link is never copied through and therefore never refused.
        await File.WriteAllTextAsync(outside.At("marker.txt"), "OUTSIDE");
        await DirectoryLink.MakeAsync(_launcher, Path.Combine(_repo.Path, "jdir"), outside);
        try
        {
            var reason = await RefusedAsync(snapshot, Live);

            reason.Should().Contain("jdir");
        }
        finally
        {
            DirectoryLink.Remove(Path.Combine(_repo.Path, "jdir"));
        }

        snapshot.Manifest.Should().BeSameAs(before, "a refused refresh replaces nothing");
        SnapshotTestKit.TextAt(snapshot, "changing.txt").Should().Be("turn 1", "the previous bytes stay");
        SnapshotTestKit.TextAt(snapshot, "going.txt").Should().Be("present at turn 1");
        snapshot.Read(Path.Combine(snapshot.Root, "kept.txt")).Should().BeOfType<SnapshotRead.Served>();
        Directory.GetDirectories(_scratch).Should().ContainSingle("no partial copy and no moved-aside copy is left");
    }

    [Fact]
    public async Task ARefreshOverACeiling_RefusesWithTheNumber_AndLeavesTheOld()
    {
        using var snapshot = await MadeAsync(SnapshotOptions.Default with { MaxFiles = 3 });
        await Write("fourth.txt", "one too many");

        var reason = await RefusedAsync(snapshot, Live);

        reason.Should().Contain("4").And.Contain("3");
        snapshot.Manifest.Files.Should().HaveCount(3);
        SnapshotTestKit.Has(snapshot, "fourth.txt").Should().BeFalse();
    }

    [Fact]
    public async Task ARefreshOverATreeThatKeepsChanging_IsRefusedAfterOneRetry_AndLeavesTheOld()
    {
        using var snapshot = await MadeAsync(SnapshotOptions.Default with
        {
            BeforeCopyOf = (attempt, path) =>
            {
                if (attempt > 0 && path == "changing.txt" && File.Exists(Path.Combine(_repo.Path, "moving")))
                {
                    File.AppendAllText(Path.Combine(_repo.Path, path), $" +{attempt}");
                }
            },
        });
        await Write("moving", "the switch the hook reads: only the refresh moves");

        var reason = await RefusedAsync(snapshot, Live);

        reason.Should().Contain("changing.txt");
        SnapshotTestKit.TextAt(snapshot, "changing.txt").Should().Be("turn 1");
    }

    [Fact]
    public async Task ARefresh_CountsTheRefreshedSize_NotOldPlusNew()
    {
        using var snapshot = await MadeAsync();
        var oldBytes = snapshot.Manifest.Bytes;
        _budget.Held.Should().Be(oldBytes);
        await Write("bigger.txt", new string('b', 500));

        var manifest = await RefreshedAsync(snapshot, Live);

        manifest.Bytes.Should().Be(oldBytes + 500);
        _budget.Held.Should().Be(manifest.Bytes, "the old copy's bytes are given back when the new one takes its place");
        _budget.Count.Should().Be(1);
        snapshot.Dispose();
        _budget.Held.Should().Be(0);
    }

    [Fact]
    public async Task ARefreshThatWouldPassTheCeiling_CountsTheReplacedBytes_Once()
    {
        // The ceiling admits the refreshed tree but not old + new: the arithmetic must subtract what is replaced.
        var budget = new SnapshotBudget(ceilingBytes: 60, freeFloorBytes: 0, static _ => long.MaxValue / 2);
        var snapshots = SnapshotTestKit.Snapshots(_launcher, budget: budget);
        using var snapshot = await SnapshotTestKit.MadeAsync(snapshots, Live, _scratch);
        snapshot.Manifest.Bytes.Should().BeLessThan(60);
        await Write("more.txt", new string('m', 60 - (int)snapshot.Manifest.Bytes - 1));

        var manifest = await RefreshedAsync(snapshot, Live);

        manifest.Bytes.Should().Be(59);
        budget.Held.Should().Be(59);
    }

    [Fact]
    public async Task ADisposedSnapshot_RefusesARefresh()
    {
        var snapshot = await MadeAsync();
        snapshot.Dispose();

        var outcome = await snapshot.RefreshAsync(Live, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<SnapshotRefresh.Refused>();
    }

    /// <summary>
    /// Finding 2 of the S1 review: a dispose racing a refresh deleted the root under the copy, disposed the semaphore the
    /// refresh still held (an exception out of its <c>finally</c>), and could leave the swapped-in tree and its bytes.
    /// </summary>
    [Fact]
    public async Task ADisposeDuringARefresh_EscapesNothing_HoldsNoBytes_AndLeavesNoDirectory()
    {
        var snapshot = await MadeAsync(SnapshotOptions.Default with
        {
            BeforeCopyOf = (_, _) =>
            {
                if (File.Exists(Path.Combine(_repo.Path, "slow")))
                {
                    Thread.Sleep(150);
                }
            },
        });
        await Write("slow", "the switch the hook reads: only the refresh is slow");

        var refresh = Task.Run(() => snapshot.RefreshAsync(Live, TestContext.Current.CancellationToken));
        await Task.Delay(60, TestContext.Current.CancellationToken);
        var dispose = Task.Run(snapshot.Dispose);
        var both = async () => await Task.WhenAll(refresh, dispose);

        await both.Should().NotThrowAsync("neither side may throw at the other");
        _budget.Held.Should().Be(0, "whichever won, nothing is held once both are done");
        _budget.Count.Should().Be(0);
        Directory.GetDirectories(_scratch).Should().BeEmpty("no root, no partial, no moved-aside copy");
    }

    // ---------- one path across servers: the conversation's root is a NAME, not only an object (S3) ----------

    /// <summary>
    /// A conversation's snapshot root is fixed by the conversation (<c>coai-vibe/&lt;id&gt;/tree</c>), so a server that starts
    /// again finds the tree its predecessor left there: <c>MakeAtAsync</c> builds the new copy COMPLETE beside it and swaps
    /// it in under the same path — the resumed session's folder (§22 B/E) — or, refused, leaves the old tree whole.
    /// </summary>
    [Fact]
    public async Task MakeAt_PublishesAtTheGivenRoot_AndOverATreeLeftThere_ReplacesItWhole()
    {
        var root = _scratch.At("conversation", "tree");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "left-by-the-last-server.txt"), "stale", TestContext.Current.CancellationToken);
        var made = await SnapshotTestKit.Snapshots(_launcher, null, _budget).MakeAtAsync(Live, root, TestContext.Current.CancellationToken);

        using var snapshot = made.Should().BeOfType<SnapshotOutcome.Made>().Which.Snapshot;
        snapshot.Root.Should().Be(root, "the path is the conversation's, whichever server made the tree");
        SnapshotTestKit.TextAt(snapshot, "changing.txt").Should().Be("turn 1");
        File.Exists(Path.Combine(root, "left-by-the-last-server.txt")).Should().BeFalse("the tree is the live one, whole — never a mix");
        Directory.GetDirectories(_scratch.At("conversation")).Should().ContainSingle("no partial and no moved-aside copy is left beside it");

        await Write("changing.txt", "turn 2");
        (await RefreshedAsync(snapshot, Live)).Holds("changing.txt").Should().BeTrue();
        SnapshotTestKit.TextAt(snapshot, "changing.txt").Should().Be("turn 2", "and it refreshes in place like any snapshot");
    }

    [Fact]
    public async Task MakeAt_ThatIsRefused_LeavesTheTreeAlreadyThere()
    {
        var root = _scratch.At("conversation", "tree");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "previous.txt"), "the previous turn's", TestContext.Current.CancellationToken);

        var made = await SnapshotTestKit.Snapshots(_launcher, SnapshotOptions.Default with { MaxFiles = 1 }, _budget)
            .MakeAtAsync(Live, root, TestContext.Current.CancellationToken);

        made.Should().BeOfType<SnapshotOutcome.Refused>().Which.Reason.Should().Contain("1");
        File.ReadAllText(Path.Combine(root, "previous.txt")).Should().Be("the previous turn's", "a refusal publishes nothing");
        _budget.Held.Should().Be(0);
    }

    /// <summary>
    /// R3 of the S3 risk consultation: the install rename AND the rollback can both fail, and the refusal said "the previous
    /// snapshot stands" over a root that held nothing. The sentence is now true in every branch — and the stable path
    /// recovers: the next <c>MakeAtAsync</c> publishes a fresh copy at it.
    /// </summary>
    [Fact]
    public async Task ARefreshWhoseInstallAndRollbackBothFail_SaysTheSnapshotIsMissing_AndMakeAtRecoversThePath()
    {
        var maker = SnapshotTestKit.Snapshots(_launcher, null, _budget);
        using var snapshot = await SnapshotTestKit.MadeAsync(maker, Live, _scratch);
        var root = snapshot.Root;
        // The first rename (the old tree aside) succeeds; every rename INTO the root fails — the install and the rollback.
        maker.Mover = (string from, string to, out string why) =>
        {
            if (string.Equals(to, root, StringComparison.OrdinalIgnoreCase))
            {
                why = "the test refuses every rename into the root";
                return false;
            }

            Directory.Move(from, to);
            why = string.Empty;
            return true;
        };
        await Write("changing.txt", "turn 2");

        var refused = await RefusedAsync(snapshot, Live);

        Directory.Exists(root).Should().BeFalse("the fixture took the old tree aside and let nothing back in");
        refused.Should().Contain("no snapshot at").And.NotContain("the previous snapshot stands",
            "a refusal must not say the old tree stands over a root that holds nothing");
        var again = await SnapshotTestKit.Snapshots(_launcher, null, _budget).MakeAtAsync(Live, root, TestContext.Current.CancellationToken);
        using var recovered = again.Should().BeOfType<SnapshotOutcome.Made>("a restart's MakeAtAsync recovers the conversation's path").Which.Snapshot;
        recovered.Root.Should().Be(root);
        SnapshotTestKit.TextAt(recovered, "changing.txt").Should().Be("turn 2");
    }

    /// <summary>
    /// R2: a snapshot is never made under a parent that is a link — a conversation's <c>&lt;id&gt;</c> folder redirected to
    /// somewhere else before <c>MakeAtAsync</c> runs is refused, and nothing is written where the link points.
    /// </summary>
    [Fact]
    public async Task MakeAt_UnderAParentThatIsALink_IsRefused_AndWritesNothingThere()
    {
        var elsewhere = Directory.CreateDirectory(_scratch.At("elsewhere")).FullName;
        var parent = _scratch.At("conversation");
        await DirectoryLink.MakeAsync(_launcher, parent, elsewhere);

        var made = await SnapshotTestKit.Snapshots(_launcher, null, _budget)
            .MakeAtAsync(Live, Path.Combine(parent, "tree"), TestContext.Current.CancellationToken);

        made.Should().BeOfType<SnapshotOutcome.Refused>().Which.Reason.Should().Contain("is a link");
        Directory.EnumerateFileSystemEntries(elsewhere).Should().BeEmpty("nothing is written through the link");
        DirectoryLink.Remove(parent);
    }
}

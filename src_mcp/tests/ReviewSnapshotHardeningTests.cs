using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the independent review of S1 found (2026-10-12), each pinned before it was fixed: a name Windows would
/// normalise INTO an excluded one, a directory that becomes a link between the plan and the copy, an untracked nested
/// repository, and the manifest's lookup and equality.
/// </summary>
public sealed class ReviewSnapshotHardeningTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _repo = null!;
    private TempDir _scratch = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-snap-repo-");
        _scratch = TempDir.For("coai-answers-snap-");
    }

    public async ValueTask DisposeAsync()
    {
        _scratch.Dispose();
        await _repo.DisposeAsync();
    }

    private async Task WriteAsync(string relative, string text)
    {
        var full = Path.Combine(_repo.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, text);
    }

    private async Task<string> BlobAsync(string text)
    {
        var made = await _launcher.RunAsync(new ProcessRequest("git", ["hash-object", "-w", "--stdin"], _repo.Path) { StdIn = text });
        made.ExitCode.Should().Be(0, made.StdErr);

        return made.StdOut.Trim();
    }

    private Task CommitTheIndexAsync(string message) =>
        _repo.GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-m", message);

    /// <summary>A directory link made synchronously — the copier's test seam is synchronous.</summary>
    private static void LinkNow(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var start = new ProcessStartInfo("cmd") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(arg);
        }

        using var made = Process.Start(start)!;
        made.WaitForExit();
        made.ExitCode.Should().Be(0, "a junction is how this test swaps the directory");
    }

    // ---------------------------------------------------------------------------------------
    // Finding 1 — a name Windows normalises into an excluded one
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(".vibe./config.toml")]
    [InlineData(".vibe /x")]
    [InlineData("AGENTS.md.")]
    [InlineData("AGENTS.md ")]
    [InlineData(".agents./skills/x")]
    [InlineData("nul")]
    [InlineData("CON.txt")]
    [InlineData("com1")]
    [InlineData("LPT9.log")]
    [InlineData("docs/aux/readme.md")]
    public void AComponentWindowsWouldNormalise_OrADeviceName_IsRefusedBeforeAnythingIsCopied(string path)
    {
        // LIVE, deliberately: in pinned mode the planner refused these already — as files "not there", the wrong reason,
        // which is how the first run of this theory went green against the defect. A live tree skips a missing file, so
        // only the name rule itself can refuse here.
        var planned = SnapshotPlanner.Plan(
            _repo.Path, _scratch.At("dest"), [new SnapshotEntry(path, EntryKind.File)], [], SnapshotOptions.Default, live: true, StringComparison.OrdinalIgnoreCase);

        planned.Should().BeOfType<Planning.Refused>($"'{path}' is a name Windows would strip or a device").Which.Reason.Should().Contain(path);
        Directory.Exists(_scratch.At("dest")).Should().BeFalse();
    }

    /// <summary>
    /// The end-to-end shape of the bypass: the index names <c>.vibe./config.toml</c> (admitted with
    /// <c>core.protectNTFS=false</c>, as a repository prepared on another OS carries it), the disk holds
    /// <c>.vibe/config.toml</c>, and a Windows copy of the former lands as the latter — inside the snapshot, under the
    /// excluded name.
    /// </summary>
    [Fact]
    public async Task ATrackedNameWindowsWouldFoldIntoAnExcludedOne_IsRefused_NeverCopiedUnderTheExclusion()
    {
        await WriteAsync(".vibe/config.toml", "a hook the branch wrote");
        await WriteAsync("keep.txt", "kept");
        var blob = await BlobAsync("a hook the branch wrote");
        await _repo.GitAsync("add", "keep.txt");
        await _repo.GitAsync("-c", "core.protectNTFS=false", "update-index", "--add", "--cacheinfo", $"100644,{blob},.vibe./config.toml");
        await CommitTheIndexAsync("a normalising name");

        var reason = await SnapshotTestKit.RefusedAsync(SnapshotTestKit.Snapshots(_launcher), new SnapshotSource.Pinned(_repo.Path), _scratch);

        reason.Should().Contain(".vibe.");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    // ---------------------------------------------------------------------------------------
    // Finding 3a — a link that appears between the plan and the copy
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ADirectoryThatBecomesALinkBetweenThePlanAndTheCopy_IsRefused_AndNoOutsideBytesLand()
    {
        await WriteAsync("dir/x.txt", "inside!!");
        await _repo.CommitAsync("base");
        using var outside = TempDir.For("coai-snap-outside-");
        // The same length and the same last-write time as the planned file, so the stamp check alone cannot see the swap.
        await File.WriteAllTextAsync(outside.At("x.txt"), "OUTSIDE!");
        File.SetLastWriteTimeUtc(outside.At("x.txt"), File.GetLastWriteTimeUtc(Path.Combine(_repo.Path, "dir", "x.txt")));
        var dir = Path.Combine(_repo.Path, "dir");
        var options = SnapshotOptions.Default with
        {
            BeforeCopyOf = (attempt, path) =>
            {
                if (attempt == 1 && path == "dir/x.txt")
                {
                    Directory.Delete(dir, recursive: true);
                    LinkNow(dir, outside);
                }
            },
        };
        try
        {
            var reason = await SnapshotTestKit.RefusedAsync(SnapshotTestKit.Snapshots(_launcher, options), new SnapshotSource.Pinned(_repo.Path), _scratch);

            reason.Should().Contain("dir");
            SnapshotTestKit.NothingPublishedUnder(_scratch);
        }
        finally
        {
            DirectoryLink.Remove(dir);
        }

        File.ReadAllText(outside.At("x.txt")).Should().Be("OUTSIDE!", "nothing touched the target");
    }

    // ---------------------------------------------------------------------------------------
    // Finding 4 — an untracked nested repository
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AnUntrackedNestedRepository_IsListedUnavailable_NotCopied_AndNotRefused()
    {
        await WriteAsync("keep.txt", "kept");
        await _repo.CommitAsync("base");
        var nested = Path.Combine(_repo.Path, "nested");
        Directory.CreateDirectory(nested);
        var inner = await _launcher.RunAsync(new ProcessRequest("git", ["init", "-q", "-b", "main", "."], nested));
        inner.ExitCode.Should().Be(0, inner.StdErr);
        await File.WriteAllTextAsync(Path.Combine(nested, "f.txt"), "in the nested repository");

        using var snapshot = await SnapshotTestKit.MadeAsync(SnapshotTestKit.Snapshots(_launcher), new SnapshotSource.Live(_repo.Path), _scratch);

        snapshot.Manifest.Files.Should().BeEquivalentTo(["keep.txt"]);
        SnapshotTestKit.Has(snapshot, "nested/f.txt").Should().BeFalse("git lists a nested repository as a directory, and nothing inside it");
        snapshot.Manifest.Unavailable.Should().ContainSingle(u => u.Path == "nested").Which.Why.Should().Contain("nested repository");
    }

    // ---------------------------------------------------------------------------------------
    // Finding 5 — the manifest
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheManifest_HoldsOrdinally_AndComparesByValue()
    {
        var manifest = new SnapshotManifest(["a.txt", "dir/b.txt"], ["AGENTS.md"], [new SnapshotUnavailable("sub", "unpopulated")], 3, false);

        manifest.Holds("dir/b.txt").Should().BeTrue();
        manifest.Holds("DIR/b.txt").Should().BeFalse("ordinal");
        manifest.Holds("c.txt").Should().BeFalse();
        new SnapshotManifest(["a.txt", "dir/b.txt"], ["AGENTS.md"], [new SnapshotUnavailable("sub", "unpopulated")], 3, false)
            .Should().Be(manifest, "a record's equality is by value, lists included");
    }
}

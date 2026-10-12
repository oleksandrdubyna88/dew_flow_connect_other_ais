using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The sanitised snapshot's copy contracts (D6 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>, S1): what is left
/// out, what refuses, what is listed unavailable, the ceilings, and the live tree's two modes — against REAL git
/// in temp repositories, because every rule here is about what git lists and what the disk holds.
/// </summary>
public sealed class ReviewSnapshotTests : IAsyncLifetime
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

    private SnapshotSource.Pinned Pinned => new(_repo.Path);

    private SnapshotSource.Live Live => new(_repo.Path);

    private Task<ReviewSnapshot> MadeAsync(SnapshotSource source, SnapshotOptions? options = null) =>
        SnapshotTestKit.MadeAsync(SnapshotTestKit.Snapshots(_launcher, options), source, _scratch);

    private Task<string> RefusedAsync(SnapshotSource source, SnapshotOptions? options = null) =>
        SnapshotTestKit.RefusedAsync(SnapshotTestKit.Snapshots(_launcher, options), source, _scratch);

    private async Task WriteAsync(string relative, string text)
    {
        var full = Path.Combine(_repo.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, text);
    }

    /// <summary>A blob in the object store, for an index entry written by hand.</summary>
    private async Task<string> BlobAsync(string text)
    {
        var made = await _launcher.RunAsync(new ProcessRequest("git", ["hash-object", "-w", "--stdin"], _repo.Path) { StdIn = text });
        made.ExitCode.Should().Be(0, made.StdErr);

        return made.StdOut.Trim();
    }

    /// <summary>Commits the index AS IT IS — <c>TempGitRepo.CommitAsync</c> adds <c>-A</c> first, which drops a hand-written entry with no file behind it.</summary>
    private Task CommitTheIndexAsync(string message) =>
        _repo.GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-m", message);

    // ---------------------------------------------------------------------------------------
    // Exclusions
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TheExcludedNames_AreAbsentAtEveryDepth_AndNamedInTheManifest()
    {
        await WriteAsync(".vibe/hooks.toml", "hook");
        await WriteAsync("nested/deep/.vibe/config.toml", "hook");
        await WriteAsync(".agents/skills/one.md", "skill");
        await WriteAsync("x/.agents/skills/two.md", "skill");
        await WriteAsync(".agents/PROJECT.md", "kept: not a skill");
        await WriteAsync(".kimi-code/config.toml", "kimi");
        await WriteAsync("AGENTS.md", "orders");
        await WriteAsync("sub/dir/agents.md", "orders");
        await WriteAsync("keep.txt", "kept");
        await _repo.CommitAsync("the tree under review");

        using var snapshot = await MadeAsync(Pinned);

        foreach (var gone in new[] { ".vibe/hooks.toml", "nested/deep/.vibe/config.toml", ".agents/skills/one.md", "x/.agents/skills/two.md", ".kimi-code/config.toml", "AGENTS.md", "sub/dir/agents.md", ".git" })
        {
            SnapshotTestKit.Has(snapshot, gone).Should().BeFalse($"{gone} is excluded at every depth");
        }

        SnapshotTestKit.TextAt(snapshot, "keep.txt").Should().Be("kept");
        SnapshotTestKit.TextAt(snapshot, ".agents/PROJECT.md").Should().Be("kept: not a skill", "only .agents/skills is excluded, not .agents");
        snapshot.Manifest.Excluded.Should().BeEquivalentTo(
            [".vibe/hooks.toml", "nested/deep/.vibe/config.toml", ".agents/skills/one.md", "x/.agents/skills/two.md", ".kimi-code/config.toml", "AGENTS.md", "sub/dir/agents.md"],
            "an excluded path is named, never silently lost");
        snapshot.Manifest.Files.Should().BeEquivalentTo(["keep.txt", ".agents/PROJECT.md"]);
    }

    [Fact]
    public async Task TheExcludedNames_AreMatchedByTheDestinationsCaseRule()
    {
        await WriteAsync("nested/Agents.MD", "orders in another case");
        await WriteAsync(".Vibe/x.toml", "hook in another case");
        await WriteAsync("keep.txt", "kept");
        await _repo.CommitAsync("case");

        using var snapshot = await MadeAsync(Pinned);

        var folded = snapshot.Manifest.CaseInsensitive;
        SnapshotTestKit.Has(snapshot, "nested/Agents.MD").Should().Be(!folded, "the exclusion matches as the destination filesystem compares names");
        SnapshotTestKit.Has(snapshot, ".Vibe/x.toml").Should().Be(!folded);
        folded.Should().Be(OperatingSystem.IsWindows() || DestinationCase.Probe(_scratch) == StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnExclusionProfile_IsAParameter()
    {
        await WriteAsync("AGENTS.md", "orders");
        await WriteAsync("private/secret.txt", "mine");
        await _repo.CommitAsync("profile");

        using var snapshot = await MadeAsync(Pinned, SnapshotOptions.Default with { Exclusions = new SnapshotExclusions(["private"]) });

        SnapshotTestKit.Has(snapshot, "private/secret.txt").Should().BeFalse();
        SnapshotTestKit.Has(snapshot, "AGENTS.md").Should().BeTrue("the profile given is the whole list");
    }

    // ---------------------------------------------------------------------------------------
    // Links, collisions, escapes
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ACommittedSymlink_RefusesNamingThePath()
    {
        await WriteAsync("keep.txt", "kept");
        var blob = await BlobAsync("keep.txt");
        // A 120000 entry written by hand, so the rule is tested on a machine whose git cannot make a link.
        await _repo.GitAsync("update-index", "--add", "--cacheinfo", $"120000,{blob},linked");
        await CommitTheIndexAsync("a link");

        var reason = await RefusedAsync(Pinned);

        reason.Should().Contain("linked").And.ContainEquivalentOf("link");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    [Fact]
    public async Task AFileSymlinkOnDisk_RefusesNamingThePath()
    {
        await WriteAsync("keep.txt", "kept");
        await _repo.CommitAsync("base");
        try
        {
            File.CreateSymbolicLink(Path.Combine(_repo.Path, "link.txt"), Path.Combine(_repo.Path, "keep.txt"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link without privilege: {e.Message}");
        }

        var reason = await RefusedAsync(Live);

        reason.Should().Contain("link.txt");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    /// <summary>A junction on Windows, a directory symlink elsewhere — the link nobody needs privilege for.</summary>
    [Fact]
    public async Task ADirectoryLinkInTheSource_RefusesNamingThePath_AndNeverFollowsIt()
    {
        await WriteAsync("keep.txt", "kept");
        await _repo.CommitAsync("base");
        using var outside = TempDir.For("coai-snap-outside-");
        await File.WriteAllTextAsync(outside.At("marker.txt"), "OUTSIDE");
        await DirectoryLink.MakeAsync(_launcher, Path.Combine(_repo.Path, "jdir"), outside);

        var reason = await RefusedAsync(Live);

        reason.Should().Contain("jdir");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
        DirectoryLink.Remove(Path.Combine(_repo.Path, "jdir"));
        File.Exists(outside.At("marker.txt")).Should().BeTrue("nothing walked through the link");
    }

    [Fact]
    public async Task TwoNamesThatCollideUnderTheDestinationsCaseRules_Refuse()
    {
        var upper = await BlobAsync("UPPER");
        var lower = await BlobAsync("lower");
        await _repo.GitAsync("update-index", "--add", "--cacheinfo", $"100644,{upper},README.md");
        await _repo.GitAsync("update-index", "--add", "--cacheinfo", $"100644,{lower},readme.md");
        await CommitTheIndexAsync("two spellings");
        var folded = DestinationCase.Probe(_scratch) == StringComparison.OrdinalIgnoreCase;
        if (!folded)
        {
            await WriteAsync("README.md", "UPPER");
            await WriteAsync("readme.md", "lower");
        }

        if (folded)
        {
            var reason = await RefusedAsync(Pinned);
            reason.Should().Contain("README.md").And.Contain("readme.md", "both names are named");
            SnapshotTestKit.NothingPublishedUnder(_scratch);
        }
        else
        {
            using var snapshot = await MadeAsync(Pinned);
            snapshot.Manifest.Files.Should().Contain(["README.md", "readme.md"], "a case-sensitive destination holds both");
        }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/windows/escape.txt")]
    [InlineData("a//b.txt")]
    public void APathThatWouldLeaveTheRoot_RefusesBeforeAnythingIsCopied(string path)
    {
        var planned = SnapshotPlanner.Plan(
            _repo.Path, _scratch.At("dest"), [new SnapshotEntry(path, EntryKind.File)], [], SnapshotOptions.Default, live: false, StringComparison.Ordinal);

        planned.Should().BeOfType<Planning.Refused>().Which.Reason.Should().Contain(path);
        Directory.Exists(_scratch.At("dest")).Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------
    // Ceilings
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TheFileCeiling_RefusesWithTheNumber_AndLeavesNoPartialCopy()
    {
        await WriteAsync("a.txt", "a");
        await WriteAsync("b.txt", "b");
        await WriteAsync("c.txt", "c");
        await _repo.CommitAsync("three");

        var reason = await RefusedAsync(Pinned, SnapshotOptions.Default with { MaxFiles = 2 });

        reason.Should().Contain("3").And.Contain("2", "the count and the ceiling are both named");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    [Fact]
    public async Task TheByteCeiling_RefusesWithTheNumber_AndLeavesNoPartialCopy()
    {
        await WriteAsync("big.txt", new string('x', 1000));
        await _repo.CommitAsync("a kilobyte");

        var reason = await RefusedAsync(Pinned, SnapshotOptions.Default with { MaxBytes = 100 });

        reason.Should().Contain("1,000").And.Contain("100");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    [Fact]
    public async Task TheTimeCeiling_RefusesWithTheNumber_AndLeavesNoPartialCopy()
    {
        await WriteAsync("a.txt", "a");
        await WriteAsync("b.txt", "b");
        await _repo.CommitAsync("two");
        var options = SnapshotOptions.Default with
        {
            MaxElapsed = TimeSpan.FromMilliseconds(1),
            BeforeCopyOf = static (_, _) => Thread.Sleep(50),
        };

        var reason = await RefusedAsync(Pinned, options);

        reason.Should().Contain("0.001 s", "the ceiling is named").And.ContainEquivalentOf("took");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    // ---------------------------------------------------------------------------------------
    // Submodules, LFS, attributes
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task APopulatedSubmodule_IsCopied_AndAnUnpopulatedOne_IsListedUnavailable()
    {
        await using var sub = await TempGitRepo.InitAsync(_launcher, "coai-snap-sub-");
        await sub.WriteAsync("inner.txt", "from the submodule");
        await sub.CommitAsync("sub");
        await WriteAsync("keep.txt", "kept");
        await _repo.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", sub.Path.Replace('\\', '/'), "mount");
        await _repo.CommitAsync("with a submodule");

        using (var populated = await MadeAsync(Pinned))
        {
            SnapshotTestKit.TextAt(populated, "mount/inner.txt").Should().Be("from the submodule");
            populated.Manifest.Files.Should().Contain("mount/inner.txt");
            populated.Manifest.Unavailable.Should().BeEmpty();
            SnapshotTestKit.Has(populated, "mount/.git").Should().BeFalse("the submodule's own .git is excluded like any other");
        }

        // A clone without --recurse-submodules: the mount is an empty directory, the gitlink unpopulated.
        using var clones = TempDir.For("coai-snap-clone-");
        var clone = clones.At("clone");
        var cloned = await _launcher.RunAsync(new ProcessRequest("git", ["clone", "-q", _repo.Path, clone], clones));
        cloned.ExitCode.Should().Be(0, cloned.StdErr);

        using var unpopulated = await SnapshotTestKit.MadeAsync(SnapshotTestKit.Snapshots(_launcher), new SnapshotSource.Pinned(clone), _scratch);

        SnapshotTestKit.Has(unpopulated, "mount/inner.txt").Should().BeFalse();
        unpopulated.Manifest.Unavailable.Should().ContainSingle(u => u.Path == "mount").Which.Why.Should().ContainEquivalentOf("submodule");
    }

    [Fact]
    public async Task AnLfsPointerFile_IsListedUnavailable_NotCopiedAsSource()
    {
        const string pointer = "version https://git-lfs.github.com/spec/v1\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\nsize 12345\n";
        await WriteAsync("model.bin", pointer);
        await WriteAsync("keep.txt", "kept");
        await _repo.CommitAsync("an lfs pointer");

        using var snapshot = await MadeAsync(Pinned);

        SnapshotTestKit.Has(snapshot, "model.bin").Should().BeFalse("a pointer is not the file");
        snapshot.Manifest.Files.Should().NotContain("model.bin");
        snapshot.Manifest.Unavailable.Should().ContainSingle(u => u.Path == "model.bin").Which.Why.Should().Contain("LFS");
    }

    [Fact]
    public async Task AnExportIgnoredOrSubstitutedFile_KeepsItsReviewedBytes()
    {
        await WriteAsync(".gitattributes", "secret.txt export-ignore\nstamp.txt export-subst\n");
        await WriteAsync("secret.txt", "dropped by git archive, kept here");
        await WriteAsync("stamp.txt", "$Format:%H$");
        await _repo.CommitAsync("attributes");

        using var snapshot = await MadeAsync(Pinned);

        SnapshotTestKit.TextAt(snapshot, "secret.txt").Should().Be("dropped by git archive, kept here");
        SnapshotTestKit.TextAt(snapshot, "stamp.txt").Should().Be("$Format:%H$", "a copy, never an archive");
    }

    // ---------------------------------------------------------------------------------------
    // The live tree
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TheLiveTree_CopiesUntrackedNotIgnored_CurrentBytes_AndOmitsDeleted()
    {
        await WriteAsync(".gitignore", "ignored.txt\nbuild/\n");
        await WriteAsync("a.txt", "committed");
        await WriteAsync("b.txt", "to be deleted");
        await _repo.CommitAsync("base");
        await WriteAsync("a.txt", "modified, not staged");
        File.Delete(Path.Combine(_repo.Path, "b.txt"));
        await WriteAsync("untracked.txt", "new");
        await WriteAsync("dir/untracked2.txt", "new in a dir");
        await WriteAsync("ignored.txt", "ignored");
        await WriteAsync("build/out.bin", "ignored dir");

        using var snapshot = await MadeAsync(Live);

        SnapshotTestKit.TextAt(snapshot, "a.txt").Should().Be("modified, not staged", "the live bytes, not HEAD's");
        SnapshotTestKit.Has(snapshot, "b.txt").Should().BeFalse("a deleted path is absent");
        SnapshotTestKit.TextAt(snapshot, "untracked.txt").Should().Be("new");
        SnapshotTestKit.TextAt(snapshot, "dir/untracked2.txt").Should().Be("new in a dir");
        SnapshotTestKit.Has(snapshot, "ignored.txt").Should().BeFalse();
        SnapshotTestKit.Has(snapshot, "build/out.bin").Should().BeFalse();
        snapshot.Manifest.Files.Should().BeEquivalentTo([".gitignore", "a.txt", "untracked.txt", "dir/untracked2.txt"]);
    }

    [Fact]
    public async Task ThePinnedTree_CopiesWhatGitLists_NotTheUntracked()
    {
        await WriteAsync("a.txt", "committed");
        await _repo.CommitAsync("base");
        await WriteAsync("untracked.txt", "new");

        using var snapshot = await MadeAsync(Pinned);

        SnapshotTestKit.Has(snapshot, "untracked.txt").Should().BeFalse("a pinned tree is what git lists in it");
        snapshot.Manifest.Files.Should().BeEquivalentTo(["a.txt"]);
    }

    [Fact]
    public async Task AFileThatChangesMidCopy_IsRetriedOnce_AndTheRetryIsWhatIsKept()
    {
        await WriteAsync("moving.txt", "first");
        await _repo.CommitAsync("base");
        var options = SnapshotOptions.Default with
        {
            BeforeCopyOf = (attempt, path) =>
            {
                if (attempt == 1 && path == "moving.txt")
                {
                    File.WriteAllText(Path.Combine(_repo.Path, "moving.txt"), "second, longer");
                }
            },
        };

        using var snapshot = await MadeAsync(Live, options);

        SnapshotTestKit.TextAt(snapshot, "moving.txt").Should().Be("second, longer", "the first pass saw the change and the second pass copied the settled bytes");
    }

    [Fact]
    public async Task AFileThatKeepsChanging_IsRefusedAfterOneRetry()
    {
        await WriteAsync("moving.txt", "first");
        await _repo.CommitAsync("base");
        var attempts = new List<int>();
        var options = SnapshotOptions.Default with
        {
            BeforeCopyOf = (attempt, path) =>
            {
                attempts.Add(attempt);
                File.AppendAllText(Path.Combine(_repo.Path, path), $" +{attempt}");
            },
        };

        var reason = await RefusedAsync(Live, options);

        reason.Should().Contain("moving.txt");
        attempts.Distinct().Should().BeEquivalentTo([1, 2], "one retry, not none and not many");
        SnapshotTestKit.NothingPublishedUnder(_scratch);
    }

    // ---------------------------------------------------------------------------------------
    // Lifetime
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Disposal_DeletesTheSnapshotFolder()
    {
        await WriteAsync("a.txt", "a");
        await _repo.CommitAsync("base");
        var snapshot = await MadeAsync(Pinned);
        var root = snapshot.Root;
        Directory.Exists(root).Should().BeTrue();
        root.Should().StartWith(_scratch.Path, "the snapshot lives inside the scratch directory it was asked into");

        snapshot.Dispose();

        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public async Task ASnapshot_IsPublishedWhole_NeverAsAPartialName()
    {
        await WriteAsync("a.txt", "a");
        await _repo.CommitAsync("base");

        using var snapshot = await MadeAsync(Pinned);

        Path.GetFileName(snapshot.Root).Should().StartWith(ReviewSnapshots.Prefix).And.NotEndWith(".partial");
        Directory.GetDirectories(_scratch).Should().ContainSingle("the partial copy was renamed over, not left beside");
    }
}

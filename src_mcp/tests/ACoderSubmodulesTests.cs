using System.Security.Cryptography;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s6, the recorded change: the content of a submodule at the BASE commit is provided to a coder job's checks without the coder being able to change it and without a network (<see cref="CoderSubmodules"/>). The road is the pinned commit read from the
/// object store the main checkout already has and written into a folder of coai's own — never the main checkout's working copy, which can be on another commit, dirty or empty. These tests read that off real git on real repositories (<see cref="CoderSubmoduleWorld"/>).
/// </summary>
public sealed class ACoderSubmodulesTests
{
    private static async Task<CoderMounts> ProvideAsync(CoderSubmoduleWorld world, CoderTree tree, IProcessLauncher? launcher = null) =>
        await new CoderSubmodules(launcher ?? world.Launcher).ProvideAsync(world.Main.Path, world.BaseSha, tree.Root, world.Folder, TestContext.Current.CancellationToken);

    [Fact]
    public async Task TheCommitTheBasePins_IsProvided_NotWhatTheMainCheckoutHasCheckedOut()
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        var tree = await world.WorktreeAsync(CoderTestBed.JobId(1));
        (await File.ReadAllTextAsync(Path.Combine(world.MainMount, "rules", "a.md"), TestContext.Current.CancellationToken)).Should().Be(CoderSubmoduleWorld.AdvancedText, "POSITIVE CONTROL: the main checkout's own copy is on the later commit");

        var mounts = await ProvideAsync(world, tree);

        var mount = mounts.Declared.Should().ContainSingle().Subject;
        mount.Path.Should().Be(CoderSubmoduleWorld.MountPath);
        mount.Pin.Should().Be(world.Pin, "the gitlink of the base commit, not the submodule's HEAD in the main checkout");
        mount.Provided.Should().BeTrue(mount.Note);
        (await File.ReadAllTextAsync(Path.Combine(mount.Source, "rules", "a.md"), TestContext.Current.CancellationToken)).Should().Be(CoderSubmoduleWorld.PinnedText);
        File.Exists(Path.Combine(mount.Source, "tools", "x.mjs")).Should().BeTrue();
        mount.Source.Should().StartWith(world.Folder + Path.DirectorySeparatorChar, "in a folder of coai's own, never inside the worktree or the main checkout");
    }

    [Fact]
    public async Task TheBinds_TargetTheWorktreePathOfTheMount_AndNothingIsPutInTheWorktree()
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        var tree = await world.WorktreeAsync(CoderTestBed.JobId(2));

        var mounts = await ProvideAsync(world, tree);

        var bind = mounts.Binds(tree.Root).Should().ContainSingle().Subject;
        bind.Target.Should().Be(Path.Combine(tree.Root, CoderSubmoduleWorld.MountPath));
        Directory.Exists(bind.Target).Should().BeTrue("the folder git made for the gitlink is the mount point");
        Directory.EnumerateFileSystemEntries(bind.Target).Should().BeEmpty("coai puts nothing into the worktree: the content is bound over it in the unit only");
        (await world.Launcher.RunAsync(new ProcessRequest("git", ["status", "--porcelain=v1", "--untracked-files=all"], tree.Root), TestContext.Current.CancellationToken)).StdOut.Should().BeEmpty("git sees the same clean tree as before");
    }

    [Fact]
    public async Task NoNetworkIsNeeded_TheSubmodulesRemoteCanBeGone()
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        var tree = await world.WorktreeAsync(CoderTestBed.JobId(3));
        await world.Upstream.DisposeAsync();
        Directory.Exists(world.Upstream.Path).Should().BeFalse("POSITIVE CONTROL: the URL in .gitmodules points at nothing now");

        var mounts = await ProvideAsync(world, tree);

        mounts.Declared.Should().ContainSingle().Which.Provided.Should().BeTrue(mounts.Declared[0].Note);
    }

    [Fact]
    public async Task TheMainCheckout_ItsGitFolderAndTheSubmodulesOwnFolderIncluded_IsNotWritten()
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        var tree = await world.WorktreeAsync(CoderTestBed.JobId(4));
        var before = await BytesAsync(world.Main.Path);

        var mounts = await ProvideAsync(world, tree);

        mounts.Declared.Single().Provided.Should().BeTrue();
        (await BytesAsync(world.Main.Path)).Should().Equal(before);
    }

    [Fact]
    public async Task EveryGitCommandIsHardened_AndNoneFetchesClonesOrRunsASubmoduleVerb()
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        var tree = await world.WorktreeAsync(CoderTestBed.JobId(5));
        var watched = new WatchedLauncher(new ProcessLauncher());

        await ProvideAsync(world, tree, watched);

        var gits = watched.Requests.Where(request => request.Executable == "git").ToList();
        gits.Should().NotBeEmpty();
        foreach (var git in gits)
        {
            git.Arguments.Take(4).Should().Equal("-c", "core.hooksPath=/dev/null", "-c", "core.fsmonitor=false");
            git.Arguments.Should().NotContain(["fetch", "clone", "pull", "submodule", "push", "remote"]);
            git.Environment.Should().ContainKey("GIT_DIR").WhoseValue.Should().BeNull("the names that move git are taken out of the environment");
        }
    }

    [Fact]
    public async Task ASparseCheckoutSettingOfTheSubmodule_DoesNotThinOutWhatIsProvided()
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        var tree = await world.WorktreeAsync(CoderTestBed.JobId(6));
        var gitDir = await world.ModuleGitDirAsync();
        await world.InMountAsync("config", "core.sparseCheckout", "true");
        Directory.CreateDirectory(Path.Combine(gitDir, "info"));
        await File.WriteAllTextAsync(Path.Combine(gitDir, "info", "sparse-checkout"), "/rules/\n", TestContext.Current.CancellationToken);

        var mounts = await ProvideAsync(world, tree);

        File.Exists(Path.Combine(mounts.Declared.Single().Source, "tools", "x.mjs")).Should().BeTrue("the whole commit is provided, whatever the person's sparse patterns say");
    }

    [Fact]
    public async Task ASubmoduleTheMainCheckoutNeverInitialised_IsDeclaredAndProtected_ButNotProvided_AndNothingThrows()
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        await using var bare = await TempGitRepo.CloneAsync(world.Launcher, world.Main.Path, "csm-bare-");
        var started = await new CoderWorktree(world.Launcher, world.Root).StartAsync(bare.Path, CoderTestBed.JobId(7), world.BaseSha);
        var opened = CoderTree.Open(started.Should().BeOfType<CoderWorktreeOutcome.Started>(started.ToString()).Subject.Path, bare.Path);
        var tree = opened.Should().BeOfType<CoderTreeOpening.Opened>().Subject.Tree;

        var mounts = await new CoderSubmodules(world.Launcher).ProvideAsync(bare.Path, world.BaseSha, tree.Root, world.Folder, TestContext.Current.CancellationToken);

        var mount = mounts.Declared.Should().ContainSingle().Subject;
        mount.Path.Should().Be(CoderSubmoduleWorld.MountPath);
        mount.Pin.Should().Be(world.Pin);
        mount.Provided.Should().BeFalse();
        mount.Note.Should().Contain("not initialised").And.Contain("not provided");
        mounts.Binds(tree.Root).Should().BeEmpty();
        mounts.Protected.Should().Contain([".gitmodules", CoderSubmoduleWorld.MountPath]);
    }

    [Fact]
    public async Task ARepositoryWithNoGitmodulesFile_HasNoMountsAndNothingProtectedButTheFile()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher());
        await using var bed = await CoderTestBed.CreateAsync();
        var started = await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(8), bed.Shas[2]);
        var tree = CoderTree.Open(started.Should().BeOfType<CoderWorktreeOutcome.Started>(started.ToString()).Subject.Path, bed.Repo.Path).Should().BeOfType<CoderTreeOpening.Opened>().Subject.Tree;

        var mounts = await new CoderSubmodules(launcher).ProvideAsync(bed.Repo.Path, bed.Shas[2], tree.Root, Path.Combine(bed.Parent, "m"), TestContext.Current.CancellationToken);

        mounts.Declared.Should().BeEmpty();
        mounts.Protected.Should().Equal(".gitmodules");
        Directory.Exists(Path.Combine(bed.Parent, "m")).Should().BeFalse("nothing to make a folder for");
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/etc")]
    [InlineData("a b")]
    [InlineData("a:b")]
    [InlineData(".git/hooks")]
    [InlineData("sub/../../x")]
    public async Task APathInGitmodulesThatCouldLeaveTheWorktreeOrBeSpelledIntoABind_IsNeverMounted(string path)
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        // A hostile .gitmodules committed at a new base: the good mount plus one bad path, and no network named anywhere.
        var text = await File.ReadAllTextAsync(Path.Combine(world.Main.Path, ".gitmodules"), TestContext.Current.CancellationToken);
        await world.Main.WriteAsync(".gitmodules", text + $"[submodule \"bad\"]\n\tpath = {path}\n\turl = https://invalid.example/x.git\n");
        await world.Main.CommitAsync("m3 a hostile .gitmodules");
        var hostile = await world.Main.HeadAsync();
        var started = await new CoderWorktree(world.Launcher, world.Root).StartAsync(world.Main.Path, CoderTestBed.JobId(9), hostile);
        var tree = CoderTree.Open(started.Should().BeOfType<CoderWorktreeOutcome.Started>(started.ToString()).Subject.Path, world.Main.Path).Should().BeOfType<CoderTreeOpening.Opened>().Subject.Tree;
        var outside = Directory.GetParent(world.Folder)!.FullName;
        var before = Directory.EnumerateFileSystemEntries(outside).Order(StringComparer.Ordinal).ToList();

        var mounts = await new CoderSubmodules(world.Launcher).ProvideAsync(world.Main.Path, hostile, tree.Root, world.Folder, TestContext.Current.CancellationToken);

        mounts.Declared.Where(mount => mount.Provided).Should().ContainSingle().Which.Path.Should().Be(CoderSubmoduleWorld.MountPath);
        var bad = mounts.Declared.Single(mount => mount.Name == "bad");
        bad.Provided.Should().BeFalse();
        bad.Note.Should().Contain("not provided");
        mounts.Binds(tree.Root).Should().ContainSingle();
        Directory.EnumerateFileSystemEntries(outside).Except(before).Should().BeSubsetOf([world.Folder], "nothing was written outside the mounts folder");
    }

    [Fact]
    public async Task TheProtectedNames_AreTheGitmodulesFileAndEveryMount()
    {
        await using var world = await CoderSubmoduleWorld.CreateAsync();
        var tree = await world.WorktreeAsync(CoderTestBed.JobId(10));

        var mounts = await ProvideAsync(world, tree);

        mounts.Protected.Should().Equal(".gitmodules", CoderSubmoduleWorld.MountPath);
    }

    private static async Task<IReadOnlyDictionary<string, string>> BytesAsync(string folder)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            seen[Path.GetRelativePath(folder, file)] = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file)));
        }

        return seen;
    }
}

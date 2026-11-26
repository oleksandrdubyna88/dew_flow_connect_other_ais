using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s6, the recorded change: a coder diff that touches a submodule's path or <c>.gitmodules</c> is REFUSED. Before this change nothing refused it (the hardened git refuses the <c>submodule</c> verb, which is a different thing: it keeps coai's own git from
/// running a coder's <c>.gitmodules</c>; a coder that EDITED the file, or wrote files where a gitlink is, was committed like any other change). Now the turn is not committed and the answer names the path. The protected names are
/// <see cref="CoderMounts.Protected"/>, carried by the tree (<see cref="CoderTree.Protected"/>). A REAL repository with a real submodule.
/// </summary>
public sealed class ACoderCommitProtectsTheSubmoduleTests
{
    private static async Task<(CoderSubmoduleWorld World, CoderTree Tree, GitHistory Git, string Head)> StartAsync(string job)
    {
        var world = await CoderSubmoduleWorld.CreateAsync();
        foreach (var (key, value) in new[] { ("user.email", "t@t"), ("user.name", "t"), ("commit.gpgsign", "false") })
        {
            await world.Launcher.RunAsync(new ProcessRequest("git", ["config", key, value], world.Main.Path), TestContext.Current.CancellationToken);
        }

        var tree = await world.WorktreeAsync(job);
        var mounts = await new CoderSubmodules(world.Launcher).ProvideAsync(world.Main.Path, world.BaseSha, tree.Root, world.Folder, TestContext.Current.CancellationToken);
        tree = tree with { Protected = mounts.Protected };
        var git = new GitHistory(world.Launcher);

        return (world, tree, git, (await CoderCommits.HeadAsync(git, tree, TestContext.Current.CancellationToken)).Head);
    }

    [Fact]
    public async Task AnOrdinaryChange_IsCommitted_AsItAlwaysWas()
    {
        var (world, tree, git, before) = await StartAsync(CoderTestBed.JobId(21));
        await using var _ = world;
        await File.WriteAllTextAsync(Path.Combine(tree.Root, "work.txt"), "honest work\n", TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(git, tree, "turn", TestContext.Current.CancellationToken);

        made.Failure.Should().BeNull();
        made.Head.Should().NotBe(before);
    }

    [Fact]
    public async Task AnEditOfTheGitmodulesFile_IsRefused_NothingIsCommitted_AndTheAnswerNamesTheFile()
    {
        var (world, tree, git, before) = await StartAsync(CoderTestBed.JobId(22));
        await using var _ = world;
        await File.AppendAllTextAsync(Path.Combine(tree.Root, ".gitmodules"), "[submodule \"evil\"]\n\tpath = evil\n\turl = https://invalid.example/x.git\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(tree.Root, "work.txt"), "honest work beside it\n", TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(git, tree, "turn", TestContext.Current.CancellationToken);

        made.Failure.Should().NotBeNull(".gitmodules is the base's, whatever else the coder did");
        made.Failure!.Reason.Should().Contain(".gitmodules").And.Contain("nothing was committed");
        made.Failure.Stop.Should().Be("coder-commit");
        (await CoderCommits.HeadAsync(git, tree, TestContext.Current.CancellationToken)).Head.Should().Be(before);
    }

    [Fact]
    public async Task FilesWrittenWhereTheSubmoduleIs_AreRefused_AndSoIsAnyFileUnderIt()
    {
        var (world, tree, git, before) = await StartAsync(CoderTestBed.JobId(23));
        await using var _ = world;
        var inside = Path.Combine(tree.Root, CoderSubmoduleWorld.MountPath, "rules");
        Directory.CreateDirectory(inside);
        await File.WriteAllTextAsync(Path.Combine(inside, "a.md"), "the coder's own rule\n", TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(git, tree, "turn", TestContext.Current.CancellationToken);

        made.Failure.Should().NotBeNull();
        made.Failure!.Reason.Should().Contain(CoderSubmoduleWorld.MountPath).And.Contain("nothing was committed");
        (await CoderCommits.HeadAsync(git, tree, TestContext.Current.CancellationToken)).Head.Should().Be(before);
    }

    [Fact]
    public async Task ATreeWithNoProtectedNames_CommitsAGitmodulesChange_AsBefore_SoTheRefusalIsTheNewBehaviourAndNothingElse()
    {
        var world = await CoderSubmoduleWorld.CreateAsync();
        await using var _ = world;
        await world.Launcher.RunAsync(new ProcessRequest("git", ["config", "user.email", "t@t"], world.Main.Path), TestContext.Current.CancellationToken);
        await world.Launcher.RunAsync(new ProcessRequest("git", ["config", "user.name", "t"], world.Main.Path), TestContext.Current.CancellationToken);
        var tree = await world.WorktreeAsync(CoderTestBed.JobId(25));
        await File.AppendAllTextAsync(Path.Combine(tree.Root, ".gitmodules"), "\n", TestContext.Current.CancellationToken);

        var made = await CoderCommits.CommitAsync(new GitHistory(world.Launcher), tree, "turn", TestContext.Current.CancellationToken);

        made.Failure.Should().BeNull("the check belongs to the names a job protects; a tree that protects none is the tree of before");
    }
}

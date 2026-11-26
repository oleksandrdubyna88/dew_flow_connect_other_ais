using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A real main checkout with a real submodule at <c>.agents/conventions</c> (named <c>shared</c>, as this repository's own is, with a name that is not its path), for the suites of the submodule mount of a coder job (epoch 3 E2 s6, the recorded change).
/// The base commit pins the submodule's FIRST commit; afterwards the submodule's own repository moves on and the main checkout's working copy of it is checked out at the SECOND — so "what the main checkout shows" and "what the base pins" differ, and a mount
/// that takes the wrong one is seen. Everything is a temp folder; the submodule's URL is a local path and nothing here uses a network.
/// </summary>
internal sealed class CoderSubmoduleWorld : IAsyncDisposable
{
    internal const string MountPath = ".agents/conventions";

    internal const string PinnedText = "rule one\n";

    internal const string AdvancedText = "rule two\n";

    private readonly TempDir _work = TempDir.For("csm-");

    private CoderSubmoduleWorld(WatchedLauncher launcher, TempGitRepo upstream, TempGitRepo main, string baseSha, string pin, string later)
    {
        Launcher = launcher;
        Upstream = upstream;
        Main = main;
        BaseSha = baseSha;
        Pin = pin;
        Later = later;
        Root = _work.At("coder-root");
        Folder = _work.At("job.mounts");
    }

    public WatchedLauncher Launcher { get; }

    /// <summary>The submodule's own repository (the "remote").</summary>
    public TempGitRepo Upstream { get; }

    public TempGitRepo Main { get; }

    public string BaseSha { get; }

    /// <summary>The commit of the submodule the base pins.</summary>
    public string Pin { get; }

    /// <summary>The commit the main checkout's working copy of the submodule is on (not the pin).</summary>
    public string Later { get; }

    public string Root { get; }

    /// <summary>The folder the mounts are made in.</summary>
    public string Folder { get; }

    public string MainMount => Path.Combine(Main.Path, MountPath);

    public static async Task<CoderSubmoduleWorld> CreateAsync()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher());
        var upstream = await TempGitRepo.InitAsync(launcher, "csm-up-");
        Directory.CreateDirectory(Path.Combine(upstream.Path, "rules"));
        Directory.CreateDirectory(Path.Combine(upstream.Path, "tools"));
        await upstream.WriteAsync("rules/a.md", PinnedText);
        await upstream.WriteAsync("tools/x.mjs", "export const x = 1;\n");
        await upstream.CommitAsync("s1");
        var pin = await upstream.HeadAsync();

        var main = await TempGitRepo.InitAsync(launcher, "csm-main-");
        await main.WriteAsync("a.txt", "one\n");
        await main.CommitAsync("m1");
        await main.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", "--name", "shared", upstream.Path.Replace('\\', '/'), MountPath);
        await main.CommitAsync("m2 the submodule");
        var baseSha = await main.HeadAsync();

        // The submodule moves on, and the main checkout's working copy of it follows: the base still pins the first commit.
        await upstream.WriteAsync("rules/a.md", AdvancedText);
        await upstream.CommitAsync("s2");
        var later = await upstream.HeadAsync();
        var world = new CoderSubmoduleWorld(launcher, upstream, main, baseSha, pin, later);
        await world.InMountAsync("fetch", "-q", "origin");
        await world.InMountAsync("checkout", "-q", later);

        return world;
    }

    public async Task<string> InMountAsync(params string[] args)
    {
        var result = await Launcher.RunAsync(new ProcessRequest("git", args, MainMount));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut.Trim();
    }

    /// <summary>The submodule's own git folder, as the main checkout has it.</summary>
    public Task<string> ModuleGitDirAsync() => InMountAsync("rev-parse", "--absolute-git-dir");

    /// <summary>Makes the coder worktree of <paramref name="jobId"/> at the base, as the product does.</summary>
    public async Task<CoderTree> WorktreeAsync(string jobId)
    {
        var started = await new CoderWorktree(Launcher, Root).StartAsync(Main.Path, jobId, BaseSha);
        var made = started.Should().BeOfType<CoderWorktreeOutcome.Started>(started.ToString()).Subject;
        var opened = CoderTree.Open(made.Path, Main.Path);

        return opened.Should().BeOfType<CoderTreeOpening.Opened>(opened.ToString()).Subject.Tree;
    }

    public async ValueTask DisposeAsync()
    {
        await Main.DisposeAsync();
        await Upstream.DisposeAsync();
        _work.Dispose();
    }
}

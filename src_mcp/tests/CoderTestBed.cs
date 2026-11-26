using System.Security.Cryptography;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A real repository with three commits and a DIRTY main checkout, a coder root of its own, and the real launcher with every request written down — the common
/// ground of the coder-worktree suites (epoch 3 E2 s2 t2). Nothing here reads the real HOME or a real data folder: the root is a temp folder.
/// </summary>
/// <remarks>
/// The main checkout is dirty on purpose: a staged new file, an unstaged edit, an untracked file and an ignored one. A start that touched the checkout's HEAD, index
/// or files would then change a byte that a clean checkout could not show (<see cref="SnapshotAsync"/>).
/// </remarks>
internal sealed class CoderTestBed : IAsyncDisposable
{
    private readonly TempDir _work = TempDir.For("cwt-");

    private CoderTestBed(TempGitRepo repo, WatchedLauncher launcher, string[] shas)
    {
        Repo = repo;
        Launcher = launcher;
        Shas = shas;
        Root = _work.At("root");
        Coder = new CoderWorktree(launcher, Root);
    }

    public TempGitRepo Repo { get; }

    public WatchedLauncher Launcher { get; }

    /// <summary>The three commits, oldest first. The base of most tests is the FIRST, so a checkout of the branch tip would show.</summary>
    public string[] Shas { get; }

    /// <summary>The coder root: a folder of this bed, outside the repository, that does not exist until a start makes it.</summary>
    public string Root { get; }

    public string Parent => _work.Path;

    public CoderWorktree Coder { get; }

    public static async Task<CoderTestBed> CreateAsync()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher());
        var repo = await TempGitRepo.InitAsync(launcher, "coai-cwt-repo-");
        await repo.WriteAsync(".gitignore", "*.log\n");
        await repo.WriteAsync("a.txt", "one\n");
        await repo.CommitAsync("c1");
        var first = await repo.HeadAsync();
        await repo.WriteAsync("a.txt", "two\n");
        await repo.CommitAsync("c2");
        var second = await repo.HeadAsync();
        await repo.WriteAsync("b.txt", "bee\n");
        await repo.CommitAsync("c3");
        var third = await repo.HeadAsync();
        await repo.WriteAsync("a.txt", "edited and not staged\n");
        await repo.WriteAsync("staged.txt", "staged and new\n");
        await repo.GitAsync("add", "staged.txt");
        await repo.WriteAsync("untracked.txt", "nobody added me\n");
        await repo.WriteAsync("ignored.log", "ignored\n");

        return new CoderTestBed(repo, launcher, [first, second, third]);
    }

    /// <summary>Another repository, for the "per repository" assertions; it shares nothing with the first but the coder root.</summary>
    public async Task<TempGitRepo> AnotherRepoAsync()
    {
        var other = await TempGitRepo.InitAsync(Launcher, "coai-cwt-other-");
        await other.WriteAsync("o.txt", "other\n");
        await other.CommitAsync("o1");

        return other;
    }

    /// <summary>
    /// What a person looking at the main checkout would call its state, as bytes: every file outside <c>.git</c> by relative path, plus <c>.git/HEAD</c> and
    /// <c>.git/index</c>. Read straight from the disk — asking git for a status could itself refresh the index and hide the very change being looked for.
    /// </summary>
    public Task<IReadOnlyDictionary<string, string>> SnapshotAsync() => SnapshotOfAsync(Repo.Path);

    public static async Task<IReadOnlyDictionary<string, string>> SnapshotOfAsync(string repoPath)
    {
        var files = Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(repoPath, file).Replace('\\', '/'))
            .Where(static name => !name.StartsWith(".git/", StringComparison.Ordinal) || name is ".git/HEAD" or ".git/index")
            .Order(StringComparer.Ordinal);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in files)
        {
            seen[name] = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(repoPath, name))));
        }

        return seen;
    }

    public async Task<string> GitIn(string folder, params string[] args)
    {
        var result = await Launcher.RunAsync(new ProcessRequest("git", args, folder));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut.Trim();
    }

    /// <summary>The full path of every worktree git lists for the repository, the main checkout first.</summary>
    public async Task<IReadOnlyList<string>> ListedAsync() =>
        [.. (await GitIn(Repo.Path, "worktree", "list", "--porcelain"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.StartsWith("worktree ", StringComparison.Ordinal))
            .Select(static line => line["worktree ".Length..])];

    public async Task<bool> BranchExistsAsync(string name) =>
        (await Launcher.RunAsync(new ProcessRequest("git", ["show-ref", "--verify", "--quiet", $"refs/heads/{name}"], Repo.Path))).ExitCode == 0;

    /// <summary>A fresh job id of the strict shape, numbered so a test can make many.</summary>
    public static string JobId(int n) => $"r20261123101010-{n:x8}";

    public async ValueTask DisposeAsync()
    {
        await Repo.DisposeAsync();
        _work.Dispose();
    }
}

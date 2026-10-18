using Xunit;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// How git is started in a tree coai made — on EVERY OS, because "Windows gets the flag, nobody else's argv
/// moves" is a claim about the other two legs of CI as much as about this one.
/// </summary>
/// <remarks>
/// The fake launcher answers every request with success and writes it down, so these tests prove what git
/// was ASKED at each of the three launch points, without a real git or a real long path.
/// </remarks>
public sealed class TreeGitTests : IAsyncLifetime
{
    private const string LongPaths = "core.longpaths=true";

    private readonly Noted _launcher = new();
    private readonly List<string> _temps = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        foreach (var temp in _temps)
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException) { /* a straggling handle on a temp dir is not a test failure */ }
            catch (UnauthorizedAccessException) { /* read-only files on Windows */ }
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public void TheFlagLeadsTheArgvOnWindows_AndNothingChangesElsewhere()
    {
        string[] args = ["worktree", "add", "--detach", "somewhere", "abc123"];

        var argv = TreeGit.Argv(args);

        if (OperatingSystem.IsWindows())
        {
            argv.Should().Equal("-c", LongPaths, "worktree", "add", "--detach", "somewhere", "abc123");
        }
        else
        {
            argv.Should().Equal(args, "Linux and macOS argv is byte-for-byte what it was");
        }
    }

    [Fact]
    public async Task TheRoundManagerAsksForTheFlagOnEveryCall_OnWindowsOnly()
    {
        var storage = Temp("testsToDelete-wt-argv-");
        var manager = new WorktreeManager(_launcher, storage);

        await using (await manager.AddAsync("repo", "abc123", "s1", round: 1))
        {
        }

        _launcher.Calls.Should().NotBeEmpty();
        _launcher.Calls.Should().Contain(c => c.Contains("worktree") && c.Contains("add"));
        _launcher.Calls.Should().Contain(c => c.Contains("worktree") && c.Contains("remove"));
        AssertEveryCallCarriesTheFlagOnlyOnWindows();
    }

    [Fact]
    public async Task TheReviewTreeRootAsksForTheFlagOnEveryCall_OnWindowsOnly()
    {
        var root = new ReviewTreeRoot(_launcher, Temp("testsToDelete-review-argv-"));

        await root.GitAsync(root.Path, ["status", "--porcelain"], TimeSpan.FromSeconds(5), CancellationToken.None);

        _launcher.Calls.Should().ContainSingle().Which.Should().EndWith("status --porcelain");
        AssertEveryCallCarriesTheFlagOnlyOnWindows();
    }

    [Fact]
    public async Task TheSubmodulePopulationAsksForTheFlagBeforeItsOwnSettings_OnWindowsOnly()
    {
        var repo = Temp("testsToDelete-sub-repo-");
        var tree = Temp("testsToDelete-sub-tree-");
        Directory.CreateDirectory(Path.Combine(repo, "mods", "sub"));
        await File.WriteAllTextAsync(Path.Combine(repo, "mods", "sub", ".git"), "gitdir: nowhere", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(tree, ".gitmodules"),
            "[submodule \"mods/sub\"]\n\tpath = mods/sub\n\turl = ../sub\n",
            TestContext.Current.CancellationToken);

        await new SubmodulePopulator(_launcher).PopulateAsync(repo, tree);

        var call = _launcher.Calls.Should().ContainSingle().Subject;
        call.Should().Contain("submodule update --init -- mods/sub");
        call.Should().Contain("-c protocol.file.allow=always", "the population's own setting is kept, whatever precedes it");
        AssertEveryCallCarriesTheFlagOnlyOnWindows();
    }

    private void AssertEveryCallCarriesTheFlagOnlyOnWindows()
    {
        foreach (var call in _launcher.Calls)
        {
            if (OperatingSystem.IsWindows())
            {
                call.Should().StartWith($"-c {LongPaths} ", "on Windows every call that works in OUR tree asks for long paths");
            }
            else
            {
                call.Should().NotContain("longpaths", "off Windows git is started exactly as before");
            }
        }
    }

    private string Temp(string prefix)
    {
        var path = Directory.CreateTempSubdirectory(prefix).FullName;
        _temps.Add(path);

        return path;
    }

    /// <summary>Every request answered with success and written down as the argv git would have been given.</summary>
    private sealed class Noted : IProcessLauncher
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls => [.. _calls];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            _calls.Add(string.Join(' ', request.Arguments));

            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));
        }
    }
}

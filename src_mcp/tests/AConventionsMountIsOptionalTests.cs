using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shared conventions are optional (v2 E3.S5, <c>v2/architecture/server.md</c> § 18.4): coai neither expects nor ships them,
/// a repository that still mounts <c>.agents/conventions</c> has its rules read as its own, and an ordinary submodule is
/// populated in a round's worktree whatever else is mounted.
/// </summary>
/// <remarks>
/// <para>Real git, real submodules, a real round worktree (<see cref="WorktreeManager"/>) — the failure this holds is what the
/// worktree and the collector do TOGETHER: <c>SubmodulePopulator</c> fills every submodule from the parent's own copy, and
/// <see cref="RuleFiles.Collect(string, RuleOrder)"/> then reads what is there. Two repositories, one fixture each:
/// <b>with</b> the mount (and an ordinary submodule beside it) and <b>without</b> it (the ordinary submodule only).</para>
/// <para>What the prompt says about a repository without the mount is held on the real stages by <c>PanelServiceTests</c> and
/// <c>ADocumentIsReviewedEndToEndTests</c>; here it is the section the collector's bundle renders.</para>
/// </remarks>
public sealed class AConventionsMountIsOptionalTests : IAsyncLifetime
{
    private const string Mount = ".agents/conventions";
    private const string Ordinary = "libs/tool";

    private readonly ProcessLauncher _launcher = new();
    private readonly List<string> _temps = [];
    private string _storage = string.Empty;
    private WorktreeManager _manager = null!;

    public ValueTask InitializeAsync()
    {
        _storage = Temp("coai-optional-storage-");
        _manager = new WorktreeManager(_launcher, _storage);

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var temp in _temps)
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException) { /* a straggling handle on a temp dir is not a test failure */ }
            catch (UnauthorizedAccessException) { /* read-only .git files on Windows */ }
        }

        return ValueTask.CompletedTask;
    }

    private string Temp(string prefix)
    {
        var path = TempDir.For(prefix).Path;
        _temps.Add(path);

        return path;
    }

    private async Task Git(string cwd, params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], cwd));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} must succeed: {result.StdErr}");
    }

    /// <summary>A repository with an AGENTS.md of its own, committed on main.</summary>
    private async Task<string> RepositoryAsync(string prefix)
    {
        var repo = Temp(prefix);
        await Git(repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(repo, "AGENTS.md"), "The repository's own instructions.");
        await Git(repo, "add", ".");
        await Git(repo, "commit", "-m", "the repository");

        return repo;
    }

    /// <summary>An upstream with one file under <paramref name="files"/>' relative paths, mounted at <paramref name="path"/> and committed.</summary>
    private async Task MountAsync(string repo, string path, params (string Relative, string Text)[] files)
    {
        var upstream = Temp("coai-optional-sub-");
        await Git(upstream, "init", "-b", "main");
        foreach (var (relative, text) in files)
        {
            var full = Path.Combine(upstream, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, text);
        }

        await Git(upstream, "add", ".");
        await Git(upstream, "commit", "-m", "the submodule");
        await Git(repo, "-c", "protocol.file.allow=always", "submodule", "add", upstream.Replace('\\', '/'), path);
        await Git(repo, "commit", "-m", $"mount {path}");
    }

    private async Task<string> RepositoryWithTheMountAsync()
    {
        var repo = await RepositoryAsync("coai-optional-with-");
        await MountAsync(repo, Mount, ("common/reuse-first.md", "Look before you build."));
        await MountAsync(repo, Ordinary, ("tool.txt", "an ordinary submodule's file"));

        return repo;
    }

    private async Task<string> RepositoryWithoutTheMountAsync()
    {
        var repo = await RepositoryAsync("coai-optional-without-");
        await MountAsync(repo, Ordinary, ("tool.txt", "an ordinary submodule's file"));

        return repo;
    }

    [Fact]
    public async Task ARepositoryThatMountsTheConventions_HasThemReadAsItsOwnRules_AndItsOrdinarySubmoduleIsPopulated()
    {
        var repo = await RepositoryWithTheMountAsync();
        var sha = await _manager.ResolveShaAsync(repo, "main");

        await using var lease = await _manager.AddAsync(repo, sha, "s1", round: 1);
        var bundle = RuleFiles.Collect(lease.Path, RuleOrder.ForBranch("main"));

        File.Exists(Path.Combine(lease.Path, "libs", "tool", "tool.txt")).Should().BeTrue("an ordinary submodule is populated in the round's worktree");
        bundle.Files.Select(file => file.Path).Should().Equal(["AGENTS.md", ".agents/conventions/common/reuse-first.md"],
            "the mount is read as the repository's own rules, after its instruction files; the ordinary submodule is not a rules mount");
        bundle.MissingMounts.Should().BeEmpty();
        RulesText.Section(bundle).Should().Contain("Look before you build").And.NotContain("NONE of the");
    }

    [Fact]
    public async Task ARepositoryWithoutTheMount_IsToldNothingIsMissing_AndItsOrdinarySubmoduleIsPopulated()
    {
        var repo = await RepositoryWithoutTheMountAsync();
        var sha = await _manager.ResolveShaAsync(repo, "main");

        await using var lease = await _manager.AddAsync(repo, sha, "s1", round: 1);
        var bundle = RuleFiles.Collect(lease.Path, RuleOrder.ForBranch("main"));

        File.Exists(Path.Combine(lease.Path, "libs", "tool", "tool.txt")).Should().BeTrue("an ordinary submodule is populated whether or not the conventions are mounted");
        bundle.Files.Select(file => file.Path).Should().Equal(["AGENTS.md"]);
        bundle.MissingMounts.Should().BeEmpty("a mount the repository never declared is not a mount that went missing");
        var section = RulesText.Section(bundle);
        section.Should().Contain("The repository's own instructions.");
        section.Should().NotContain("NONE of the").And.NotContain("NOT in the tree").And.NotContain("different revision");
    }

    [Fact]
    public async Task AMountedConventionsFolderTheRoundCannotPopulate_IsStillNamedAsAMissingMount()
    {
        // The repository DECLARES the mount but the parent never initialised it, so there is nothing to populate from:
        // a round with fewer rules, and the one notice that stays — it is about a mount THIS repository declares.
        var repo = await RepositoryAsync("coai-optional-unpopulated-");
        await File.WriteAllTextAsync(
            Path.Combine(repo, ".gitmodules"),
            $"[submodule \"conventions\"]\n\tpath = {Mount}\n\turl = https://example.invalid/rules.git\n");
        await Git(repo, "add", ".");
        await Git(repo, "commit", "-m", "declare a mount nobody can fetch");
        var sha = await _manager.ResolveShaAsync(repo, "main");

        await using var lease = await _manager.AddAsync(repo, sha, "s1", round: 1);

        RuleFiles.Collect(lease.Path, RuleOrder.ForBranch("main")).MissingMounts.Should().Equal([Mount]);
    }
}

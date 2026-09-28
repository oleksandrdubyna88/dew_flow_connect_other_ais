using CoaiMcp.Core.Feature;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A submodule changed in a feature's range is named in the pack as a submodule AT A COMMIT, never read,
/// and never "deleted at head" — on a real repository with real submodules (§9.31 of the feature-review plan).
/// </summary>
/// <remarks>
/// <para><b>Found by the D26 live run.</b> <c>git cat-file --batch-check</c> answers a gitlink with two
/// fields (<c>&lt;oid&gt; submodule</c>); the parser accepted only three, so the live submodule
/// <c>.agents/conventions</c> read as MISSING at head, <c>FeatureOutlineBuilder.Changed</c> classified it
/// <c>Deleted</c>, and the pack told three reviewers the feature had deleted its own rules. The reason
/// <c>ReadPlan</c> already had for a submodule was unreachable.</para>
/// <para>Three submodules, one per fate: a pin MOVED in the range (modified), one ADDED in the range, and one
/// REMOVED in the range — the last is the positive that keeps "never deleted" from becoming "never
/// Deleted even when it was".</para>
/// </remarks>
public sealed class ASubmoduleInTheRangeIsNamedNotDeletedTests : IAsyncLifetime
{
    private readonly WatchedLauncher _launcher = new(new ProcessLauncher());
    private readonly List<string> _dirs = [];
    private string _repo = string.Empty;
    private string _base = string.Empty;
    private string _head = string.Empty;
    private string _movedTo = string.Empty;
    private string _added = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var rules = await Upstream("coai-submodule-rules-");
        var other = await Upstream("coai-submodule-other-");
        var gone = await Upstream("coai-submodule-gone-");
        var pinnedAtBase = await Rev(rules, "HEAD");

        _repo = Temp("coai-submodule-repo-");
        await Git(_repo, "init", "-b", "main");
        File.WriteAllText(Path.Combine(_repo, "app.cs"), "public sealed class App { }\n");
        await AddSubmodule(rules, "mods/rules");
        await AddSubmodule(gone, "mods/gone");
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "base");
        _base = await Rev(_repo, "HEAD");

        // The feature: the rules pin moves to a new commit, a second submodule arrives, a third leaves.
        await Git(rules, "commit", "--allow-empty", "-m", "a newer rule");
        _movedTo = await Rev(rules, "HEAD");
        _movedTo.Should().NotBe(pinnedAtBase, "the fixture moves the pin");
        await Git(Path.Combine(_repo, "mods", "rules"), "fetch", "-q", "origin");
        await Git(Path.Combine(_repo, "mods", "rules"), "checkout", "-q", _movedTo);
        await AddSubmodule(other, "mods/other");
        _added = await Rev(other, "HEAD");
        await Git(_repo, "rm", "-q", "mods/gone");
        File.WriteAllText(Path.Combine(_repo, "app.cs"), "public sealed class App { public int V() => 1; }\n");
        await Git(_repo, "add", ".");
        await Git(_repo, "commit", "-m", "the feature");
        _head = await Rev(_repo, "HEAD");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var dir in _dirs)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal); // git's pack files are read-only on Windows
                }

                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private async Task<FeatureOutline> Build() =>
        await new FeatureOutlineBuilder(_launcher, new TreeSitterOutliner()).BuildAsync(_repo, _base, _head);

    [Fact]
    public async Task AMovedPin_IsAModifiedSubmoduleAtItsNewCommit_NeverDeleted()
    {
        var outline = await Build();

        outline.Files.Should().ContainSingle(f => f.Path == "mods/rules").Which.Letter.Should().Be('M', "the submodule is there at head");
        outline.Omissions.NotOutlined.Should().ContainSingle(n => n.Path == "mods/rules")
            .Which.Should().Be(new NotOutlined("mods/rules", $"a submodule at commit {_movedTo}; not read"),
                "named like a binary or an oversized file — what it is, where it stands, that it was not read");
    }

    [Fact]
    public async Task AnAddedSubmodule_IsNamedAtItsCommit()
    {
        var outline = await Build();

        outline.Files.Should().ContainSingle(f => f.Path == "mods/other").Which.Letter.Should().Be('A');
        outline.Omissions.NotOutlined.Should().ContainSingle(n => n.Path == "mods/other")
            .Which.Reason.Should().Be($"a submodule at commit {_added}; not read");
    }

    /// <summary>The positive: a submodule the feature really removed IS deleted at head.</summary>
    [Fact]
    public async Task ARemovedSubmodule_IsStillDeletedAtHead()
    {
        var outline = await Build();

        outline.Files.Should().ContainSingle(f => f.Path == "mods/gone").Which.Letter.Should().Be('D');
        outline.Omissions.NotOutlined.Should().ContainEquivalentOf(new NotOutlined("mods/gone", "deleted at head"));
    }

    [Fact]
    public async Task NoSubmoduleContentIsRead_AndTheOrdinaryFileStillIs()
    {
        var outline = await Build();

        var batch = _launcher.Requests.Where(r => r.Arguments.SequenceEqual(["cat-file", "--batch"]))
            .Should().ContainSingle("ONE batch reads every chosen blob").Subject;
        batch.StdIn.Should().NotContain(_movedTo).And.NotContain(_added, "a submodule's commit is not in this repository and is never read");
        batch.StdIn.Should().Contain(await Rev(_repo, $"{_head}:app.cs"), "the positive: the ordinary file IS read");
        outline.Section.Should().Contain("### app.cs");
    }

    /// <summary>A repository with one commit, to be a submodule's upstream.</summary>
    /// <remarks>
    /// Its first commit's message is the prefix, so no two upstreams share a commit id: three empty commits
    /// made in the same second by the same author ARE one commit, and git's rename detection then pairs the
    /// removed submodule with the added one as a rename of a single gitlink.
    /// </remarks>
    private async Task<string> Upstream(string prefix)
    {
        var dir = Temp(prefix);
        await Git(dir, "init", "-b", "main");
        await Git(dir, "commit", "--allow-empty", "-m", prefix);

        return dir;
    }

    private async Task AddSubmodule(string upstream, string path) =>
        await Git(_repo, "-c", "protocol.file.allow=always", "submodule", "add", "-q", upstream.Replace('\\', '/'), path);

    private string Temp(string prefix)
    {
        var dir = Directory.CreateTempSubdirectory(prefix).FullName;
        _dirs.Add(dir);

        return dir;
    }

    private static async Task Git(string dir, params string[] args)
    {
        var result = await new ProcessLauncher().RunAsync(new ProcessRequest(
            "git",
            ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", .. args],
            dir));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private static async Task<string> Rev(string dir, string rev)
    {
        var result = await new ProcessLauncher().RunAsync(new ProcessRequest("git", ["rev-parse", rev], dir));
        result.ExitCode.Should().Be(0, result.StdErr);

        return result.StdOut.Trim();
    }
}

using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A file whose NAME is git pathspec magic must still get its own diff, and only its own.
/// </summary>
/// <remarks>
/// <para>The third shape of finding 4, found by CodeRabbit on the pull request that fixed the other
/// two. <c>--numstat -z</c> hands back the exact path, and the path is then handed back to git after
/// <c>--</c> — where <b>a leading <c>:(…)</c> is not a name at all but an instruction</b>. A file
/// called <c>:(exclude)ordinary.txt</c> asks git for "everything except ordinary.txt", so the
/// reviewer receives somebody else's changed file underneath that name, with nothing anywhere
/// saying so. Measured before the fix: the one-file diff came back carrying two files.</para>
/// <para><b>Built through <c>mktree</c> rather than through the working tree</b>, because Windows
/// refuses the name in a directory entry AND in the index — <c>git update-index --cacheinfo</c>
/// answers <c>Invalid path</c>. Git objects have no such objection, and a Linux checkout, which is
/// what the Team server reviews, carries the path perfectly happily. So the fixture writes trees and
/// commits directly; that is also the only form of this fixture that runs on both platforms.</para>
/// </remarks>
public sealed class ContextAssemblerPathspecMagicTests : IAsyncLifetime
{
    /// <summary>A legal POSIX filename, and an exclude pathspec, and that is the whole problem.</summary>
    private const string NameThatIsAlsoPathspecMagic = ":(exclude)ordinary.txt";

    private const string SomebodyElsesWork = "a line only third.txt has";

    private readonly ProcessLauncher _launcher = new();
    private readonly ContextAssembler _assembler;
    private string _repo = string.Empty;

    public ContextAssemblerPathspecMagicTests() => _assembler = new ContextAssembler(_launcher);

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-pathspec-").FullName;
        await Git("init", "-b", "main");

        var before = await Commit(
            await Tree(new() { ["ordinary.txt"] = "base\n", ["third.txt"] = "base\n" }),
            parent: string.Empty);

        // All three changed, and the third one is the evidence: its content must not appear under
        // anybody else's name.
        var after = await Commit(
            await Tree(new()
            {
                ["ordinary.txt"] = "base\nchanged\n",
                ["third.txt"] = $"base\n{SomebodyElsesWork}\n",
                [NameThatIsAlsoPathspecMagic] = "mine\n",
            }),
            parent: before);

        await Git("update-ref", "refs/heads/main", before);
        await Git("update-ref", "refs/heads/feature", after);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_repo, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AFileNamedLikePathspecMagicCarriesOnlyItsOwnDiff()
    {
        var files = await Collect();

        var magic = files.Should().ContainSingle(f => f.Path == NameThatIsAlsoPathspecMagic,
            "the collected diff should name it once — it collected: {0}",
            string.Join(", ", files.Select(f => f.Path))).Subject;

        magic.Text.Should().Contain("mine",
            "the file's own change is what the reviewer asked for");
        magic.Text.Should().NotContain(SomebodyElsesWork,
            "a name that git reads as an instruction must not decide WHICH file's diff comes back — "
            + "the reviewer would be reading another file's work under this one's name");
    }

    [Fact]
    public async Task TheOtherFilesAreUnaffected()
    {
        // The companion guard: escaping the pathspecs must not stop an ordinary path from matching.
        var files = await Collect();

        files.Select(f => f.Path).Should().BeEquivalentTo(
            [NameThatIsAlsoPathspecMagic, "ordinary.txt", "third.txt"]);
        files.Should().OnlyContain(f => f.Text.Length > 0, "every one of them changed");
    }

    private async Task<IReadOnlyList<FileDiff>> Collect() =>
        (await _assembler.CollectAsync(_repo, "main", "feature", ct: TestContext.Current.CancellationToken)).Files;

    /// <summary>One tree object holding exactly these paths, written without a working tree.</summary>
    private async Task<string> Tree(Dictionary<string, string> entries)
    {
        var lines = new List<string>();
        foreach (var (path, text) in entries)
        {
            lines.Add($"100644 blob {await Blob(text)}\t{path}");
        }

        return await GitWithInput(string.Join('\n', lines) + "\n", "mktree");
    }

    private async Task<string> Blob(string text) =>
        await GitWithInput(text, "hash-object", "-w", "--stdin");

    private async Task<string> Commit(string tree, string parent) =>
        await Git(parent.Length == 0
            ? ["commit-tree", tree, "-m", "x"]
            : ["commit-tree", tree, "-m", "x", "-p", parent]);

    private async Task<string> Git(params string[] args) => await GitWithInput(string.Empty, args);

    private async Task<string> GitWithInput(string stdIn, params string[] args)
    {
        var result = await _launcher.RunAsync(
            new ProcessRequest(
                "git",
                ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args],
                _repo)
            { StdIn = stdIn });
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut.Trim();
    }
}

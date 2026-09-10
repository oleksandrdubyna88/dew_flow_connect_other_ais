using Xunit;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>Real git: the exclusions and binary handling only mean anything against the real diff.</summary>
public sealed class ContextAssemblerTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly ContextAssembler _assembler;
    private string _repo = string.Empty;

    public ContextAssemblerTests() => _assembler = new ContextAssembler(_launcher);

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-ctx-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitignore"), "ignored/\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");

        // The branch changes real code, a lock file, a dist artefact, a binary, and an ignored file.
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v2 with a real change\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "package-lock.json"), "{\"locked\": true}\n");
        Directory.CreateDirectory(Path.Combine(_repo, "dist"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "dist", "bundle.js"), "minified noise\n");
        await File.WriteAllBytesAsync(Path.Combine(_repo, "logo.png"), [0, 1, 2, 0, 255, 0, 7]);
        Directory.CreateDirectory(Path.Combine(_repo, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "ignored", "scratch.txt"), "never committed\n");
        await Git("add", ".");
        await Git("commit", "-m", "feature work");
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

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git",
            ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args],
            _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private async Task<IReadOnlyList<FileDiff>> Collect() =>
        (await _assembler.CollectAsync(_repo, "main", "feature", ct: TestContext.Current.CancellationToken)).Files;

    /// <summary>Move `main` on, the way another session merging its own pull request does.</summary>
    private async Task MoveTheBase()
    {
        await Git("checkout", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "somebody-elses.cs"), "merged while you worked\n");
        await File.WriteAllBytesAsync(Path.Combine(_repo, "theirs.png"), [9, 9, 9, 0, 255, 0, 1, 2, 3]);
        await Git("add", ".");
        await Git("commit", "-m", "another session merged its own PR");
        await Git("checkout", "feature");
    }

    [Fact]
    public async Task LockFilesAndBuildOutput_NeverReachTheDiff()
    {
        var files = await Collect();

        var paths = files.Select(f => f.Path);
        paths.Should().Contain("app.cs");
        paths.Should().NotContain("package-lock.json", "a context window spent on a lock file is a finding not made");
        paths.Should().NotContain("dist/bundle.js");
    }

    [Fact]
    public async Task GitignoredFiles_NeverReachTheDiff()
    {
        var files = await Collect();

        files.Select(f => f.Path).Should().NotContain(p => p.StartsWith("ignored/"),
            "git never diffs what the repository itself ignores");
    }

    [Fact]
    public async Task Binary_ComesBackNamed_WithItsBlobSize_NotInlined()
    {
        var files = await Collect();

        var binary = files.Should().ContainSingle(f => f.Path == "logo.png").Subject;
        binary.IsBinary.Should().BeTrue();
        binary.BinaryBytes.Should().Be(7);
        binary.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRealChange_RidesWholeIntoTheShapedBundle()
    {
        var shaped = DiffShaper.Shape(await Collect());

        shaped.Text.Should().Contain("real change");
        shaped.WasElided.Should().BeFalse();
    }

    /// <remarks>
    /// 2026-09-08, measured: a round reported three Blocking findings from two vendors saying the
    /// branch had deleted three files and reverted a version. It had not — `main` had moved under
    /// it, and `A..B` shows the commits the base has and the branch does not as DELETIONS the branch
    /// performed. 17 files and 1616 deletions were sent where the branch's own change was 9 files
    /// and 12. It has happened three times.
    /// </remarks>
    [Fact]
    public async Task AChangeIsDiffedAgainstTheMergeBase_NotTheTipOfMain()
    {
        await MoveTheBase();

        var collected = await _assembler.CollectAsync(_repo, "main", "feature", ct: TestContext.Current.CancellationToken);

        collected.Files.Select(f => f.Path).Should().NotContain("somebody-elses.cs",
            "a file another session merged into the base is not something this branch did");
        collected.Files.Select(f => f.Path).Should().Contain("app.cs", "the branch's own change is still there");
        collected.Kind.Should().Be(DiffBase.MergeBase);
        collected.ComparedAgainst.Should().NotBe("main", "the round can only be re-checked if it names the commit");
    }

    [Fact]
    public async Task ABinaryIsSizedAgainstTheMergeBase_NotTheMovedTip()
    {
        // `cat-file` takes a rev, not a range, so this call site cannot be fixed by three dots — it
        // needs the resolved commit, and without it the OLD side of a binary is read from a commit
        // somebody else made.
        await MoveTheBase();

        var collected = await _assembler.CollectAsync(_repo, "main", "feature", ct: TestContext.Current.CancellationToken);

        collected.Files.Select(f => f.Path).Should().NotContain("theirs.png");
        var mine = collected.Files.Should().ContainSingle(f => f.Path == "logo.png").Subject;
        mine.BinaryBytes.Should().Be(7);
    }

    [Fact]
    public async Task AReviewOfOneCommit_IsStillItsOwnDiff()
    {
        // The documented usage: pass the commit as the branch and its parent as the base. The parent
        // IS the merge base, so nothing about this changes — which is the point of asserting it.
        var collected = await _assembler.CollectAsync(_repo, "feature~1", "feature", ct: TestContext.Current.CancellationToken);

        collected.Files.Select(f => f.Path).Should().Contain("app.cs");
        collected.Kind.Should().Be(DiffBase.MergeBase);
    }

    [Fact]
    public async Task UnrelatedHistoriesFallBackAndSaySo()
    {
        await Git("checkout", "--orphan", "stranger");
        await Git("rm", "-rf", ".");
        await File.WriteAllTextAsync(Path.Combine(_repo, "stranger.cs"), "no ancestor at all\n");
        await Git("add", ".");
        await Git("commit", "-m", "a history of its own");
        await Git("checkout", "feature");

        var collected = await _assembler.CollectAsync(_repo, "stranger", "feature", ct: TestContext.Current.CancellationToken);

        collected.Kind.Should().Be(DiffBase.NoCommonAncestor, "the one case three dots cannot answer");
        // A COMMIT even here. `stranger` is read three times — the numstat, each file's diff, a
        // binary's old side — and a ref another session can advance between two of them is a review
        // of two snapshots that nobody could reproduce from a log naming only the ref.
        collected.ComparedAgainst.Should().MatchRegex("^[0-9a-f]{7,64}$",
            "the fallback pins the ref to a commit rather than carrying a moving one");
        // And it is the two-dot diff, not an empty list dressed up as one: the stranger's own file is
        // absent from `feature` and shows as a deletion, which is exactly what two dots means here.
        var paths = collected.Files.Select(f => f.Path).ToList();
        paths.Should().Contain("app.cs").And.Contain("stranger.cs");
    }

    [Fact]
    public async Task ABaseThatNamesNoCommit_IsRefusedRatherThanHandedToGit()
    {
        // The one place a caller's own string would have reached a command line. A "ref" beginning
        // with a dash is an OPTION to git, and `--output=` is one that writes a file; every value
        // that reaches a range or a `rev:path` argument is an object id by the time it gets there.
        // (gemini, the code round.)
        var refused = async () => await _assembler.CollectAsync(
            _repo, "--output=owned.txt", "feature", ct: TestContext.Current.CancellationToken);

        await refused.Should().ThrowAsync<ContextException>().WithMessage("*names no commit*");
        File.Exists(Path.Combine(_repo, "owned.txt")).Should().BeFalse("git was never asked to write it");
    }
}

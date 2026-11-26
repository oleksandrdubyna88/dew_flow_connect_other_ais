using System.Text;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s2 t2 (security): the manifests of a COMMIT, read from the objects of the main repository — real git, real repositories. The same commit gives the same answer; nothing of a working tree is read; a link is a refusal and is never read; a submodule is never entered; every limit is a
/// refusal with the number and the limit; a commit that is not there and a git that does not answer are refusals, never an empty set.
/// </summary>
public sealed class AManifestsAtACommitTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();

    private TempGitRepo _repo = null!;

    private ManifestsAtCommit Reader => new(new GitHistory(_launcher));

    public async ValueTask InitializeAsync() => _repo = await TempGitRepo.InitAsync(_launcher, "mac-");

    public ValueTask DisposeAsync() => _repo.DisposeAsync();

    private async Task<ManifestRead> ReadAtHeadAsync() => await Reader.ReadAsync(_repo.Path, await _repo.HeadAsync(), ToolchainDefaults.Current);

    private static ManifestSet Found(ManifestRead read) => read.Should().BeOfType<ManifestRead.Found>(read.ToString()).Subject.Set;

    private async Task StageAsync(string path, string text) => await _repo.StageBlobAsync(path, Encoding.UTF8.GetBytes(text));

    // ---- d1: what is found, and from where ----

    [Fact]
    public async Task TheManifestsOfACommit_AreTheNamesOfTheTable_WithTheirTexts_AndPresenceFilesWithNone()
    {
        await StageAsync("src/App.sln", "sln text\n");
        await StageAsync("src/App/App.csproj", "<Project/>\n");
        await StageAsync("global.json", "{\"sdk\":{}}\n");
        await StageAsync("Directory.Build.props", "<Project/>\n");
        await StageAsync("web/package.json", "{\"name\":\"web\"}\n");
        await StageAsync("web/package-lock.json", "{\"lockfileVersion\":3}\n");
        await StageAsync("web/tsconfig.json", "{}\n");
        await StageAsync("web/.prettierrc.json", "{}\n");
        await StageAsync("README.md", "documents are not manifests\n");
        await StageAsync("lib/Cargo.toml", "[package]\n");
        await _repo.CommitAsync("c1");

        var set = Found(await ReadAtHeadAsync());

        set.Files.Select(file => file.Path).Should().Equal("Directory.Build.props", "global.json", "lib/Cargo.toml", "src/App.sln", "src/App/App.csproj", "web/.prettierrc.json", "web/package-lock.json", "web/package.json", "web/tsconfig.json");
        set.Files.Single(file => file.Path == "web/package.json").Should().Match<ManifestFile>(file => file.Stack == "node" && file.Kind == ManifestKind.Manifest && file.Text == "{\"name\":\"web\"}\n");
        set.Files.Single(file => file.Path == "global.json").Kind.Should().Be(ManifestKind.Definition);
        set.Files.Single(file => file.Path == "Directory.Build.props").Kind.Should().Be(ManifestKind.Definition);
        set.Files.Single(file => file.Path == "lib/Cargo.toml").Stack.Should().Be("rust");
        set.Of(ManifestKind.Present).Select(file => file.Path).Should().Equal("web/.prettierrc.json", "web/package-lock.json", "web/tsconfig.json");
        set.Of(ManifestKind.Present).Should().OnlyContain(file => file.Text.Length == 0, "a lock file's name is the fact: its text is never read");
    }

    [Fact]
    public async Task TheSameCommit_GivesTheSameAnswer()
    {
        await StageAsync("a/package.json", "{}\n");
        await StageAsync("b/App.csproj", "<Project/>\n");
        await _repo.CommitAsync("c1");
        var head = await _repo.HeadAsync();

        var first = Found(await Reader.ReadAsync(_repo.Path, head, ToolchainDefaults.Current));
        var second = Found(await Reader.ReadAsync(_repo.Path, head, ToolchainDefaults.Current));

        second.Files.Should().Equal(first.Files);
        second.Notes.Should().Equal(first.Notes);
    }

    [Fact]
    public async Task ADepthOfFourFoldersIsSearched_AFifthIsNot()
    {
        await StageAsync("a/b/c/d/package.json", "{}\n");
        await StageAsync("a/b/c/d/e/package.json", "{}\n");
        await _repo.CommitAsync("c1");

        Found(await ReadAtHeadAsync()).Files.Select(file => file.Path).Should().Equal("a/b/c/d/package.json");
    }

    [Theory]
    [InlineData("node_modules/x/package.json")]
    [InlineData("web/node_modules/package.json")]
    [InlineData("bin/App.csproj")]
    [InlineData("obj/App.csproj")]
    [InlineData(".hidden/package.json")]
    [InlineData("src/.cache/package.json")]
    public async Task AFolderCalledNodeModulesBinObjOrStartingWithADot_IsNeverEntered(string path)
    {
        await StageAsync(path, "{}\n");
        await StageAsync("real/package.json", "{}\n");
        await _repo.CommitAsync("c1");

        Found(await ReadAtHeadAsync()).Files.Select(file => file.Path).Should().Equal("real/package.json");
    }

    [Fact]
    public async Task NothingOfTheWorkingTreeIsRead_AnUntrackedFileAddsNothing_AnEditedOneChangesNothing()
    {
        await StageAsync("package.json", "{\"committed\":true}\n");
        await _repo.CommitAsync("c1");
        await _repo.WriteAsync("package.json", "{\"edited\":true}\n");
        await _repo.WriteAsync("Cargo.toml", "untracked\n");
        Directory.CreateDirectory(Path.Combine(_repo.Path, "ignored"));
        await _repo.WriteAsync("ignored/composer.json", "{}\n");

        var set = Found(await ReadAtHeadAsync());

        set.Files.Should().ContainSingle().Which.Text.Should().Be("{\"committed\":true}\n");
    }

    [Fact]
    public async Task ACommitWithNoManifest_IsAnEmptySet_NotARefusal()
    {
        await StageAsync("README.md", "documents only\n");
        await _repo.CommitAsync("c1");

        Found(await ReadAtHeadAsync()).Files.Should().BeEmpty();
    }

    // ---- d2: links, submodules, limits ----

    [Fact]
    public async Task AManifestThatIsALink_IsARefusalThatNamesIt_AndIsNeverRead()
    {
        var target = await _repo.StageBlobAsync("elsewhere.txt", Encoding.UTF8.GetBytes("/etc/passwd"));
        await _repo.StageEntryAsync("app/package.json", "120000", target);
        await _repo.CommitIndexAsync("c1");

        var read = await ReadAtHeadAsync();

        read.Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("app/package.json").And.Contain("link").And.Contain(".coai/toolchain.yaml");
    }

    [Fact]
    public async Task ALockFileThatIsALink_IsRefusedToo()
    {
        var target = await _repo.StageBlobAsync("elsewhere.txt", Encoding.UTF8.GetBytes("x"));
        await _repo.StageEntryAsync("package-lock.json", "120000", target);
        await _repo.CommitIndexAsync("c1");

        (await ReadAtHeadAsync()).Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("package-lock.json");
    }

    [Fact]
    public async Task ASubmodule_IsNeverEntered_AndIsNamedInTheNotes()
    {
        await StageAsync("package.json", "{}\n");
        await _repo.StageEntryAsync("vendor/lib", "160000", "0123456789012345678901234567890123456789");
        await _repo.CommitIndexAsync("c1");

        var set = Found(await ReadAtHeadAsync());

        set.Files.Select(file => file.Path).Should().Equal("package.json");
        set.Notes.Should().ContainSingle().Which.Should().Contain("vendor/lib").And.Contain("not entered");
    }

    [Fact]
    public async Task OverFiveHundredManifestFiles_IsARefusal_WithTheNumberAndTheLimit()
    {
        for (var index = 0; index < 501; index++)
        {
            await StageAsync($"p{index}/package.json", "{}\n");
        }

        await _repo.CommitAsync("c1");

        (await ReadAtHeadAsync()).Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("501").And.Contain("500");
    }

    [Fact]
    public async Task AManifestOver256KiB_IsARefusal_WithTheSizeAndTheLimit()
    {
        await _repo.StageBlobAsync("big/package.json", new byte[256 * 1024 + 1]);
        await _repo.CommitAsync("c1");

        (await ReadAtHeadAsync()).Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("big/package.json").And.Contain("262145").And.Contain("262144");
    }

    [Fact]
    public async Task AManifestOf256KiBExactly_IsRead()
    {
        await _repo.StageBlobAsync("big/package.json", Encoding.UTF8.GetBytes(new string(' ', 256 * 1024)));
        await _repo.CommitAsync("c1");

        Found(await ReadAtHeadAsync()).Files.Should().ContainSingle().Which.Size.Should().Be(256 * 1024);
    }

    [Fact]
    public void MoreThanSixteenMiBOfManifestTexts_IsARefusal_WithTheNumberAndTheLimit()
    {
        var entries = Enumerable.Range(0, 70).Select(index => new TreeEntry("100644", "blob", new string('a', 40), 250_000, $"p{index}/package.json")).ToList();

        var selection = ManifestSearch.Select(new TreeListing(entries, string.Empty), ToolchainDefaults.Current);

        selection.Should().BeOfType<ManifestSelection.Refused>().Which.Sentence.Should().Contain("17500000").And.Contain("16777216");
    }

    [Fact]
    public void ALockFileOfAnySize_IsRecordedNotRead_SoItsSizeIsNoLimit()
    {
        var entries = new[] { new TreeEntry("100644", "blob", new string('a', 40), 50_000_000, "package-lock.json"), new TreeEntry("100644", "blob", new string('b', 40), 10, "package.json") };

        var selection = ManifestSearch.Select(new TreeListing(entries, string.Empty), ToolchainDefaults.Current);

        selection.Should().BeOfType<ManifestSelection.Selected>().Which.Candidates.Select(candidate => candidate.Path).Should().Equal("package-lock.json", "package.json");
    }

    [Fact]
    public void AListingThatDidNotParse_IsARefusal_NotAnEmptySet()
    {
        var selection = ManifestSearch.Select(new TreeListing([], "100644 blob zzz"), ToolchainDefaults.Current);

        selection.Should().BeOfType<ManifestSelection.Refused>().Which.Sentence.Should().Contain("cannot read").And.NotContain("zzz");
    }

    [Fact]
    public async Task AListingOverThirtyTwoMiB_IsARefusal_WithTheLimit()
    {
        var launcher = new Scripted((request, _) => request.Arguments.Contains("cat-file") ? new ProcessResult(0, string.Empty, string.Empty, false) : new ProcessResult(0, "100644 blob " + new string('a', 40) + "      10\tpackage.json\0", string.Empty, false, Truncated: true));

        var read = await new ManifestsAtCommit(new GitHistory(launcher)).ReadAsync(_repo.Path, new string('a', 40), ToolchainDefaults.Current);

        read.Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("33554432").And.Contain(".coai/toolchain.yaml");
    }

    // ---- d3: a commit that is not there, a git that does not answer, a bad path ----

    [Fact]
    public async Task ACommitThatIsNotInTheRepository_IsARefusal_NotAnEmptySet()
    {
        await StageAsync("package.json", "{}\n");
        await _repo.CommitAsync("c1");

        var read = await Reader.ReadAsync(_repo.Path, new string('0', 40), ToolchainDefaults.Current);

        read.Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain(new string('0', 40)).And.Contain("not in the repository");
    }

    [Theory]
    [InlineData("--upload-pack=x")]
    [InlineData("HEAD")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task ARevisionThatIsNotACommitId_IsARefusal_AndNoProcessIsStarted(string sha)
    {
        var launcher = new Scripted((_, _) => throw new InvalidOperationException("no process may start for a value that could be an option"));

        var read = await new ManifestsAtCommit(new GitHistory(launcher)).ReadAsync(_repo.Path, sha, ToolchainDefaults.Current);

        read.Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("not a commit id");
    }

    [Fact]
    public async Task AGitThatDoesNotAnswer_IsARefusal_NotAnEmptySet()
    {
        var launcher = new Scripted((_, _) => new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true));

        var read = await new ManifestsAtCommit(new GitHistory(launcher)).ReadAsync(_repo.Path, new string('a', 40), ToolchainDefaults.Current);

        read.Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("did not answer");
    }

    [Fact]
    public async Task AGitThatAnswersTheListingBadly_IsARefusal()
    {
        var launcher = new Scripted((request, _) => request.Arguments.Contains("ls-tree") ? new ProcessResult(128, string.Empty, "fatal", false) : new ProcessResult(0, string.Empty, string.Empty, false));

        var read = await new ManifestsAtCommit(new GitHistory(launcher)).ReadAsync(_repo.Path, new string('a', 40), ToolchainDefaults.Current);

        read.Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("could not list");
    }

    [Fact]
    public async Task AManifestWhoseBlobGitCannotRead_IsARefusal()
    {
        await StageAsync("package.json", "{}\n");
        await _repo.CommitAsync("c1");
        var head = await _repo.HeadAsync();
        var real = new ProcessLauncher();
        var launcher = new Scripted((request, ct) => request.Arguments.Contains("blob") ? new ProcessResult(128, string.Empty, "fatal: bad object", false) : real.RunAsync(request, ct).GetAwaiter().GetResult());

        var read = await new ManifestsAtCommit(new GitHistory(launcher)).ReadAsync(_repo.Path, head, ToolchainDefaults.Current);

        read.Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("package.json").And.Contain("could not read");
    }

    [Fact]
    public async Task APathWithAControlCharacter_IsARefusalThatNamesIt()
    {
        await _repo.StageBlobAsync("odd\nname/package.json", Encoding.UTF8.GetBytes("{}\n"));
        await _repo.CommitAsync("c1");

        var read = await ReadAtHeadAsync();

        read.Should().BeOfType<ManifestRead.Refused>().Which.Sentence.Should().Contain("a path with unusual characters").And.Contain("blob ").And.NotContain("odd");
    }

    [Theory]
    [InlineData("/abs/package.json")]
    [InlineData("C:/x/package.json")]
    public void APathThatIsNotAPlainRelativeOne_IsARefusal(string path)
    {
        var selection = ManifestSearch.Select(new TreeListing([new TreeEntry("100644", "blob", new string('a', 40), 5, path)], string.Empty), ToolchainDefaults.Current);

        selection.Should().BeOfType<ManifestSelection.Refused>().Which.Sentence.Should().Contain("not a path inside the repository");
    }

    [Theory]
    [InlineData("../package.json")]
    [InlineData("a/../package.json")]
    [InlineData("/abs/package.json")]
    [InlineData("C:/x/package.json")]
    [InlineData("a/b\u0001c/package.json")]
    public void ThePathRule_RefusesAParentAnAbsoluteAndADriveFormAndAControlCharacter(string path)
    {
        ManifestSearch.PathProblem(path).Should().NotBeEmpty();
    }

    // ---- The pattern matcher the names go through ----

    [Theory]
    [InlineData("*.csproj", "App.csproj", true)]
    [InlineData("*.csproj", "App.csproj.bak", false)]
    [InlineData("*.csproj", "csproj", false)]
    [InlineData(".prettierrc*", ".prettierrc", true)]
    [InlineData(".prettierrc*", ".prettierrc.json", true)]
    [InlineData("prettier.config.*", "prettier.config.mjs", true)]
    [InlineData("prettier.config.*", "prettier.config", false)]
    [InlineData("package.json", "Package.json", false)]
    [InlineData("package.json", "package.json", true)]
    public void ANameMatchesAPatternWithOneStar_OrExactly_AndOrdinal(string pattern, string name, bool expected)
    {
        ManifestSearch.Matches(pattern, name).Should().Be(expected);
    }

    private sealed class Scripted(Func<ProcessRequest, CancellationToken, ProcessResult> answer) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => Task.FromResult(answer(request, ct));
    }
}

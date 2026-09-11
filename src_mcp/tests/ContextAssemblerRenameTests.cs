using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The two shapes <c>git diff --numstat</c>'s third column takes that are NOT file paths.
/// </summary>
/// <remarks>
/// <para>A fixture repository of its own rather than more files in
/// <see cref="ContextAssemblerTests"/>'s: these need their own commits, and loading the shared
/// fixture with them would change what every exclusion test there counts.</para>
/// <para>Found by the product audit of 2026-09-09 (finding 4) and reproduced here from git rather
/// than described: the reviewer saw the file NAMED in its list and nothing under it, with no error
/// anywhere. In Fast mode there is nothing else to read, so the reviewer judged a change it could
/// not see — which is the one thing this product exists not to do.</para>
/// </remarks>
public sealed class ContextAssemblerRenameTests : IAsyncLifetime
{
    /// <summary>Enough lines that git's rename detection fires on a small edit.</summary>
    /// <remarks>
    /// A one-line file whose one line changes is 0 % similar, so git records an add and a delete and
    /// the defect never appears. The first attempt at this test used one and passed against the
    /// unfixed code — the fixture has to be similar enough to BE a rename.
    /// </remarks>
    private const int LinesEnoughToBeRecognisedAsARename = 40;

    /// <summary>A name git C-quotes under the default <c>core.quotePath</c>.</summary>
    private const string OutsideAscii = "файл.cs";

    private readonly ProcessLauncher _launcher = new();
    private readonly ContextAssembler _assembler;
    private string _repo = string.Empty;

    public ContextAssemblerRenameTests() => _assembler = new ContextAssembler(_launcher);

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-rename-").FullName;
        await Git("init", "-b", "main");
        Directory.CreateDirectory(Path.Combine(_repo, "src"));

        await File.WriteAllTextAsync(
            Path.Combine(_repo, "src", "old.cs"),
            string.Concat(Enumerable.Range(0, LinesEnoughToBeRecognisedAsARename).Select(i => $"    var line{i} = {i};\n")));
        await File.WriteAllTextAsync(Path.Combine(_repo, "src", OutsideAscii), "before\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "src", "dead.cs"), "removed on the branch\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");

        // A rename WITH an edit: the shape a refactor actually produces, and the one whose diff is
        // worth reading. A pure rename has nothing under it to lose.
        await Git("mv", "src/old.cs", "src/new.cs");
        await File.AppendAllTextAsync(Path.Combine(_repo, "src", "new.cs"), "    var addedByTheBranch = 1;\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "src", OutsideAscii), "after\nand a second line\n");
        await Git("rm", "src/dead.cs");
        await Git("add", ".");
        await Git("commit", "-m", "a refactor that renames");
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
    public async Task ARenamedAndEditedFileStillCarriesItsDiff()
    {
        // numstat prints a rename as `src/{old.cs => new.cs}`, which is not a path. Handed back to
        // git as a pathspec it matches nothing, and the file arrives with an empty Text.
        var renamed = await FileNamed("new.cs");

        renamed.Text.Should().NotBeEmpty("a renamed file's change is exactly what a refactor's review is about");
        renamed.Text.Should().Contain("addedByTheBranch", "the edit under the rename is the part worth reading");
        renamed.Text.Should().Contain("old.cs", "and the reviewer has to be able to see where it came from");
    }

    [Fact]
    public async Task AFileNamedOutsideAsciiStillCarriesItsDiff()
    {
        // Under the default core.quotePath, numstat prints this one C-quoted with escapes —
        // `"src/\321\204..."` — which is not a path either.
        var unicode = await FileNamed(OutsideAscii);

        unicode.Text.Should().NotBeEmpty();
        unicode.Text.Should().Contain("and a second line");
    }

    [Fact]
    public async Task ADeletedFileIsStillADeletion()
    {
        // The regression guard the other two need: a parser rewritten to understand renames must not
        // lose the ordinary records around them, and a delete is the one whose counts look most
        // like a rename's.
        var deleted = await FileNamed("dead.cs");

        deleted.Text.Should().Contain("removed on the branch");
    }

    [Fact]
    public async Task EveryChangedFileIsAccountedFor()
    {
        // Three files, and NOT four: a rename is one file that moved, never an add beside a delete.
        // Without this the two tests above would pass on a parser that emitted both halves.
        var files = await Collect();

        files.Select(f => Path.GetFileName(f.Path))
            .Should().BeEquivalentTo(["new.cs", OutsideAscii, "dead.cs"]);
    }

    private async Task<FileDiff> FileNamed(string name)
    {
        var files = await Collect();

        return files.Should().ContainSingle(f => Path.GetFileName(f.Path) == name,
            "the collected diff should name it once — it collected: {0}",
            string.Join(", ", files.Select(f => f.Path))).Subject;
    }

    private async Task<IReadOnlyList<FileDiff>> Collect() =>
        (await _assembler.CollectAsync(_repo, "main", "feature", ct: TestContext.Current.CancellationToken)).Files;

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git",
            ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args],
            _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }
}

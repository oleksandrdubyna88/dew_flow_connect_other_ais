using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The deletion guard for <c>coai-wt-*</c> trees: a tree is ours when its REAL path is under the REAL storage root.
/// </summary>
/// <remarks>
/// <para><b>The defect (2026-11-01).</b> The guard OR-ed the lexical <c>GetFullPath</c> of a path with its resolved
/// form, so a name lexically under the root that resolved outside passed — and <c>EraseAsync</c> then ran
/// <c>git worktree remove -f -f</c> and a recursive delete on it. The resolver also gave up after 16 links and
/// answered with where it stopped, which is a fail-open.</para>
/// <para>Observed through <see cref="WorktreeManager.ReleaseAsync"/>, which is how a lease gives its tree back: a
/// canary file in the OUTSIDE folder is the assertion that nothing outside was touched.</para>
/// </remarks>
public sealed class AWorktreeIsOursOnlyByItsRealPathTests : IDisposable
{
    private const string Canary = "canary.txt";

    private readonly TempDir _work = TempDir.For("wtguard-");
    private readonly string _storage;
    private readonly string _outside;
    private readonly string _repo;
    private readonly WorktreeManager _manager;
    private readonly List<string> _notes = [];

    public AWorktreeIsOursOnlyByItsRealPathTests()
    {
        _storage = Directory.CreateDirectory(_work.At("storage")).FullName;
        _outside = Directory.CreateDirectory(_work.At("outside")).FullName;
        _repo = Directory.CreateDirectory(_work.At("repo")).FullName;
        _manager = new WorktreeManager(new ProcessLauncher(), _storage, _notes.Add);
    }

    public void Dispose() => _work.Dispose();

    private void Link(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }
    }

    private string CanaryIn(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Canary);
        File.WriteAllText(file, "must survive");

        return file;
    }

    [Fact]
    public async Task ANameLexicallyUnderTheRoot_WhoseParentIsALinkOutside_IsNotOursToDelete()
    {
        Link(Path.Combine(_storage, "sub"), _outside);
        var canary = CanaryIn(Path.Combine(_outside, "coai-wt-x"));

        await _manager.ReleaseAsync(_repo, Path.Combine(_storage, "sub", "coai-wt-x"));

        File.Exists(canary).Should().BeTrue("the tree is outside the storage root, whatever its spelling says");
    }

    [Fact]
    public async Task ATreeThatIsItselfALink_IsNotOursToDelete()
    {
        var canary = CanaryIn(Path.Combine(_outside, "coai-wt-real"));
        var leaf = Path.Combine(_storage, "coai-wt-leaf");
        Link(leaf, Path.Combine(_outside, "coai-wt-real"));

        await _manager.ReleaseAsync(_repo, leaf);

        File.Exists(canary).Should().BeTrue();
        new DirectoryInfo(leaf).LinkTarget.Should().NotBeNull("a leaf link is not ours, so it is not even unlinked");
    }

    [Fact]
    public async Task AParentLinkChainLongerThanSixteen_IsNotOursEither_NotAFailOpen()
    {
        // The old resolver stopped after 16 links and answered where it had got to. 17 links, the last of them
        // leaving the root, must not turn into "ours".
        var previous = _outside;
        for (var i = 20; i >= 1; i--)
        {
            var hop = Path.Combine(_storage, $"hop{i}");
            Link(hop, previous);
            previous = hop;
        }

        var canary = CanaryIn(Path.Combine(_outside, "coai-wt-deep"));

        await _manager.ReleaseAsync(_repo, Path.Combine(_storage, "hop1", "coai-wt-deep"));

        File.Exists(canary).Should().BeTrue();
    }

    [Fact]
    public async Task ATreeReallyUnderTheRoot_IsStillOurs()
    {
        var tree = Path.Combine(_storage, "coai-wt-mine");
        var canary = CanaryIn(tree);

        await _manager.ReleaseAsync(_repo, tree);

        File.Exists(canary).Should().BeFalse("a plain tree under the root is exactly what the guard lets through");
    }
}

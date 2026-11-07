using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the deletion guard JUDGES is what the erase ACTS ON: the resolved tree, never the spelling that was handed in.
/// </summary>
/// <remarks>
/// <para>The guard resolved a path and judged it; <c>EraseAsync</c> then ran <c>git worktree unlock</c>, <c>git worktree remove -f -f</c>, the delete,
/// the move-aside and the owner-marker delete on the SPELLED path. A link component swapped between the two reads would send
/// them where the guard never looked. The tree is erased by the path that was judged.</para>
/// <para>Observed through the git commands a recording launcher sees, and the folder that is gone.</para>
/// </remarks>
public sealed class AWorktreeIsErasedAtItsRealPathTests : IDisposable
{
    private readonly TempDir _work = TempDir.For("wterase-");
    private readonly List<ProcessRequest> _seen = [];
    private readonly string _storage;
    private readonly string _repo;

    public AWorktreeIsErasedAtItsRealPathTests()
    {
        _storage = Directory.CreateDirectory(_work.At("storage")).FullName;
        _repo = Directory.CreateDirectory(_work.At("repo")).FullName;
    }

    public void Dispose() => _work.Dispose();

    private sealed class Recording(List<ProcessRequest> seen) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            seen.Add(request);

            return Task.FromResult(new ProcessResult(1, string.Empty, "not a worktree", TimedOut: false));
        }
    }

    [Fact]
    public async Task ASpelledPathWhoseResolutionDiffers_IsErasedAtTheResolvedTreeOnly()
    {
        var real = WorktreePaths.Real(_storage);
        var sibling = Directory.CreateDirectory(Path.Combine(_storage, "sib")).FullName;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_storage, "lnk"), sibling);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }

        var tree = Directory.CreateDirectory(Path.Combine(sibling, "coai-wt-a")).FullName;
        File.WriteAllText(Path.Combine(tree, "canary.txt"), "goes with the tree");
        var spelled = Path.Combine(_storage, "lnk", "coai-wt-a");
        var resolved = Path.Combine(real, "sib", "coai-wt-a");

        await new WorktreeManager(new Recording(_seen), _storage).ReleaseAsync(_repo, spelled);

        var named = _seen.SelectMany(request => request.Arguments).Where(arg => arg.Contains("coai-wt-a", StringComparison.Ordinal)).ToList();
        named.Should().NotBeEmpty("git was asked to unlock and remove the tree");
        named.Should().OnlyContain(arg => arg == resolved, "every command is aimed at the path that was judged, not the spelling");
        Directory.Exists(tree).Should().BeFalse("the resolved tree is the one erased");
    }
}

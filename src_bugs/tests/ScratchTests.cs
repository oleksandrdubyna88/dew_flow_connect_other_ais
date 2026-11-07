using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// <see cref="Scratch.Delete"/> removes a folder a test made and nothing beside it (E5 S3, Opus review 6). A scratch folder has no claim; a sibling <c>&lt;dir&gt;.owner</c> is somebody
/// else's file, and the leftovers helper's own teardown deletes exactly such a file, so this suite does not use that teardown for folders it did not claim.
/// </summary>
public sealed class ScratchTests : IDisposable
{
    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    [Fact]
    public void Delete_RemovesTheFolder_AndLeavesASiblingOwnerFileAlone()
    {
        var root = _owned.Dir("s3-scratch");
        var folder = Path.Combine(root, "scratch");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "inside.txt"), "x");
        var sibling = folder + ".owner";
        File.WriteAllText(sibling, "somebody else's claim");

        Scratch.Delete(folder);

        Directory.Exists(folder).Should().BeFalse("the scratch folder goes, with what was inside");
        File.ReadAllText(sibling).Should().Be("somebody else's claim", "a file beside the folder is not the folder's: it carries no claim of this test");
    }
}

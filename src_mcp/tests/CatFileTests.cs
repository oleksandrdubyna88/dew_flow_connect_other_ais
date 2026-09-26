using System.Text;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Feature;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The two <c>git cat-file</c> answers the feature outline reads: sizes before content, and content cut
/// by the size git declared — with a non-UTF-8 blob reported, not allowed to mis-slice the ones after it.
/// </summary>
public sealed class CatFileTests
{
    private const string OidA = "1111111111111111111111111111111111111111";
    private const string OidB = "2222222222222222222222222222222222222222";

    [Fact]
    public void BatchCheck_ReadsFoundObjects_AndAMissingName_EvenWithSpacesInIt()
    {
        var output = $"{OidA} blob 12\nabc123:src/a b c.cs missing\n{OidB} commit 0\n";

        var objects = CatFile.ParseCheck(output, 3);

        objects[0].Should().Be(new GitObject(OidA, "blob", 12));
        objects[1].Exists.Should().BeFalse();
        objects[2].IsBlob.Should().BeFalse("a submodule is a commit, not a file");
    }

    [Fact]
    public void BatchCheck_ThatAnsweredTheWrongNumberOfLines_IsRefused() =>
        FluentActions.Invoking(() => CatFile.ParseCheck($"{OidA} blob 12\n", 2)).Should().Throw<ContextException>().WithMessage("*1 line(s) for 2 name(s)*");

    [Fact]
    public void Batch_CutsEachBlobAtItsDeclaredByteSize_MultiByteCharactersIncluded()
    {
        var first = "class Café {}\n";
        var second = "x\ny";
        var output = $"{OidA} blob {Encoding.UTF8.GetByteCount(first)}\n{first}\n{OidB} blob {Encoding.UTF8.GetByteCount(second)}\n{second}\n";

        var blobs = CatFile.ParseBatch(output, [new GitObject(OidA, "blob", Encoding.UTF8.GetByteCount(first)), new GitObject(OidB, "blob", Encoding.UTF8.GetByteCount(second))]);

        blobs.Should().Equal(new BlobText(first, true), new BlobText(second, true));
    }

    [Fact]
    public void ABlobThatIsNotUtf8_IsReportedNotAligned_AndTheNextBlobIsStillRead()
    {
        // One Latin-1 byte (0xE9) decodes to U+FFFD, which re-encodes as THREE bytes: the declared size no
        // longer ends the blob where git's newline is.
        var declared = 6;
        var output = $"{OidA} blob {declared}\ncaf�!!\n{OidB} blob 2\nok\n";

        var blobs = CatFile.ParseBatch(output, [new GitObject(OidA, "blob", declared), new GitObject(OidB, "blob", 2)]);

        blobs[0].Aligned.Should().BeFalse();
        blobs[1].Should().Be(new BlobText("ok", true));
    }

    [Fact]
    public void Batch_WhoseHeaderIsNotTheOneAskedFor_IsRefused() =>
        FluentActions.Invoking(() => CatFile.ParseBatch($"{OidB} blob 2\nok\n", [new GitObject(OidA, "blob", 2)]))
            .Should().Throw<ContextException>().WithMessage($"*expected '{OidA} blob 2' at object 1 of 1*");
}

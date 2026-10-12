using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one repository-root helper finds the folder holding both halves, and FAILS — naming where it looked — when there
/// is none, rather than answering some other folder and letting a source-reading test read the wrong files.
/// </summary>
public sealed class RepoPathsTests : IDisposable
{
    private readonly string _tree = Directory.CreateTempSubdirectory("coai-repopaths-").FullName;

    [Fact]
    public void ARootHoldingBothHalves_IsFound_FromDeepBelowIt()
    {
        Directory.CreateDirectory(Path.Combine(_tree, "src_mcp"));
        Directory.CreateDirectory(Path.Combine(_tree, "src_vs_code"));
        var deep = Directory.CreateDirectory(Path.Combine(_tree, "src_mcp", "tests", "bin", "Debug")).FullName;

        RepoPaths.RootAbove(deep).Should().Be(new DirectoryInfo(_tree).FullName);
    }

    [Fact]
    public void NoFolderHoldingBothHalves_Throws_NamingWhereTheSearchBegan()
    {
        // One half only, so a marker-of-one rule (the old copies) would have stopped here.
        Directory.CreateDirectory(Path.Combine(_tree, "src_mcp"));
        var deep = Directory.CreateDirectory(Path.Combine(_tree, "src_mcp", "tests", "bin")).FullName;

        var search = () => RepoPaths.RootAbove(deep);

        search.Should().Throw<InvalidOperationException>()
            .WithMessage($"*was not found above {deep}*", "a test that reads sources must fail here, not read some other folder");
    }

    public void Dispose() => Directory.Delete(_tree, recursive: true);
}

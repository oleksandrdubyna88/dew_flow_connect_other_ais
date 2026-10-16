using System.Text;
using Xunit;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// What a failed round-tree <c>worktree add</c> says — pure text in, text out, so it runs on every OS.
/// </summary>
/// <remarks>
/// The stderr samples are git 2.55 for Windows, copied from the probe in
/// <c>todo/PLAN_review_trees_survive_long_paths.md</c> (P2, P3, P11) with the long paths shortened: the
/// bare <c>fatal:</c> line of P2 and P11 names the INDEX ("Could not reset index file"), which is the
/// sentence that made a path problem read as a corrupt repository.
/// </remarks>
public sealed class WorktreeFailureTests
{
    private static readonly string Tree = OperatingSystem.IsWindows()
        ? @"C:\Users\someone\AppData\Local\coai-mcp\round-worktrees\coai-wt-1a2b3c4d-r1-5e6f7a8b"
        : "/home/someone/coai-mcp/round-worktrees/coai-wt-1a2b3c4d-r1-5e6f7a8b";

    private const string ResetFatal = "fatal: Could not reset index file to revision 'HEAD'.";

    [Fact]
    public void AFileThatIsTooLong_NamesItsPathItsLengthAndGitsReason()
    {
        const string relative = "src/deep/folder/file-with-a-long-name.txt";
        var stderr = $"""
            warning: unable to access 'src/deep/.gitattributes': Filename too long
            error: unable to create file {relative}: Filename too long
            {ResetFatal}
            """;

        var said = WorktreeFailure.Sentence(stderr, Tree);

        var path = Joined(relative);
        said.Should().Contain($"'{path}'").And.Contain($"({path.Length} characters)");
        said.Should().Contain("Filename too long");
        said.Should().Contain("COAI_ROUND_WORKTREES", "a shorter local root is the way out an operator has");
        said.Should().Contain(ResetFatal, "git's own verdict stays, as the last word rather than the only one");
    }

    [Fact]
    public void ADirectoryThatIsTooLong_IsNamedWithoutItsQuotes()
    {
        const string relative = "deep-dir-000/deep-dir-001/deep-dir-002";
        var stderr = $"fatal: cannot create directory at '{relative}': Filename too long";

        var said = WorktreeFailure.Sentence(stderr, Tree);

        var path = Joined(relative);
        said.Should().Contain($"'{path}'").And.Contain($"({path.Length} characters)");
        said.Should().NotContain($"''{path}''");
        said.Should().Contain("COAI_ROUND_WORKTREES");
    }

    [Fact]
    public void TheWindowsLimitIsNamedOnWindowsOnly_BecauseNobodyMeasuredItElsewhere()
    {
        var said = WorktreeFailure.Sentence("error: unable to create file a/b.txt: Filename too long", Tree);

        if (OperatingSystem.IsWindows())
        {
            said.Should().Contain(WorktreeFailure.WindowsMaxPathUnits.ToString(System.Globalization.CultureInfo.InvariantCulture));
            said.Should().Contain("core.longpaths=true", "the reader must know coai already asked for long paths");
        }
        else
        {
            said.Should().NotContain("259", "an unmeasured number is not filled in for a platform nobody probed");
            said.Should().NotContain("core.longpaths");
        }
    }

    [Fact]
    public void TheWindowsLimitIsOneNamedConstant_259()
    {
        WorktreeFailure.WindowsMaxPathUnits.Should().Be(259, "P1 accepted 259 units and P2 refused 260");
    }

    /// <summary>A name longer than NTFS allows is not cured by a shorter root, so no advice about a root.</summary>
    [Fact]
    public void AnotherReason_NamesThePathAndTheReason_WithoutTheLimitOrTheRootAdvice()
    {
        var component = new string('c', 256);
        var stderr = $"""
            error: unable to create file a/{component}.txt: Invalid argument
            {ResetFatal}
            """;

        var said = WorktreeFailure.Sentence(stderr, Tree);

        var path = Joined($"a/{component}.txt");
        said.Should().Contain($"'{path}'").And.Contain($"({path.Length} characters)");
        said.Should().Contain("Invalid argument");
        said.Should().NotContain("COAI_ROUND_WORKTREES");
        said.Should().NotContain("259");
    }

    [Fact]
    public void WhenSeveralFilesFail_TheFirstNamesThePath()
    {
        var stderr = """
            error: unable to create file first/one.txt: Filename too long
            error: unable to create file second/two.txt: Filename too long
            fatal: Could not reset index file to revision 'HEAD'.
            """;

        var said = WorktreeFailure.Sentence(stderr, Tree);

        said.Should().Contain("one.txt").And.NotContain("two.txt");
    }

    [Theory]
    [InlineData("fatal: invalid reference: nope")]
    [InlineData("warning: something odd\nfatal: not a git repository\n")]
    [InlineData("no verdict at all, just words")]
    [InlineData("")]
    public void AFailureWithoutAPathLine_ReadsExactlyAsItAlwaysDid(string stderr)
    {
        WorktreeFailure.Sentence(stderr, Tree).Should().Be(WorktreeManager.FatalLine(stderr));
    }

    /// <summary>Windows counts UTF-16 code units, not bytes: one <c>é</c> is one unit and two bytes.</summary>
    [Fact]
    public void TheLengthIsCountedInCharacters_NotInBytes()
    {
        const string relative = "dossier/résumé-éééé.txt";
        var stderr = $"error: unable to create file {relative}: Filename too long";

        var said = WorktreeFailure.Sentence(stderr, Tree);

        var path = Joined(relative);
        Encoding.UTF8.GetByteCount(path).Should().BeGreaterThan(path.Length, "the fixture must tell characters from bytes");
        said.Should().Contain($"({path.Length} characters)");
        said.Should().NotContain($"({Encoding.UTF8.GetByteCount(path)} characters)");
    }

    private static string Joined(string relative) =>
        Path.Combine(Tree, OperatingSystem.IsWindows() ? relative.Replace('/', '\\') : relative);
}

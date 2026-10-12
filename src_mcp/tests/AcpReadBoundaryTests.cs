using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The lexical read boundary (<see cref="FolderReadBoundary"/>): the spellings that are refused by SHAPE before
/// normalisation can tidy them, the names refused at any depth, and the link on the way that is refused on disk.
/// </summary>
public sealed class AcpReadBoundaryTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-acp-boundary-");

    public void Dispose() => _dir.Dispose();

    private string Root => _dir.At("snap");

    private FolderReadBoundary Boundary() => new(Root, KimiAcpProfile.Exclusions);

    private string Write(string relative, string text = "marker")
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);

        return path;
    }

    [Fact]
    public void AFileInsideTheRootIsServed_AtAnyDepth()
    {
        var top = Write("marker.txt", "EGRET-5");
        var deep = Write(Path.Combine("src", "a", "b.cs"), "class B {}");

        Boundary().Read(top).Should().Be(AcpReadAnswer.Of("EGRET-5"));
        Boundary().Read(deep).Should().Be(AcpReadAnswer.Of("class B {}"));
    }

    [Fact]
    public void AFileOutsideTheRootIsRefused_AndNotQuoted()
    {
        var outside = _dir.At("secret.txt");
        File.WriteAllText(outside, "LYNX-404");

        var answer = Boundary().Read(outside);

        answer.Served.Should().BeFalse();
        answer.Refusal.Should().Contain("outside the working folder");
        answer.Text.Should().BeEmpty();
    }

    [Fact]
    public void AnEscapeThroughDotDotIsRefused_EvenWhenItStartsInsideTheRoot()
    {
        Write("marker.txt");
        File.WriteAllText(_dir.At("secret.txt"), "LYNX-404");

        Boundary().Read(Path.Combine(Root, "sub", "..", "..", "secret.txt")).Served.Should().BeFalse("the normalised path is what is judged");
    }

    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("agents.md")]
    [InlineData("deep/er/AGENTS.md")]
    [InlineData(".kimi-code/config.toml")]
    [InlineData("deep/.kimi-code/AGENTS.md")]
    [InlineData(".vibe/hooks.toml")]
    [InlineData(".agents/skills/x/SKILL.md")]
    [InlineData(".git/config")]
    [InlineData("deep/.git/HEAD")]
    public void AnExcludedNameIsRefused_AtAnyDepth(string relative)
    {
        var path = Write(relative.Replace('/', Path.DirectorySeparatorChar), "instructions");

        var answer = Boundary().Read(path);

        answer.Served.Should().BeFalse(relative);
        answer.Refusal.Should().Contain("not served");
    }

    [Fact]
    public void TheExclusionFollowsThePlatformsCaseRule()
    {
        var path = Write("Agents.MD", "instructions");

        var answer = Boundary().Read(path);

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            answer.Served.Should().BeFalse("the filesystem folds case, so the exclusion must too");
        }
        else
        {
            answer.Served.Should().BeTrue("on a case-sensitive filesystem Agents.MD is a different file from AGENTS.md and agents.md");
        }
    }

    [Fact]
    public void AnAgentsSkillsFolderIsOnlyThePair_NotEitherNameAlone()
    {
        var agents = Write(Path.Combine(".agents", "rules", "x.md"), "a rule");
        var skills = Write(Path.Combine("skills", "x.md"), "not under .agents");

        Boundary().Read(agents).Served.Should().BeTrue(".agents/rules is not .agents/skills");
        Boundary().Read(skills).Served.Should().BeTrue();
    }

    [Fact]
    public void AMissingFile_ADirectory_AndARelativePath_AreRefused()
    {
        Directory.CreateDirectory(Path.Combine(Root, "folder"));

        Boundary().Read(Path.Combine(Root, "nowhere.txt")).Refusal.Should().Contain("no such file");
        Boundary().Read(Path.Combine(Root, "folder")).Refusal.Should().Contain("a directory");
        Boundary().Read("marker.txt").Refusal.Should().Contain("not an absolute path");
        Boundary().Read(string.Empty).Served.Should().BeFalse();
    }

    /// <summary>The Windows spellings, refused by shape — each would resolve somewhere the root is not.</summary>
    [Theory]
    [InlineData(@"\\server\share\snap\marker.txt", "UNC or device")]
    [InlineData(@"\\?\C:\snap\marker.txt", "UNC or device")]
    [InlineData(@"\\.\C:\snap\marker.txt", "UNC or device")]
    [InlineData(@"//server/share/marker.txt", "UNC or device")]
    [InlineData(@"C:marker.txt", "not an absolute path")]
    [InlineData(@"\snap\marker.txt", "not an absolute path")]
    [InlineData(@"C:\snap\marker.txt:hidden", "alternate data stream")]
    [InlineData(@"C:\snap\marker.txt.", "dot or a space")]
    [InlineData(@"C:\snap\sub.\marker.txt", "dot or a space")]
    [InlineData(@"C:\snap\marker.txt ", "dot or a space")]
    public void AWindowsSpellingThatResolvesElsewhere_IsRefusedByShape(string spelling, string refusal)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Path.GetFullPath reads these as relative on POSIX — refused there too, but by the absolute-path rule.
            FolderReadBoundary.Refusal("/snap/", spelling, KimiAcpProfile.Exclusions, StringComparison.Ordinal).Should().NotBeEmpty(spelling);

            return;
        }

        FolderReadBoundary.Refusal(@"C:\snap\", spelling, KimiAcpProfile.Exclusions, StringComparison.OrdinalIgnoreCase)
            .Should().Contain(refusal, spelling);
    }

    [Fact]
    public void TheRootItselfIsNotAFile_AndAPathBesideItWithTheSamePrefixIsOutside()
    {
        Directory.CreateDirectory(Root);
        var sibling = _dir.At("snapshot-other", "marker.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sibling)!);
        File.WriteAllText(sibling, "x");

        Boundary().Read(Root).Served.Should().BeFalse();
        Boundary().Read(sibling).Refusal.Should().Contain("outside", "`snap` is a prefix of `snapshot-other` by characters, not by components");
    }

    [Fact]
    public async Task APathThatResolvesThroughALink_IsRefused_EvenWhenItReadsInsideTheRoot()
    {
        var target = _dir.At("elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "marker.txt"), "LYNX-404");
        Directory.CreateDirectory(Root);
        var link = Path.Combine(Root, "linked");
        await DirectoryLink.MakeAsync(new ProcessLauncher(), link, target);
        try
        {
            var answer = Boundary().Read(Path.Combine(link, "marker.txt"));

            answer.Served.Should().BeFalse("a link inside the root reaches outside it");
            answer.Refusal.Should().Contain("link").And.NotContain("LYNX-404");
        }
        finally
        {
            DirectoryLink.Remove(link);
        }
    }

    [Fact]
    public void AFileOverTheCeilingIsRefused()
    {
        var big = Write("big.bin");
        using (var stream = new FileStream(big, FileMode.Create))
        {
            stream.SetLength(FolderReadBoundary.MaxBytes + 1);
        }

        Boundary().Read(big).Refusal.Should().Contain("ceiling");
    }
}

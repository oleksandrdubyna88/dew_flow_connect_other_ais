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

    /// <summary>
    /// Finding 2 of the security review: Windows keeps an 8.3 alias (<c>VIBE~1</c>) for every long name, and the
    /// exclusions compare long names — so the alias must be refused by its shape.
    /// </summary>
    [Fact]
    public void AnEightDotThreeSpellingOfAnExcludedName_IsRefused()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "8.3 short names exist only on Windows volumes");
        var hooks = Write(Path.Combine(".vibe", "hooks.toml"), "[[hooks]]");
        var shortFolder = ShortPath(Path.Combine(Root, ".vibe"));
        Assert.SkipWhen(
            string.Equals(shortFolder, Path.Combine(Root, ".vibe"), StringComparison.OrdinalIgnoreCase),
            $"8.3 name generation is disabled on this volume (GetShortPathName answered the long name for {Root}), so the alias cannot be made");
        File.ReadAllText(Path.Combine(shortFolder, "hooks.toml")).Should().Be("[[hooks]]", "the alias really reaches the excluded file");

        var answer = Boundary().Read(Path.Combine(shortFolder, "hooks.toml"));

        answer.Served.Should().BeFalse($"{shortFolder} is {hooks} by another spelling");
        answer.Refusal.Should().Contain("8.3");
    }

    /// <summary>The Windows 8.3 alias of the folder — what the shell shows as <c>VIBE~1</c>.</summary>
    private static string ShortPath(string longPath)
    {
        var buffer = new System.Text.StringBuilder(1024);
        var length = GetShortPathNameW(longPath, buffer, (uint)buffer.Capacity);
        length.Should().BePositive($"GetShortPathName failed for {longPath}");

        return buffer.ToString();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, System.Text.StringBuilder shortPath, uint length);

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
    [InlineData(@"C:\snap\VIBE~1\hooks.toml", "8.3")]
    [InlineData(@"C:\snap\src\AGENTS~1.MD", "8.3")]
    [InlineData(@"C:\snap\KIMI-C~1\config.toml", "8.3")]
    public void AWindowsSpellingThatResolvesElsewhere_IsRefusedByShape(string spelling, string refusal)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Path.GetFullPath reads these as relative on POSIX — refused there too, but by the absolute-path rule.
            FolderReadBoundary.Refusal("/snap/", spelling, KimiAcpProfile.Exclusions, StringComparison.Ordinal, shortNames: false).Should().NotBeEmpty(spelling);

            return;
        }

        FolderReadBoundary.Refusal(@"C:\snap\", spelling, KimiAcpProfile.Exclusions, StringComparison.OrdinalIgnoreCase, shortNames: true)
            .Should().Contain(refusal, spelling);
    }

    /// <summary>Where no 8.3 alias exists, a tilde-digit name is just a name — the rule is the flag's, not the platform's.</summary>
    [Fact]
    public void WithoutShortNames_ATildeDigitNameIsJustAName()
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)) + Path.DirectorySeparatorChar;

        FolderReadBoundary.Refusal(root, Path.Combine(Root, "notes~1.txt"), KimiAcpProfile.Exclusions, StringComparison.Ordinal, shortNames: false)
            .Should().BeEmpty("on a filesystem without 8.3 aliases the spelling names only itself");
        FolderReadBoundary.Refusal(root, Path.Combine(Root, "notes~1.txt"), KimiAcpProfile.Exclusions, StringComparison.Ordinal, shortNames: true)
            .Should().Contain("8.3", "with aliases the same spelling may name an excluded folder");
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

    /// <summary>Finding F of the risk consultations: the link check and the open were two operations on a path.</summary>
    [Fact]
    public void ALinkSwappedInBetweenTheCheckAndTheOpen_IsRefused_AndNeverQuoted()
    {
        var inside = Write(Path.Combine("dir", "inner.txt"), "inside");
        var target = _dir.At("elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "inner.txt"), "LYNX-404");
        var boundary = Boundary();
        boundary.BeforeOpen = () =>
        {
            Directory.Delete(Path.Combine(Root, "dir"), recursive: true);
            SnapshotTestKit.LinkNow(Path.Combine(Root, "dir"), target);
        };
        try
        {
            var answer = boundary.Read(inside);

            answer.Served.Should().BeFalse("what was opened is not the file that was checked");
            answer.Text.Should().NotContain("LYNX-404");
            answer.Refusal.Should().NotContain("LYNX-404");
        }
        finally
        {
            DirectoryLink.Remove(Path.Combine(Root, "dir"));
        }
    }

    /// <summary>
    /// The cadence consultation of epics 4-5 (2026-10-12): the root's canonical path was asked for AFTER the open, so a
    /// root renamed aside and replaced by a link between the check and the open made the outside file's handle match the
    /// outside "root" — and its bytes were served. The root's identity is fixed when the boundary is made.
    /// </summary>
    [Fact]
    public void ARootSwappedForALink_BetweenTheCheckAndTheOpen_IsRefused_AndNeverQuoted()
    {
        var inside = Write(Path.Combine("dir", "inner.txt"), "inside");
        var outside = _dir.At("outside-root");
        Directory.CreateDirectory(Path.Combine(outside, "dir"));
        File.WriteAllText(Path.Combine(outside, "dir", "inner.txt"), "ROOT-SWAP-CANARY");
        var aside = _dir.At("snap-aside");
        var boundary = Boundary();
        boundary.BeforeOpen = () =>
        {
            Directory.Move(Root, aside);
            SnapshotTestKit.LinkNow(Root, outside);
        };
        try
        {
            var answer = boundary.Read(inside);

            answer.Served.Should().BeFalse("the root the boundary was made for is not the one the handle names");
            answer.Text.Should().NotContain("ROOT-SWAP-CANARY");
            answer.Refusal.Should().NotContain("ROOT-SWAP-CANARY");
        }
        finally
        {
            DirectoryLink.Remove(Root);
            if (Directory.Exists(aside))
            {
                Directory.Move(aside, Root);
            }
        }
    }

    /// <summary>
    /// The same consultation, on the platform that cannot name a handle (macOS): a link swapped in for the open and the
    /// directory put BACK before the check after it — a walk of the path then finds nothing, so the check must be of what
    /// was opened (its identity), not of the path again. Elsewhere the handle's own name already refuses it.
    /// </summary>
    [Fact]
    public void ALinkSwappedInForTheOpen_AndRemovedBeforeTheCheck_IsRefused()
    {
        var inside = Write(Path.Combine("dir", "inner.txt"), "inside");
        var outside = _dir.At("elsewhere-2");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "inner.txt"), "RESTORED-CANARY");
        var aside = _dir.At("dir-aside");
        var boundary = Boundary();
        boundary.BeforeOpen = () =>
        {
            Directory.Move(Path.Combine(Root, "dir"), aside);
            SnapshotTestKit.LinkNow(Path.Combine(Root, "dir"), outside);
        };
        boundary.AfterOpen = () =>
        {
            DirectoryLink.Remove(Path.Combine(Root, "dir"));
            Directory.Move(aside, Path.Combine(Root, "dir"));
        };
        try
        {
            var answer = boundary.Read(inside);

            answer.Served.Should().BeFalse("what was opened is the outside file, whatever the path says afterwards");
            answer.Text.Should().NotContain("RESTORED-CANARY");
        }
        finally
        {
            // AfterOpen put the real directory back; only a link still standing (a failure before it) is removed.
            if (new DirectoryInfo(Path.Combine(Root, "dir")).LinkTarget is not null)
            {
                DirectoryLink.Remove(Path.Combine(Root, "dir"));
            }

            if (Directory.Exists(aside))
            {
                Directory.Move(aside, Path.Combine(Root, "dir"));
            }
        }
    }

    /// <summary>Finding F: a size checked before the open is a size the file can outgrow.</summary>
    [Fact]
    public void AFileThatGrowsPastTheCeiling_AfterItsSizeWasChecked_IsRefused()
    {
        var growing = Write("grows.bin", "small");
        var boundary = Boundary();
        boundary.BeforeOpen = () =>
        {
            using var grown = new FileStream(growing, FileMode.Open, FileAccess.Write);
            grown.SetLength(FolderReadBoundary.MaxBytes + 1);
        };

        var answer = boundary.Read(growing);

        answer.Served.Should().BeFalse("the read itself is capped, not a size looked at earlier");
        answer.Refusal.Should().Contain("ceiling");
    }
}

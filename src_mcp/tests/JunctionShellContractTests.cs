using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The contract of <c>IJunctionShell.MakeLink</c> (the coai code gate, E5): the link's PARENT folder is created when it is missing — by BOTH real shells, so a fresh owned home's first launch never fails on a folder nobody made.
/// Each real shell runs on its own platform (the Windows junction on Windows, the symbolic link on Linux); the platform that is not this one skips.
/// </summary>
public sealed class JunctionShellContractTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-shell-contract-");

    public void Dispose() => _dir.Dispose();

    private (string Link, string Target) Fresh()
    {
        var target = _dir.At("persons", "credentials");
        Directory.CreateDirectory(target);

        return (_dir.At("owned", "kh-new", "home", "link"), target);
    }

    [Fact]
    public void TheWindowsShell_CreatesTheLinksMissingParents_AndTheLinkReadsBack()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the junction shell is Windows'");
        var (link, target) = Fresh();

        JunctionsNative.Instance.MakeLink(link, target).Should().BeEmpty();

        Directory.Exists(Path.GetDirectoryName(link)).Should().BeTrue("the parent was made by the shell");
        Junctions.Target(link).Should().Be(target);
    }

    [Fact]
    public void TheUnixShell_CreatesTheLinksMissingParents_AndTheLinkReadsBack()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the symbolic-link shell is Linux's");
        var (link, target) = Fresh();

        JunctionsUnix.Instance.MakeLink(link, target).Should().BeEmpty();

        Directory.Exists(Path.GetDirectoryName(link)).Should().BeTrue("a symlink cannot be made in a folder that is not there: the shell makes it");
        Junctions.Target(link).Should().Be(target);
    }
}

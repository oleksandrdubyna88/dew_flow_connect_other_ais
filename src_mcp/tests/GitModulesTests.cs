using CoaiMcp.Runners.Git;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>.gitmodules</c> is a file inside the repository under review, so everything it says is input
/// rather than fact — and one of the two things read out of it (the submodule NAME) decides whether
/// a round stays offline or quietly goes to the network.
/// </summary>
public sealed class GitModulesTests
{
    [Fact]
    public void TheNameAndThePathAreReadSeparately_BecauseGitDoesNotRequireThemToMatch()
    {
        var mounts = GitModules.Parse(
            "[submodule \"dew_flow_conventions\"]\n\tpath = .claude/rules/shared\n\turl = https://example.invalid/c.git\n");

        mounts.Should().ContainSingle();
        mounts[0].Name.Should().Be("dew_flow_conventions");
        mounts[0].Path.Should().Be(".claude/rules/shared");
    }

    [Fact]
    public void ASectionNamedAfterItsPath_IsReadTheSameWay()
    {
        var mounts = GitModules.Parse(
            "[submodule \".claude/rules/shared\"]\n\tpath = .claude/rules/shared\n\turl = https://example.invalid/c.git\n");

        mounts.Should().ContainSingle().Which.Name.Should().Be(".claude/rules/shared");
    }

    [Fact]
    public void SeveralSubmodules_AreAllFound()
    {
        var mounts = GitModules.Parse(
            "[submodule \"rules\"]\n\tpath = .claude/rules/shared\n"
            + "[submodule \"code\"]\n\tpath = external/dew_flow_mcp\n");

        mounts.Select(m => m.Path).Should().Equal([".claude/rules/shared", "external/dew_flow_mcp"]);
    }

    /// <summary>A <c>path</c> outside a submodule section belongs to somebody else's key.</summary>
    [Fact]
    public void APathInAnotherSection_IsNotASubmodule()
    {
        var mounts = GitModules.Parse("[submodule \"rules\"]\n\tpath = a\n[core]\n\tpath = b\n");

        mounts.Select(m => m.Path).Should().Equal(["a"]);
    }

    /// <summary>
    /// The declared path becomes a clone SOURCE under the parent checkout. One that escapes the
    /// checkout is the difference between "the rules beside us" and "any local repository".
    /// </summary>
    [Theory]
    [InlineData("../../elsewhere")]
    [InlineData("a/../../b")]
    [InlineData("/etc/passwd")]
    // Rooted on the platform that WROTE it, which is not the one reading it: a drive path is an
    // ordinary relative directory called "C:" to Path.IsPathRooted on Linux, where CI runs. This
    // case went green on Windows and red there, which is how the guard came to be platform-blind.
    [InlineData("C:/Windows")]
    [InlineData("\\\\server\\share")]
    [InlineData("")]
    public void APathThatCanLeaveTheCheckout_IsNotAMount(string path)
    {
        GitModules.IsSafeMountPath(path).Should().BeFalse();
        GitModules.Parse($"[submodule \"x\"]\n\tpath = {path}\n").Should().BeEmpty();
    }

    /// <summary>
    /// The name lands in a git CONFIG KEY (<c>-c submodule.&lt;name&gt;.url=…</c>), so it is the half
    /// that can set something nobody asked for.
    /// </summary>
    [Theory]
    [InlineData("foo.url=https://evil.example/x")]
    [InlineData("foo\n[core]\nsshCommand")]
    [InlineData("foo bar")]
    [InlineData("")]
    public void ANameThatCouldSpellAnotherConfigKey_IsNotAMount(string name)
    {
        GitModules.IsSafeMountName(name).Should().BeFalse();
        GitModules.Parse($"[submodule \"{name}\"]\n\tpath = ok/here\n").Should().BeEmpty();
    }

    [Fact]
    public void TheNamesThisFamilyActuallyUses_AreAccepted()
    {
        GitModules.IsSafeMountName(".claude/rules/shared").Should().BeTrue();
        GitModules.IsSafeMountName("dew_flow_conventions").Should().BeTrue();
        GitModules.IsSafeMountName("external/dew_flow_mcp").Should().BeTrue();
    }

    [Fact]
    public void AnOrdinaryRelativePath_IsAMount()
    {
        GitModules.IsSafeMountPath(".claude/rules/shared").Should().BeTrue();
        GitModules.IsSafeMountPath("external\\dew_flow_mcp").Should().BeTrue("git writes forward slashes, Windows does not");
    }

    [Fact]
    public void ARepositoryThatDeclaresNothing_HasNoMounts() =>
        GitModules.In(Directory.CreateTempSubdirectory("coai-gitmodules-").FullName).Should().BeEmpty();
}

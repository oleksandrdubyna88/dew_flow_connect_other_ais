using Xunit;
using CoaiMcp;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>What an invocation is, decided before anything happens — pure, so a table.</summary>
public sealed class StartupTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void HelpSpellings_AreHelp(string arg) =>
        Program.Classify([arg]).Should().Be(Program.Startup.Help);

    [Fact]
    public void NoArguments_IsServe() =>
        Program.Classify([]).Should().Be(Program.Startup.Serve);

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    [InlineData("version")]
    public void VersionSpellings_AreVersion(string arg) =>
        Program.Classify([arg]).Should().Be(Program.Startup.Version);

    [Theory]
    [InlineData("--stdio")]
    [InlineData("serve")]
    // `-v` used to be here. It is a real spelling now, so the negative row is a NEAR-MISS instead:
    // a mode this binary does not have must still be refused rather than guessed at.
    [InlineData("--ver")]
    public void AnythingElse_IsUsage(string arg) =>
        Program.Classify([arg]).Should().Be(Program.Startup.Usage);

    /// <summary>
    /// What `--version` prints is what the extension parses to decide whether to offer an update,
    /// so its shape is a contract rather than a convenience.
    /// </summary>
    [Fact]
    public void Version_IsAComparableNumber() =>
        Program.VersionText.Should().MatchRegex(@"^\d+\.\d+\.\d+");

    [Theory]
    [InlineData("0.12.3+8f3a1c9", "0.12.3")]
    [InlineData("0.12.3", "0.12.3")]
    [InlineData("1.2.3-rc.1+deadbeef", "1.2.3-rc.1")]
    [InlineData("", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void BuildMetadata_IsNotPartOfTheVersion(string? informational, string expected) =>
        Program.VersionFrom(informational).Should().Be(expected);

    /// <summary>
    /// An unstamped build must read as OLDER than every release: the panel compares this against
    /// the newest published tag, and the SDK's default 1.0.0 would have hidden the button for ever.
    /// </summary>
    [Fact]
    public void AnUnstampedBuild_IsNotNewerThanEveryRelease() =>
        Program.VersionText.Should().Be(
            "0.0.0",
            "nothing stamped this build, and the csproj pins that case to 0.0.0 — the SDK's default "
                + "1.0.0 would have read as newer than every published release and hidden the update button");

    [Fact]
    public void HelpText_NamesTheClientConfigKey() =>
        Program.HelpText.Should().Contain("\"coai\"");
}

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
    [InlineData("--stdio")]
    [InlineData("serve")]
    [InlineData("-v")]
    public void AnythingElse_IsUsage(string arg) =>
        Program.Classify([arg]).Should().Be(Program.Startup.Usage);

    [Fact]
    public void HelpText_NamesTheClientConfigKey() =>
        Program.HelpText.Should().Contain("\"coai\"");
}

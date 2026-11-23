using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the installed claude must say before it is given the coder's write launch (epoch 3 E2 s2 t3, d3): a release at or above 2.1.330, the
/// first the boundary was measured on, and a <c>--help</c> that declares <c>--restricted</c>, <c>--permission-prompts</c> and
/// <c>acceptEdits</c> as a permission mode — each one read as an OPTION, never as a word in prose.
/// </summary>
/// <remarks>
/// The fixture is the REAL help text of claude 2.1.330 on the measuring machine (<c>fixtures/claude/help-2.1.330-wsl.txt</c>, captured 2026-11-23);
/// the broken variants are that text with one thing taken out, so each refusal is tested against a help that is otherwise acceptable (a fixture the
/// code rejects for another reason proves nothing).
/// </remarks>
public sealed class ClaudeWriteCapabilityTests
{
    private const string Version = "2.1.330 (Claude Code)\n";

    private static string Help(string name = "help-2.1.330-wsl.txt") =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "claude", name));

    /// <summary>The help without the option block that starts at <paramref name="flag"/>: its line and the wrapped lines under it.</summary>
    private static string WithoutOption(string help, string flag)
    {
        var kept = new List<string>();
        var inside = false;
        foreach (var line in help.Split('\n'))
        {
            inside = line.StartsWith($"  {flag}", StringComparison.Ordinal) || (inside && line.StartsWith("    ", StringComparison.Ordinal));
            if (!inside)
            {
                kept.Add(line);
            }
        }

        return string.Join('\n', kept);
    }

    private static ClaudeWriteCapability.Refused Refusal(string version, string help) =>
        ClaudeWriteCapability.Judge(version, help).Should().BeOfType<ClaudeWriteCapability.Refused>().Subject;

    [Fact]
    public void TheMeasuredClaude_IsReady_AndItsVersionIsRead()
    {
        ClaudeWriteCapability.Judge(Version, Help()).Should().Be(new ClaudeWriteCapability.Ready("2.1.330"));
        ClaudeWriteCapability.VersionFloor.Should().Be(new System.Version(2, 1, 330));
    }

    [Theory]
    [InlineData("2.1.331 (Claude Code)")]
    [InlineData("2.2.0 (Claude Code)")]
    [InlineData("2.1.1000")]
    [InlineData("3.0.0 (Claude Code)")]
    public void ANewerRelease_IsReady(string printed)
    {
        ClaudeWriteCapability.Judge(printed + "\n", Help()).Should().BeOfType<ClaudeWriteCapability.Ready>();
    }

    [Theory]
    [InlineData("2.1.329 (Claude Code)")]
    [InlineData("2.0.999")]
    [InlineData("1.9.9999 (Claude Code)")]
    public void ABelowTheFloor_IsRefused_WithTheCure_EvenWhenItsHelpLooksRight(string printed)
    {
        var refused = Refusal(printed + "\n", Help());

        refused.Said.Should().Contain("2.1.330").And.Contain(printed.Split(' ')[0]);
        refused.Cure.Should().Contain("update claude");
        refused.CouldNotStart.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("claude")]
    [InlineData("Claude Code, a banner\n2.1.330 (Claude Code)")]
    public void AVersionLineThatNamesNoRelease_IsRefused_NeverGuessed(string printed)
    {
        Refusal(printed, Help()).Said.Should().Contain("--version");
    }

    [Fact]
    public void AHelpWithoutRestricted_IsRefused_ByName()
    {
        var refused = Refusal(Version, WithoutOption(Help(), "--restricted"));

        refused.Said.Should().Contain("--restricted").And.NotContain("--permission-prompts");
        refused.Cure.Should().Contain("update claude");
    }

    [Fact]
    public void AHelpWithoutPermissionPrompts_IsRefused_ByName()
    {
        var refused = Refusal(Version, WithoutOption(Help(), "--permission-prompts"));

        refused.Said.Should().Contain("--permission-prompts").And.NotContain("--restricted");
    }

    [Fact]
    public void APermissionModeWithoutAcceptEdits_IsRefused_EvenWhenTheWordIsElsewhereInTheHelp()
    {
        // The word is in prose elsewhere, as a planted decoy: only the choices of --permission-mode count.
        var decoy = Help().Replace("\"acceptEdits\", \"auto\"", "\"auto\"", StringComparison.Ordinal)
            .Replace("Restricted mode:", "Restricted mode (acceptEdits is not a choice here):", StringComparison.Ordinal);
        decoy.Should().NotBe(Help(), "the fixture must have been changed, or the test is about nothing");

        Refusal(Version, decoy).Said.Should().Contain("acceptEdits");
    }

    [Fact]
    public void EverythingMissing_IsNamedAtOnce()
    {
        // The 2.1.197 help has no --restricted and no --permission-prompts, but its permission modes DO list acceptEdits: take that out too, so all three are missing.
        var bare = Help("help-2.1.197-wsl.txt").Replace("\"acceptEdits\", ", string.Empty, StringComparison.Ordinal);
        bare.Should().NotBe(Help("help-2.1.197-wsl.txt"), "the fixture must have been changed, or the test is about nothing");

        var refused = Refusal(Version, bare);

        refused.Said.Should().Contain("--restricted").And.Contain("--permission-prompts").And.Contain("acceptEdits");
    }

    [Fact]
    public void AColouredHelp_IsReadThroughItsColour()
    {
        var coloured = System.Text.RegularExpressions.Regex.Replace(Help(), "(?m)^(  (?:-\\w, )?)(--[a-z-]+)", "$1\u001b[36m$2\u001b[39m");
        coloured.Should().NotBe(Help());

        ClaudeWriteCapability.Judge(Version, coloured).Should().BeOfType<ClaudeWriteCapability.Ready>();
    }

    [Fact]
    public async Task TheProbe_AsksTheVersionAndTheHelp_ThroughTheOneLauncher_AndNothingElse()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(0, Version, string.Empty, false),
            ["--help"] => new ProcessResult(0, Help(), string.Empty, false),
            _ => throw new InvalidOperationException("a capability check started a turn"),
        });

        var answer = await ClaudeWriteCapability.ProbeAsync(launcher, "claude", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        answer.Should().Be(new ClaudeWriteCapability.Ready("2.1.330"));
        launcher.Requests.Select(r => r.Arguments[0]).Should().Equal("--version", "--help");
        launcher.Requests.Should().OnlyContain(r => r.Timeout <= TimeSpan.FromSeconds(10), "a hung CLI is stopped by the launcher, not waited for");
    }

    [Fact]
    public async Task AVersionThatTimedOut_IsRefused_NotReadAsAnOldClaude()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(-1, string.Empty, string.Empty, true),
            _ => new ProcessResult(0, Help(), string.Empty, false),
        });

        var answer = await ClaudeWriteCapability.ProbeAsync(launcher, "claude", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        var refused = answer.Should().BeOfType<ClaudeWriteCapability.Refused>().Subject;
        refused.Said.Should().Contain("--version").And.Contain("did not answer");
        refused.CouldNotStart.Should().BeFalse();
    }
}

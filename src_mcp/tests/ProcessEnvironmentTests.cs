using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The allowlist a confined launch starts from — BOTH platforms, from either machine.
/// </summary>
/// <remarks>
/// <see cref="ProcessLauncherTests"/> proves the allowlist against a real child, which is what
/// settles that it is applied at all. It can only ever prove the HOST's half: run on Windows it
/// never asserts <c>HOME</c>, and run in a Linux CI it never asserts the Windows entries. These
/// assert both from anywhere, which is why <c>ForPlatform</c> takes the platform as a question.
/// <para>The entry this exists for is <c>HOME</c>. Its absence was the plan round's Blocking finding
/// — a Node runtime with no <c>HOME</c> fails in initialisation, before it reads a prompt, so losing
/// it would break every reviewer on the Team server and nothing on a Windows developer's machine
/// would have gone red.</para>
/// </remarks>
public sealed class ProcessEnvironmentTests
{
    [Fact]
    public void AUnixLaunchKeepsTheVariablesARuntimeCannotStartWithout()
    {
        var unix = ProcessEnvironment.ForPlatform(isWindows: false);

        unix.Should().Contain("HOME",
            "a Node runtime with no HOME fails in initialisation, before it reads a prompt");
        unix.Should().Contain(["USER", "LOGNAME", "SHELL"]);
        unix.Should().Contain("PATH", "the CLI is found through it and starts its own children through it");
    }

    [Fact]
    public void AWindowsLaunchKeepsWhatWindowsCannotStartWithout()
    {
        var windows = ProcessEnvironment.ForPlatform(isWindows: true);

        windows.Should().Contain(["SystemRoot", "SystemDrive", "ComSpec", "PATHEXT"]);
        windows.Should().Contain("USERPROFILE", "which is where Windows keeps what HOME keeps elsewhere");
    }

    [Fact]
    public void NeitherPlatformCarriesTheOthersHomeVariables()
    {
        // Not tidiness: a name on the wrong list is a name nobody will ever see used, and the next
        // reader has to decide whether it is load-bearing. Each list should be readable as "what
        // THIS platform needs" with nothing to discount.
        ProcessEnvironment.ForPlatform(isWindows: false).Should().NotContain("USERPROFILE");
        ProcessEnvironment.ForPlatform(isWindows: true).Should().NotContain("LOGNAME");
    }

    [Fact]
    public void WindowsComparesNamesWithoutCaseAndUnixWithIt()
    {
        // The operating system's rule, not a preference: Windows resolves Path and PATH to one
        // variable and Linux keeps them apart. Compared the other way, a Windows child would lose
        // `Path` — which is how it is actually spelled in a Windows process environment.
        ProcessEnvironment.ForPlatform(isWindows: true).Should().Contain("Path");
        ProcessEnvironment.ForPlatform(isWindows: false).Should().NotContain("path");
    }

    [Fact]
    public void TheHostsOwnListIsOneOfTheTwo()
    {
        // The companion that keeps the four above from being a test of a function nothing calls:
        // Passthrough is what ProcessLauncher actually confines with.
        ProcessEnvironment.Passthrough.Should().BeEquivalentTo(
            ProcessEnvironment.ForPlatform(OperatingSystem.IsWindows()));
    }
}

using Xunit;
using CoaiMcp.Runners.Processes;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The npm-shim gap, found by the second real run: `codex` on Windows is `codex.cmd`, and
/// Process.Start does not consult PATHEXT.
/// </summary>
public sealed class ExecutableResolverTests
{
    private const string Path1 = @"C:\Users\ada\AppData\Roaming\npm";

    /// <summary>npm's three shims: the extensionless shell script is the one that cannot be started.</summary>
    private static readonly HashSet<string> NpmDir =
    [
        @$"{Path1}\codex",
        @$"{Path1}\codex.cmd",
        @$"{Path1}\codex.ps1",
    ];

    private static string Resolve(string command, string? path = Path1, HashSet<string>? files = null) =>
        ExecutableResolver.Resolve(command, isWindows: true, path, (files ?? NpmDir).Contains);

    [Fact]
    public void BareName_ResolvesToTheCmdShim_NotTheExtensionlessScript() =>
        Resolve("codex").Should().Be(@$"{Path1}\codex.cmd",
            "the extensionless file is a shell script Process.Start cannot execute");

    [Fact]
    public void ExeWins_OverTheCmdShim()
    {
        var files = new HashSet<string> { @$"{Path1}\tool.cmd", @$"{Path1}\tool.exe" };

        Resolve("tool", files: files).Should().Be(@$"{Path1}\tool.exe", "a real executable beats a shim");
    }

    [Fact]
    public void FirstPathEntryWins_AsAShellWouldChoose()
    {
        var files = new HashSet<string> { @"C:\first\tool.cmd", @"C:\second\tool.cmd" };

        Resolve("tool", @"C:\first;C:\second", files).Should().Be(@"C:\first\tool.cmd");
    }

    [Fact]
    public void AnExplicitPath_IsTheOperatorsDecision_AndIsNeverRewritten()
    {
        Resolve(@"D:\tools\codex.exe").Should().Be(@"D:\tools\codex.exe");
        Resolve("./codex").Should().Be("./codex");
    }

    [Fact]
    public void AnAlreadyExtensionedName_IsNotDoubleSuffixed() =>
        Resolve("codex.cmd").Should().Be(@$"{Path1}\codex.cmd");

    [Fact]
    public void NothingFound_ReturnsTheNameUnchanged_SoTheOsMessageNamesWhatWasConfigured() =>
        Resolve("nosuchtool").Should().Be("nosuchtool");

    [Fact]
    public void OnPosix_TheNameIsReturnedUnchanged_ThereIsNoSuchGap() =>
        ExecutableResolver.Resolve("codex", isWindows: false, "/usr/bin", _ => true).Should().Be("codex");

    [Fact]
    public void EmptyPath_IsNotACrash() =>
        Resolve("codex", path: null).Should().Be("codex");

    [Fact]
    public void TheRealCliOnThisMachine_ResolvesToSomethingStartable()
    {
        // Not a mock: this is the case that cost a real run. On a machine without codex the
        // resolution falls through to the bare name, which is still correct behaviour.
        var resolved = ExecutableResolver.Resolve("codex");

        if (OperatingSystem.IsWindows() && resolved != "codex")
        {
            System.IO.Path.GetExtension(resolved).Should().NotBeEmpty("Process.Start needs the extension");
            File.Exists(resolved).Should().BeTrue();
        }
    }
}

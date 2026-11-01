using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// DL5's Kimi half (E6 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): on a Linux or WSL host the person's places are found where Moonshot's installer put them — <c>~/.kimi-code/bin/kimi</c>, or
/// <c>$KIMI_INSTALL_DIR/bin/kimi</c> — not only on the editor's <c>PATH</c>, and the folder where <c>rg</c> and <c>fd</c> were found (including <c>~/.local/bin</c>) joins the built <c>PATH</c>.
/// Spelled with <c>/</c> on every machine that runs it: a Linux path is judged the same on a Windows test host.
/// </summary>
public sealed class KimiPlacesOnLinuxTests
{
    private const string Home = "/home/p";

    private static Func<string, string?> Env(Dictionary<string, string> values) => name => values.GetValueOrDefault(name);

    private static Func<string, bool> Files(params string[] present) => new HashSet<string>(present, StringComparer.Ordinal).Contains;

    [Theory]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    public void TheInstallersKimi_IsFoundOnAPathWithoutItsFolder(HostKind host)
    {
        var files = Files("/home/p/.kimi-code/bin/kimi", "/home/p/.kimi-code/bin/rg", "/home/p/.kimi-code/bin/fd");

        var places = KimiPlaces.Resolve("/owned/kh-x", host, Env(new() { ["PATH"] = "/usr/bin:/bin" }), Home, files);

        places.PersonsHome.Should().Be("/home/p/.kimi-code");
        places.PersonsBin.Should().Be("/home/p/.kimi-code/bin");
        places.Executable.Should().Be("/home/p/.kimi-code/bin/kimi", "the installer's own folder is searched though the editor's PATH lacks it");
        places.ToolsRefusal.Should().BeEmpty();
    }

    [Fact]
    public void KimiInstallDir_IsHonoured_ForTheExecutableAndTheBin()
    {
        var files = Files("/opt/ki/bin/kimi", "/opt/ki/bin/rg", "/opt/ki/bin/fd");

        var places = KimiPlaces.Resolve("/owned/kh-x", HostKind.Wsl, Env(new() { ["KIMI_INSTALL_DIR"] = "/opt/ki", ["PATH"] = "/usr/bin" }), Home, files);

        places.PersonsBin.Should().Be("/opt/ki/bin");
        places.Executable.Should().Be("/opt/ki/bin/kimi");
        places.ToolFolders.Should().BeEmpty("both tools are in the person's bin");
    }

    [Fact]
    public void NoKimiAnywhere_IsTheBareName_SoTheLaunchFailsWithTheInstallCure()
    {
        KimiPlaces.Resolve("/owned/kh-x", HostKind.Wsl, Env(new() { ["PATH"] = "/usr/bin" }), Home, Files()).Executable.Should().Be("kimi");
    }

    [Fact]
    public void AKimiOnlyOnPath_StaysTheBareName_BecauseThePosixPathWalkIsTheOperatingSystems()
    {
        var places = KimiPlaces.Resolve("/owned/kh-x", HostKind.Linux, Env(new() { ["PATH"] = "/opt/k:/usr/bin" }), Home, Files("/opt/k/kimi"));

        places.Executable.Should().Be("kimi");
    }

    [Fact]
    public void MacOs_StillGuessesNothing()
    {
        var places = KimiPlaces.Resolve("/owned/kh-x", HostKind.MacOs, Env(new() { ["PATH"] = "/usr/bin" }), Home, Files("/home/p/.kimi-code/bin/kimi"));

        places.Executable.Should().Be("kimi", "nothing was measured on macOS, and a path nobody observed is not one to start a process from");
    }

    /// <summary>DL5's load-bearing case: the VS Code server's PATH lacks <c>~/.local/bin</c>, so the folder of an <c>fd</c> link must come from the search, never from the PATH.</summary>
    [Fact]
    public void ToolsOnlyInLocalBin_JoinTheBuiltPath_BeforeTheParentsEntries()
    {
        var parent = string.Join(Path.PathSeparator, "/usr/bin", "/bin");
        var files = Files("/home/p/.kimi-code/bin/kimi", "/home/p/.local/bin/rg", "/home/p/.local/bin/fd");

        var places = KimiPlaces.Resolve("/owned/kh-x", HostKind.Wsl, Env(new() { ["PATH"] = parent }), Home, files);

        places.ToolFolders.Should().Equal(["/home/p/.local/bin"]);
        places.ToolsRefusal.Should().BeEmpty("a ~/.local/bin/fd is found by the search, not by PATH");
        KimiEnvironment.BuiltPath([places.PersonsBin, .. places.ToolFolders], parent).Split(Path.PathSeparator)
            .Should().Equal("/home/p/.kimi-code/bin", "/home/p/.local/bin", "/usr/bin", "/bin");
    }

    [Fact]
    public void ToolsFoundOnPath_NameTheirFolder_InRgThenFdOrder()
    {
        var files = Files("/home/p/.kimi-code/bin/kimi", "/opt/a/rg", "/opt/b/fd");

        var places = KimiPlaces.Resolve("/owned/kh-x", HostKind.Linux, Env(new() { ["PATH"] = "/opt/b:/opt/a" }), Home, files);

        places.ToolFolders.Should().Equal("/opt/a", "/opt/b");
    }

    [Fact]
    public void TheRefusal_OnAHostWithAptGet_NamesTheAptLine()
    {
        var places = KimiPlaces.Resolve("/owned/kh-x", HostKind.Wsl, Env(new() { ["PATH"] = "/usr/bin" }), Home, Files("/usr/bin/apt-get"));

        places.ToolsRefusal.Should().Contain("sudo apt-get install -y ripgrep").And.NotContain("winget");
    }
}

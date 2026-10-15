using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The person's paths, resolved ONCE in the unredirected parent and carried down (E4.2 decision 4 and brief section 2,
/// S2.13–S2.14): honouring Kimi's own <c>KIMI_CODE_HOME</c> and <c>KIMI_INSTALL_DIR</c>, and — for the shim, whose
/// <c>USERPROFILE</c> IS the owned <c>os/</c> folder — reading no environment at all.
/// </summary>
[Collection("fakecli-env")]
public sealed class KimiPlacesTests
{
    private static Func<string, string?> Env(Dictionary<string, string> values) => name => values.GetValueOrDefault(name);

    private static Func<string, bool> Files(params string[] present) => new HashSet<string>(present, StringComparer.OrdinalIgnoreCase).Contains;

    [Fact]
    public void ThePlaces_AreResolvedFromTheRealProfile_AndHonourKimisOwnVariables()
    {
        var env = Env(new() { ["KIMI_CODE_HOME"] = @"D:\kh", ["KIMI_INSTALL_DIR"] = @"D:\ki", ["PATH"] = @"D:\ki\bin;C:\Windows", ["PATHEXT"] = ".EXE" });
        var files = Files(@"D:\ki\bin\kimi.exe", @"D:\ki\bin\rg.exe", @"D:\ki\bin\fd.exe");

        var places = KimiPlaces.Resolve(@"C:\owned\kh-x", HostKind.Windows, env, @"C:\Users\someone", files);

        places.PersonsHome.Should().Be(@"D:\kh");
        places.Credentials.Should().Be(Path.Combine(@"D:\kh", "credentials"));
        places.PersonsConfig.Should().Be(Path.Combine(@"D:\kh", "config.toml"));
        places.PersonsBin.Should().Be(@"D:\ki\bin");
        places.Executable.Should().Be(@"D:\ki\bin\kimi.exe");
        places.ToolFolders.Should().BeEmpty("both tools are in the person's bin");
        places.ToolsRefusal.Should().BeEmpty();
        places.Home.Should().Be(Path.Combine(@"C:\owned\kh-x", "home"));
        places.Os.Should().Be(Path.Combine(@"C:\owned\kh-x", "os"));
        places.Link.Should().Be(Path.Combine(places.Home, "credentials"));
        places.Config.Should().Be(Path.Combine(places.Home, "config.toml"));
    }

    [Fact]
    public void WithoutTheVariables_ThePlacesAreTheProfilesKimiCodeFolder()
    {
        var files = Files(@"C:\Users\someone\.kimi-code\bin\kimi.exe", @"C:\Users\someone\.kimi-code\bin\rg.exe", @"C:\Users\someone\.kimi-code\bin\fd.exe");

        var places = KimiPlaces.Resolve(@"C:\owned\kh-x", HostKind.Windows, Env([]), @"C:\Users\someone", files);

        places.PersonsHome.Should().Be(@"C:\Users\someone\.kimi-code");
        places.PersonsBin.Should().Be(@"C:\Users\someone\.kimi-code\bin");
        places.Executable.Should().Be(@"C:\Users\someone\.kimi-code\bin\kimi.exe");
    }

    [Fact]
    public void AKimiOnlyOnPath_IsTheAbsolutePathItResolvesTo()
    {
        var env = Env(new() { ["PATH"] = @"C:\tools;C:\Windows", ["PATHEXT"] = ".EXE" });
        var files = Files(@"C:\tools\kimi.exe", @"C:\tools\rg.exe", @"C:\tools\fd.exe");

        var places = KimiPlaces.Resolve(@"C:\owned\kh-x", HostKind.Windows, env, @"C:\Users\someone", files);

        places.Executable.Should().Be(@"C:\tools\kimi.exe");
        places.ToolFolders.Should().Equal(@"C:\tools");
    }

    [Fact]
    public void NoKimiAnywhere_IsTheBareName_SoTheLaunchFailsWithTheInstallCure()
    {
        var places = KimiPlaces.Resolve(@"C:\owned\kh-x", HostKind.Windows, Env([]), @"C:\Users\someone", Files());

        places.Executable.Should().Be("kimi");
        places.ToolsRefusal.Should().Contain("rg and fd were not found");
    }

    [Fact]
    public void ForShim_ReadsNoEnvironment()
    {
        using var os = TempDir.For("testsToDelete-kimi-shimenv-");
        var root = Path.Combine(os, "owned", "kh-x");
        var bin = Path.Combine(os, "bin");
        var marker = Path.Combine(os, "THE-REDIRECTED-PROFILE");
        var before = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("USERPROFILE", marker);
        KimiPlaces places;
        try
        {
            places = KimiPlaces.ForShim(root, bin);
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", before);
        }

        places.Home.Should().Be(Path.Combine(root, "home"));
        places.Os.Should().Be(Path.Combine(root, "os"));
        places.PersonsBin.Should().Be(bin);
        places.Executable.Should().BeEmpty("the shim is handed --executable, and never derives one");
        places.ToolsRefusal.Should().BeEmpty();
        places.ToolFolders.Should().BeEmpty();
        places.PersonsHome.Should().BeEmpty("the shim reads the sign-in folder from the owned junction's own reparse data");
        places.ToString().Should().NotContain("THE-REDIRECTED-PROFILE", "nothing is derived from the profile");
    }
}

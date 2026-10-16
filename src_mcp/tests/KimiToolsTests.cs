using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>rg</c> and <c>fd</c> are FOUND before the launch or it is refused — never admitted into the owned home
/// (E4.2 decision 2, S2.12): pure, over an injected file test.
/// </summary>
public sealed class KimiToolsTests
{
    private const string Bin = @"C:\k\bin";

    private static Func<string, bool> Files(params string[] present)
    {
        var set = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);

        return set.Contains;
    }

    [Fact]
    public void BothInBin_NeedsNoExtraFolder()
    {
        var files = Files(@"C:\k\bin\rg.exe", @"C:\k\bin\fd.exe");

        KimiTools.FoldersFor(true, Bin, @"C:\Windows", ".COM;.EXE", files).Should().BeEmpty("bin itself is never listed — it begins the PATH anyway");
        KimiTools.Refusal(true, Bin, @"C:\Windows", ".COM;.EXE", files).Should().BeEmpty();
    }

    [Fact]
    public void RgInBin_FdElsewhere_NamesTheFdFolder()
    {
        var files = Files(@"C:\k\bin\rg.exe", @"C:\tools\fd.exe");

        KimiTools.FoldersFor(true, Bin, @"C:\Windows;C:\tools", ".EXE", files).Should().Equal(@"C:\tools");
        KimiTools.Refusal(true, Bin, @"C:\Windows;C:\tools", ".EXE", files).Should().BeEmpty();
    }

    [Fact]
    public void RgAndFdOnPathInTwoFolders_NamesBoth_RgFirst()
    {
        var files = Files(@"C:\rg\rg.exe", @"C:\fd\fd.exe");

        KimiTools.FoldersFor(true, Bin, @"C:\fd;C:\rg", ".EXE", files).Should().Equal(@"C:\rg", @"C:\fd");
    }

    [Fact]
    public void BothInOnePathFolder_NamesItOnce()
    {
        var files = Files(@"C:\tools\rg.exe", @"C:\tools\fd.exe");

        KimiTools.FoldersFor(true, Bin, @"C:\tools\;C:\Windows", ".EXE", files).Should().Equal(@"C:\tools");
    }

    [Fact]
    public void Neither_IsRefused_NamingBothCures()
    {
        var refusal = KimiTools.Refusal(true, Bin, @"C:\Windows", ".EXE", Files());

        refusal.Should().Contain("rg and fd were not found in " + Bin + " or on PATH")
            .And.Contain("winget install --id BurntSushi.ripgrep.MSVC -e")
            .And.Contain("winget install --id sharkdp.fd -e")
            .And.Contain("restart the editor")
            .And.Contain("run kimi once in a terminal")
            .And.Contain("D19");
    }

    [Fact]
    public void RgOnly_IsRefused_NamingFd()
    {
        var refusal = KimiTools.Refusal(true, Bin, @"C:\Windows", ".EXE", Files(@"C:\k\bin\rg.exe"));

        refusal.Should().Contain("fd was not found in").And.NotContain("rg and fd");
    }

    [Fact]
    public void FdOnly_IsRefused_NamingRg()
    {
        var refusal = KimiTools.Refusal(true, Bin, @"C:\Windows;C:\tools", ".EXE", Files(@"C:\tools\fd.exe"));

        refusal.Should().Contain("rg was not found in").And.NotContain("rg and fd");
    }

    [Fact]
    public void AnExtensionIsFoundThroughPathext_AndOnlyThroughIt()
    {
        var files = Files(@"C:\t\rg.exe", @"C:\t\fd.exe");

        KimiTools.Refusal(true, Bin, @"C:\t", ".EXE", files).Should().BeEmpty("fd.exe is fd plus a PATHEXT entry");
        KimiTools.Refusal(true, Bin, @"C:\t", ".BAT", files).Should().Contain("rg and fd were not found", "PATHEXT without .EXE does not find an .exe");
    }

    [Fact]
    public void WithNoPathext_TheWindowsDefaultsApply()
    {
        var files = Files(@"C:\t\rg.exe", @"C:\t\fd.exe");

        KimiTools.Refusal(true, Bin, @"C:\t", null, files).Should().BeEmpty();
    }

    [Fact]
    public void OnUnix_TheNameIsAsGiven_AndPathIsColonSeparated()
    {
        var files = Files("/opt/bin/rg", "/usr/bin/fd");

        KimiTools.FoldersFor(false, "/home/p/.kimi-code/bin", "/opt/bin:/usr/bin", null, files).Should().Equal("/opt/bin", "/usr/bin");
        KimiTools.Refusal(false, "/home/p/.kimi-code/bin", "/usr/bin", null, files).Should().Contain("rg was not found");
    }
}

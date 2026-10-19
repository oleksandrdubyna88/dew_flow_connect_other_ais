using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// DL5 and DL12 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c> (E6; S-K9, S-K17's refusal arm): <c>rg</c> and <c>fd</c> are found on a Linux or WSL host in the person's Kimi <c>bin</c>, then on <c>PATH</c>,
/// then in <c>~/.local/bin</c> (where DL12's <c>fd</c> link goes) — and the refusal names the cure of THIS host: the <c>apt-get</c> or <c>dnf</c> line when that manager resolves, else one generic sentence.
/// Never <c>winget</c>. Pure over an injected file test, with <c>/</c> paths on whatever machine runs it.
/// </summary>
public sealed class KimiToolsOnLinuxTests
{
    private const string Bin = "/home/p/.kimi-code/bin";

    private const string Local = "/home/p/.local/bin";

    private const string Generic = "install ripgrep and fd with your distribution's package manager, or place them on PATH or in ~/.local/bin";

    private static Func<string, bool> Files(params string[] present) => new HashSet<string>(present, StringComparer.Ordinal).Contains;

    [Fact]
    public void ToolsOnlyInLocalBin_AreFound_AndTheirFolderJoinsTheBuiltPath()
    {
        var files = Files(Local + "/rg", Local + "/fd");

        KimiTools.FoldersFor(false, Bin, "/usr/bin:/bin", null, files, Local).Should().Equal([Local], "~/.local/bin is a searched place, and the folder a tool was found in is what the built PATH must name");
        KimiTools.Refusal(false, Bin, "/usr/bin:/bin", null, files, Local).Should().BeEmpty("both tools were found in ~/.local/bin");
    }

    [Fact]
    public void TheSearchOrder_IsTheKimiBin_ThenPath_ThenLocalBin()
    {
        var both = Files("/opt/bin/rg", Local + "/rg", Local + "/fd", "/opt/bin/fd");

        KimiTools.FoldersFor(false, Bin, "/opt/bin", null, both, Local).Should().Equal(["/opt/bin"], "a PATH hit comes before ~/.local/bin");
        KimiTools.FoldersFor(false, Bin, "/opt/bin", null, Files("/opt/bin/rg", Local + "/fd"), Local).Should().Equal(["/opt/bin", Local], "rg first, then fd, each where it was found");
        KimiTools.FoldersFor(false, Bin, "/opt/bin", null, Files(Bin + "/rg", Bin + "/fd", Local + "/rg"), Local).Should().BeEmpty("the person's bin begins the PATH anyway and wins");
    }

    [Fact]
    public void WithNoLocalBin_ASearchIsWhatItWas()
    {
        KimiTools.FoldersFor(false, Bin, "/usr/bin", null, Files(Local + "/rg", Local + "/fd")).Should().BeEmpty("without ~/.local/bin as a place nothing is found there");
    }

    [Fact]
    public void RgAbsentEverywhere_WithAptGet_NamesTheAptLine_AndNotWinget()
    {
        var refusal = KimiTools.Refusal(false, Bin, "/usr/bin:/bin", null, Files(Bin + "/fd", "/usr/bin/apt-get"), Local);

        refusal.Should().Contain($"rg was not found in {Bin}, on PATH or in ~/.local/bin")
            .And.Contain("sudo apt-get install -y ripgrep", "the Linux refusal should name the apt-get line")
            .And.Contain("D19")
            .And.NotContain("winget", "the Linux refusal quotes winget")
            .And.NotContain("dnf")
            .And.NotContain("fd-find", "fd was found, so only rg's cure is named");
    }

    [Fact]
    public void RgAbsentEverywhere_WithDnf_NamesTheDnfLine()
    {
        var refusal = KimiTools.Refusal(false, Bin, "/usr/bin", null, Files(Bin + "/fd", "/usr/bin/dnf"), Local);

        refusal.Should().Contain("sudo dnf install -y ripgrep").And.NotContain("apt-get").And.NotContain("winget");
    }

    [Fact]
    public void WhenBothManagersResolve_TheAptLineIsNamed()
    {
        var refusal = KimiTools.Refusal(false, Bin, "/usr/bin", null, Files("/usr/bin/apt-get", "/usr/bin/dnf"), Local);

        refusal.Should().Contain("sudo apt-get install -y ripgrep").And.Contain("sudo apt-get install -y fd-find").And.NotContain("dnf");
    }

    [Fact]
    public void FdAbsent_UnderApt_NamesTheFdfindLink_BecauseDebianNamesTheBinaryFdfind()
    {
        var refusal = KimiTools.Refusal(false, Bin, "/usr/bin", null, Files(Bin + "/rg", "/usr/bin/apt-get"), Local);

        refusal.Should().Contain("fd was not found").And.Contain("sudo apt-get install -y fd-find")
            .And.Contain("fdfind").And.Contain("ln -sfn \"$(command -v fdfind)\" ~/.local/bin/fd").And.Contain("~/.local/bin/fd");
        refusal.Should().Contain("unless ~/.local/bin/fd is already your own file", "the cure must not tell a person to overwrite a file of their own")
            .And.NotContain("ln -sf \"", "the unguarded ln -sf follows a planted directory link and writes inside it; the install line uses ln -sfn");
    }

    [Fact]
    public void FdAbsent_UnderDnf_NeedsNoLink()
    {
        var refusal = KimiTools.Refusal(false, Bin, "/usr/bin", null, Files(Bin + "/rg", "/usr/bin/dnf"), Local);

        refusal.Should().Contain("sudo dnf install -y fd-find").And.NotContain("fdfind", "Fedora's fd-find installs /usr/bin/fd itself");
    }

    [Fact]
    public void WithNeitherManager_TheRefusalIsTheGenericSentence_NamingNoPackageManager()
    {
        var refusal = KimiTools.Refusal(false, Bin, "/usr/bin", null, Files(), Local);

        refusal.Should().Contain("rg and fd were not found").And.Contain(Generic)
            .And.NotContain("winget", "the refusal named a package manager this host does not have")
            .And.NotContain("apt-get", "the refusal named a package manager this host does not have")
            .And.NotContain("dnf", "the refusal named a package manager this host does not have");
    }

    [Fact]
    public void AManagerOutsidePath_DoesNotResolve_SoTheSentenceIsTheGenericOne()
    {
        var refusal = KimiTools.Refusal(false, Bin, "/opt/bin", null, Files("/usr/bin/apt-get"), Local);

        refusal.Should().Contain(Generic).And.NotContain("apt-get");
    }
}

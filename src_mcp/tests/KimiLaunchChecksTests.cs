using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pure checks of the ONE Kimi launch control (E4.2 brief sections 1 and 10, S2.11 and S2.22–S2.24): the model, the built
/// environment and what admits it, the host. The launch itself, over a fake <c>kimi</c>, is <c>KimiLaunchControlTests</c>.
/// </summary>
public sealed class KimiLaunchChecksTests
{
    private static readonly string Tmp = Path.Combine(Path.GetTempPath(), "testsToDelete-kimi-checks");

    private static readonly string Root = Path.Combine(Tmp, "owned", "kh-x");

    private static readonly string Bin = Path.Combine(Tmp, "ki", "bin");

    private static readonly string Tools = Path.Combine(Tmp, "tools");

    private static readonly string Windows = Path.Combine(Tmp, "windows");

    private static readonly KimiPlaces Places = new(Root, Path.Combine(Tmp, "ki"), Bin, [Tools], string.Empty, Path.Combine(Bin, "kimi.exe"));

    private static readonly string Parent = string.Join(Path.PathSeparator, Tools, Windows, Bin);

    private static ProcessRequest Raw(params string[] arguments) =>
        new(Places.Executable, arguments, Places.Home) { Environment = new Dictionary<string, string?> { ["VIBE_MCP_SERVERS"] = "x" } };

    private static ProcessRequest ShimRaw() =>
        Raw("--ask-acp", "--profile", "kimi", "--model", KimiModels.Default, "--home", Root, "--persons-bin", Bin);

    private static ProcessRequest Built() => KimiLaunch.Built(ShimRaw(), Places, Parent);

    private static ProcessRequest With(ProcessRequest request, string name, string? value) =>
        request with { Environment = new Dictionary<string, string?>(request.Environment) { [name] = value } };

    // ---------- S2.11 ----------

    [Theory]
    [InlineData("moonshot-ai/kimi-k2.6")]
    [InlineData("nope")]
    [InlineData("")]
    public void AnIdOutsideKimiCode_IsRefusedWithTheBillingSentence(string model)
    {
        KimiLaunch.ModelRefusal(model).Should()
            .Contain($"the model '{model}' is not on the Kimi Code plan")
            .And.Contain("only kimi-code/* ids are")
            .And.Contain("kimi-code/k3-256k")
            .And.Contain("a moonshot-ai/* id bills your own API key, so coai sends nothing (D10)");
    }

    [Fact]
    public void AnUncuratedKimiCodeId_IsRefusedWithTheList()
    {
        KimiLaunch.ModelRefusal("kimi-code/k9").Should()
            .Contain("the model 'kimi-code/k9' is not one coai has seen Kimi answer")
            .And.Contain("choose one of kimi-code/k3-256k, kimi-code/k3, kimi-code/kimi-for-coding, kimi-code/kimi-for-coding-highspeed")
            .And.EndWith("(D10)");
    }

    [Fact]
    public void EveryCuratedId_IsAdmitted()
    {
        KimiModels.Curated.Should().NotBeEmpty();
        KimiModels.Curated.Select(KimiLaunch.ModelRefusal).Should().OnlyContain(refusal => refusal.Length == 0);
    }

    // ---------- S2.22 ----------

    [Fact]
    public void TheBuiltEnvironment_IsExactlyTheSevenNames_AndPathBeginsWithBin()
    {
        var built = Built();

        built.InheritsEnvironment.Should().BeFalse();
        built.Passthrough.Should().BeEquivalentTo(ProcessEnvironment.Minimal);
        built.Environment.Keys.Should().BeEquivalentTo(
            ["KIMI_CODE_HOME", "KIMI_CODE_NO_AUTO_UPDATE", "USERPROFILE", "HOME", "HOMEDRIVE", "HOMEPATH", "PATH"],
            "the seven names and no VIBE_MCP_SERVERS the request came with");
        built.Environment["KIMI_CODE_HOME"].Should().Be(Places.Home);
        built.Environment["KIMI_CODE_NO_AUTO_UPDATE"].Should().Be("1");
        built.Environment["USERPROFILE"].Should().Be(Places.Os);
        built.Environment["HOME"].Should().Be(Places.Os);
        (built.Environment["HOMEDRIVE"] + built.Environment["HOMEPATH"]).Should().Be(Places.Os);
        built.Environment["PATH"].Should().Be(string.Join(Path.PathSeparator, Bin, Tools, Windows));
        KimiLaunch.Built(built, Places, built.Environment["PATH"]!).Should().BeEquivalentTo(built, "building a built request changes nothing — idempotent");
    }

    [Fact]
    public void OnWindows_TheOsHomeIsSplitIntoDriveAndPath()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a drive letter is a Windows path shape");

        var built = Built();

        built.Environment["HOMEDRIVE"].Should().MatchRegex("^[A-Za-z]:$", "the drive without its trailing separator");
        built.Environment["HOMEPATH"].Should().StartWith(@"\").And.EndWith(@"\os");
    }

    [Fact]
    public void BuiltPath_PrefixesFirst_ThenTheParentsEntriesNotAlreadyListed()
    {
        var separator = Path.PathSeparator;
        var trailing = Bin + Path.DirectorySeparatorChar;

        KimiLaunch.BuiltPath([Bin, Tools], string.Join(separator, trailing, Windows, string.Empty, Tools))
            .Should().Be(string.Join(separator, Bin, Tools, Windows), "a trailing separator is the same folder, an empty entry is dropped");
        KimiLaunch.BuiltPath([Bin], null).Should().Be(Bin);
    }

    // ---------- S2.23 ----------

    public static TheoryData<string> Rows => new()
    {
        "inherited", "the default passthrough", "an eighth name", "a real USERPROFILE", "a PATH that starts elsewhere", "a pin of 0",
        "another KIMI_CODE_HOME", "another --home", "another --persons-bin", "a changed HOME", "a split HOMEPATH", "a renamed name",
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void EnvironmentRefusal_AdmitsOnlyWhatBuiltMakes(string row)
    {
        var built = Built();
        var (request, mentions) = row switch
        {
            "inherited" => (built with { InheritsEnvironment = true }, "D19"),
            "the default passthrough" => (built with { Passthrough = ProcessEnvironment.Passthrough }, "D19"),
            "an eighth name" => (With(built, "KIMI_SHELL_PATH", "x"), "D19"),
            "a renamed name" => (With(Without(built, "HOME"), "SOMETHING_ELSE", Places.Os), "D19"),
            "a real USERPROFILE" => (With(built, "USERPROFILE", Path.Combine(Tmp, "real-profile")), "real-profile"),
            "a changed HOME" => (With(built, "HOME", Path.Combine(Tmp, "real-home")), "real-home"),
            "a split HOMEPATH" => (With(built, "HOMEPATH", Path.Combine(Path.DirectorySeparatorChar.ToString(), "elsewhere")), "elsewhere"),
            "a PATH that starts elsewhere" => (With(built, "PATH", string.Join(Path.PathSeparator, Windows, Bin)), Windows),
            "a pin of 0" => (With(built, "KIMI_CODE_NO_AUTO_UPDATE", "0"), "KIMI_CODE_NO_AUTO_UPDATE"),
            "another KIMI_CODE_HOME" => (With(built, "KIMI_CODE_HOME", Path.Combine(Tmp, "another-root", "home")), "another-root"),
            "another --home" => (WithFlag(built, "--home", Path.Combine(Tmp, "other-root")), "other-root"),
            _ => (WithFlag(built, "--persons-bin", Path.Combine(Tmp, "other-bin")), "other-bin"),
        };

        var refusal = KimiLaunch.EnvironmentRefusal(request, Places);

        refusal.Should().NotBeEmpty($"{row} is not what Built makes");
        refusal.Should().Contain(mentions);
        KimiLaunch.EnvironmentRefusal(built, Places).Should().BeEmpty("what Built makes is admitted");
    }

    /// <summary>The request with the value after <paramref name="flag"/> replaced.</summary>
    private static ProcessRequest WithFlag(ProcessRequest request, string flag, string value)
    {
        var arguments = request.Arguments.ToList();
        arguments[arguments.IndexOf(flag) + 1] = value;

        return request with { Arguments = arguments };
    }

    private static ProcessRequest Without(ProcessRequest request, string name) =>
        request with { Environment = request.Environment.Where(pair => pair.Key != name).ToDictionary(pair => pair.Key, pair => pair.Value) };

    [Fact]
    public void ABuiltVersionRequest_CarriesNoFlags_AndIsAdmitted()
    {
        var version = KimiLaunch.Built(Raw("--version"), Places, Parent);

        KimiLaunch.EnvironmentRefusal(version, Places).Should().BeEmpty("--version carries neither --home nor --persons-bin");
    }

    [Fact]
    public void FlagOf_ReadsTheValueAfterTheFlag_AndModelOfIsTheModelFlag()
    {
        var request = ShimRaw();

        KimiLaunch.FlagOf(request, "--home").Should().Be(Root);
        KimiLaunch.FlagOf(request, "--persons-bin").Should().Be(Bin);
        KimiLaunch.FlagOf(request, "--nope").Should().BeEmpty();
        KimiLaunch.ModelOf(request).Should().Be(KimiModels.Default);
        KimiLaunch.FlagOf(Raw("--model"), "--model").Should().BeEmpty("a flag with no value after it has none");
    }

    // ---------- S2.24 ----------

    [Theory]
    [InlineData(HostKind.Linux, "linux")]
    [InlineData(HostKind.Wsl, "wsl")]
    [InlineData(HostKind.MacOs, "macos")]
    [InlineData(HostKind.Other, "other")]
    public void HostRefusal_IsVibesShape_ForKimi_OnAnyHostButWindows(HostKind host, string word)
    {
        KimiLaunch.HostRefusal(host, "kimi").Should()
            .Contain("Kimi is refused on a " + word + " host")
            .And.Contain("D18")
            .And.Contain("every Kimi measurement coai has is Windows");
    }

    [Theory]
    [InlineData("wsl.exe")]
    [InlineData("wsl")]
    [InlineData(@"C:\Windows\System32\WSL.EXE")]
    [InlineData(@"\\wsl$\Ubuntu\home\p\.local\bin\kimi")]
    public void OnWindows_AnExecutableThatStartsTheLinuxSide_IsRefused(string executable)
    {
        HostKinds.RunsInWsl(executable).Should().BeTrue();
        KimiLaunch.HostRefusal(HostKind.Windows, executable).Should()
            .Contain($"'{executable}' would start Kimi on the Linux side")
            .And.Contain("D18")
            .And.Contain("point this row at a Windows kimi");
    }

    [Theory]
    [InlineData("kimi")]
    [InlineData(@"C:\Users\p\.kimi-code\bin\kimi.exe")]
    public void OnWindows_AWindowsKimi_IsNotRefusedByTheHost(string executable)
    {
        HostKinds.RunsInWsl(executable).Should().BeFalse();
        KimiLaunch.HostRefusal(HostKind.Windows, executable).Should().BeEmpty();
    }
}

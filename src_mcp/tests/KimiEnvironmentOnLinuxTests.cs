using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The BUILT environment of a Kimi launch on a Linux or WSL host (S-K8 and DL11 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>, E6): <c>HOME</c> alone redirects the OS home (no <c>USERPROFILE</c>, <c>HOMEDRIVE</c>,
/// <c>HOMEPATH</c>), and <c>XDG_CACHE_HOME</c> points at a <c>cache/</c> folder beside <c>os/</c> — NOT inside <c>KIMI_CODE_HOME</c> — so Kimi's native-file extraction (L-K3: about 2 MB into
/// <c>$HOME/.cache/kimi-code/native/…</c> on every launch) leaves <c>os/</c> empty. The pure decisions run on every machine through the explicit platform flag.
/// </summary>
public sealed class KimiEnvironmentOnLinuxTests : IDisposable
{
    private static readonly string Tmp = Path.Combine(Path.GetTempPath(), "testsToDelete-kimi-env-unix");

    private static readonly KimiPlaces Places = new(Path.Combine(Tmp, "owned", "kh-x"), Path.Combine(Tmp, "ki"), Path.Combine(Tmp, "ki", "bin"), [], string.Empty, Path.Combine(Tmp, "ki", "bin", "kimi"));

    private static readonly string Parent = string.Join(Path.PathSeparator, Path.Combine(Tmp, "usr-bin"), Path.Combine(Tmp, "bin"));

    private readonly KimiFixture _person = new();

    public void Dispose() => _person.Dispose();

    private static ProcessRequest Raw(params string[] arguments) => new(Places.Executable, arguments, Places.Home);

    private static ProcessRequest Built() => KimiEnvironment.Built(Raw("--version"), Places, Parent, windows: false);

    private static ProcessRequest With(ProcessRequest request, string name, string? value) =>
        request with { Environment = new Dictionary<string, string?>(request.Environment) { [name] = value } };

    [Fact]
    public void TheUnixEnvironment_IsHomeAlone_AndTheCache()
    {
        var built = Built();

        built.InheritsEnvironment.Should().BeFalse();
        built.Passthrough.Should().BeEquivalentTo(ProcessEnvironment.Minimal);
        built.Environment.Keys.Should().BeEquivalentTo(
            ["KIMI_CODE_HOME", "KIMI_CODE_NO_AUTO_UPDATE", "HOME", "XDG_CACHE_HOME", "PATH"],
            "HOME alone redirects the OS home on Unix: no USERPROFILE, HOMEDRIVE or HOMEPATH");
        built.Environment["HOME"].Should().Be(Places.Os);
        built.Environment["XDG_CACHE_HOME"].Should().Be(Places.Cache);
        built.Environment["KIMI_CODE_HOME"].Should().Be(Places.Home);
        built.Environment["KIMI_CODE_NO_AUTO_UPDATE"].Should().Be("1");
        KimiEnvironment.Built(built, Places, built.Environment["PATH"]!, windows: false).Should().BeEquivalentTo(built, "building a built request changes nothing — idempotent");
    }

    [Fact]
    public void TheCache_IsBesideOs_AndNotInsideTheHomeKimiJudges()
    {
        Path.GetDirectoryName(Places.Cache).Should().Be(Path.GetDirectoryName(Places.Os), "beside os/, inside the round's owned tree");
        Path.GetFileName(Places.Cache).Should().Be("cache");
        Places.Cache.Should().NotStartWith(Places.Home, "KimiHome judges the shape of home/; a cache there would widen its allowlist");
        Places.Cache.Should().NotStartWith(Places.Os + Path.DirectorySeparatorChar, "os/ stays empty");
    }

    [Fact]
    public void OnWindows_NoCacheVariableIsSet_AndTheSevenNamesAreWhatTheyWere()
    {
        var built = KimiEnvironment.Built(Raw("--version"), Places, Parent, windows: true);

        built.Environment.Keys.Should().BeEquivalentTo(["KIMI_CODE_HOME", "KIMI_CODE_NO_AUTO_UPDATE", "USERPROFILE", "HOME", "HOMEDRIVE", "HOMEPATH", "PATH"]);
        built.Environment.Keys.Should().NotContain("XDG_CACHE_HOME");
    }

    [Fact]
    public void TheDefaultShape_IsTheMachinesOwn()
    {
        var built = KimiEnvironment.Built(Raw("--version"), Places, Parent);

        built.Environment.ContainsKey("XDG_CACHE_HOME").Should().Be(!OperatingSystem.IsWindows());
        built.Environment.ContainsKey("USERPROFILE").Should().Be(OperatingSystem.IsWindows());
        KimiEnvironment.Refusal(built, Places).Should().BeEmpty("what Built makes on this machine is admitted by Refusal on this machine");
    }

    public static TheoryData<string> Rows => new()
    {
        "a changed HOME", "a changed XDG_CACHE_HOME", "a missing XDG_CACHE_HOME", "an XDG_CACHE_HOME inside KIMI_CODE_HOME", "a USERPROFILE", "a HOMEDRIVE", "inherited",
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void TheUnixRefusal_ChecksHomeAndTheCache_AndAdmitsNothingElse(string row)
    {
        var built = Built();
        var (request, mentions) = row switch
        {
            "a changed HOME" => (With(built, "HOME", Path.Combine(Tmp, "real-home")), "real-home"),
            "a changed XDG_CACHE_HOME" => (With(built, "XDG_CACHE_HOME", Path.Combine(Tmp, "real-cache")), "real-cache"),
            "a missing XDG_CACHE_HOME" => (built with { Environment = built.Environment.Where(pair => pair.Key != "XDG_CACHE_HOME").ToDictionary(pair => pair.Key, pair => pair.Value) }, "D19"),
            "an XDG_CACHE_HOME inside KIMI_CODE_HOME" => (With(built, "XDG_CACHE_HOME", Path.Combine(Places.Home, "cache")), Path.Combine(Places.Home, "cache")),
            "a USERPROFILE" => (With(built, "USERPROFILE", Places.Os), "D19"),
            "a HOMEDRIVE" => (With(built, "HOMEDRIVE", string.Empty), "D19"),
            _ => (built with { InheritsEnvironment = true }, "D19"),
        };

        var refusal = KimiEnvironment.Refusal(request, Places, windows: false);

        refusal.Should().NotBeEmpty($"{row} is not what Built makes on Unix");
        refusal.Should().Contain(mentions);
        KimiEnvironment.Refusal(built, Places, windows: false).Should().BeEmpty("what Built makes is admitted");
    }

    /// <summary>
    /// DL11: a launch leaves <c>os/</c> empty. The owned root is made with its cache beside <c>os/</c> (<c>0700</c>); a Kimi that extracts its native files where <c>XDG_CACHE_HOME</c> says — which is all
    /// L-K3 measured it to do — puts them there, and the home passes its check.
    /// </summary>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void ALaunchOnLinux_LeavesOsEmpty_BecauseKimiExtractsIntoTheCache()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the cache folder and its 0700 mode are Unix's");
        using var standIn = WindowsStandIn.Begin();
        var places = _person.MadeHome();
        var built = KimiEnvironment.Built(Raw("--version"), places, Parent);

        Directory.Exists(places.Cache).Should().BeTrue("the cache is made with the home");
        File.GetUnixFileMode(places.Cache).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, "0700");
        SimulatedKimiExtraction(built);

        Directory.EnumerateFileSystemEntries(places.Os).Should().BeEmpty("os/ stays empty: Kimi's native cache went where XDG_CACHE_HOME said");
        Directory.EnumerateFiles(places.Cache, "*", SearchOption.AllDirectories).Should().ContainSingle("the extraction landed in the cache");
        KimiHome.Refusal(places, _person.ExpectedConfig()).Should().BeEmpty("the owned home passes its check with a populated cache beside it");
    }

    /// <summary>The same extraction WITHOUT the redirect lands in <c>$HOME/.cache</c> — inside <c>os/</c> — which the home check refuses. That is what the cache variable is for.</summary>
    [Fact]
    public void WithoutTheCacheVariable_TheExtractionLandsInOs_AndTheHomeIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the cache folder and its 0700 mode are Unix's");
        using var standIn = WindowsStandIn.Begin();
        var places = _person.MadeHome();
        var without = KimiEnvironment.Built(Raw("--version"), places, Parent);
        without = without with { Environment = without.Environment.Where(pair => pair.Key != "XDG_CACHE_HOME").ToDictionary(pair => pair.Key, pair => pair.Value) };

        SimulatedKimiExtraction(without);

        KimiHome.Refusal(places, _person.ExpectedConfig()).Should().Contain("os/ holds .cache", "an extraction into $HOME/.cache is what os/'s rule refuses");
    }

    /// <summary>What L-K3 measured Kimi doing at every start: extract nine native files into <c>${XDG_CACHE_HOME:-$HOME/.cache}/kimi-code/native/&lt;version&gt;/linux-x64/&lt;sha&gt;/</c>.</summary>
    private static void SimulatedKimiExtraction(ProcessRequest built)
    {
        var cache = built.Environment.TryGetValue("XDG_CACHE_HOME", out var xdg) && !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(built.Environment["HOME"]!, ".cache");
        var folder = Directory.CreateDirectory(Path.Combine(cache, "kimi-code", "native", "2.2.0", "linux-x64", "0123abcd")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "libnative.so"), new byte[16]);
    }
}

using System.Text.Json;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>coai-mcp --host-facts</c> (E1.S2, S-N14; DN10 of <c>todo/PLAN_kimi_and_vibe_on_native_linux.md</c>): one JSON line on stdout — host kind, the kernel banner and whether it was read, each runtime's admission answer,
/// the keyring-session facts as recorded facts — read-only, exit 0, before any transport. It is the product's own reading, to be set beside the facts script's raw one.
/// </summary>
[Collection("console-out")]
public sealed class HostFactsModeTests : IDisposable
{
    private const string NoSuchMode =
        "coai-mcp --host-facts is not a mode — N-V6 has no product reading to set beside the facts script's own (DN10: two readings)";

    private readonly string _data = Directory.CreateTempSubdirectory("coai-host-facts-").FullName;
    private readonly string? _dirWas = Environment.GetEnvironmentVariable("COAI_DATA_DIR");
    private readonly string? _sideWas = Environment.GetEnvironmentVariable("COAI_DATA_SIDE");

    public HostFactsModeTests()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _data);
        Environment.SetEnvironmentVariable("COAI_DATA_SIDE", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dirWas);
        Environment.SetEnvironmentVariable("COAI_DATA_SIDE", _sideWas);
        Directory.Delete(_data, recursive: true);
    }

    private static readonly KeyringSessionFacts NoKeyring = new(false, false, [], KeyringSessionFacts.Read);

    private static HostFactsReport Report(HostKind kind, BannerReading banner, WslGeneration generation, KeyringSessionFacts? keyring = null) =>
        HostFactsReport.Of("coai-mcp 9.9.9", kind, banner, generation, keyring ?? NoKeyring);

    private static async Task<(string Out, int Code)> RunAsync() => await Stdout.OfAsync(() => Program.RunAsync(["--host-facts"]));

    private static JsonElement Parsed(string line) => JsonDocument.Parse(line).RootElement.Clone();

    // ---------- the mode exists ----------

    [Fact]
    public void HostFacts_IsAMode_NotAnUnknownArgument()
    {
        Program.Classify(["--host-facts"]).Should().Be(Program.Startup.HostFacts, NoSuchMode);
    }

    [Fact]
    public void HostFacts_IsNotAWriterMode()
    {
        Program.WriterModes.Should().NotContain(Program.Startup.HostFacts, "it writes nothing, so a missing named data folder must not stop it — it never opens one");
    }

    // ---------- S-N14 : the line ----------

    [Fact]
    public async Task ThePrintedLine_IsOne_ParsesAndCarriesEveryField_AndExits0()
    {
        var (said, code) = await RunAsync();

        code.Should().Be(0, NoSuchMode);
        said.Should().NotBeNullOrWhiteSpace("--host-facts printed nothing on stdout — its line went somewhere else, and stdout is the mode's entire interface");
        said.TrimEnd().Split('\n').Should().HaveCount(1, "the mode prints ONE line");
        var line = Parsed(said);

        line.GetProperty("server").GetString().Should().StartWith("coai-mcp ");
        line.GetProperty("hostKind").GetString().Should().Be(HostKinds.Word(HostKinds.Current));
        line.GetProperty("banner").EnumerateObject().Select(field => field.Name).Should().Equal("read", "text", "generation");
        line.GetProperty("admission").EnumerateObject().Select(field => field.Name).Should().Equal("Vibe", "Kimi");
        line.GetProperty("keyringSession").EnumerateObject().Select(field => field.Name).Should().Equal("busAddressSet", "busSocket", "daemons", "listing");
        line.GetProperty("decides").GetString().Should().Be("admission only; keyringSession is recorded, never read by the admission");
    }

    [Fact]
    public async Task TheMode_CreatesNothingUnderTheDataFolder()
    {
        await RunAsync();

        Directory.EnumerateFileSystemEntries(_data).Should().BeEmpty("a read-only mode made something under the data folder");
    }

    /// <summary>The CI runner is a native Linux VM: the REAL line says linux, banner read, Vibe admitted (E2.S2, research/RESULTS_vibe_confinement.md §32) and Kimi admitted (the owner's decision of 2026-10-23, with a note).</summary>
    [Fact]
    public async Task OnANativeLinuxHost_TheRealLineSaysLinux_BannerRead_VibeAdmitted_AndKimiAdmitted()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && !VendorDiagnosis.OnWsl, "the real line of a native Linux host: runs on the Linux CI legs");

        var (said, _) = await RunAsync();
        var line = Parsed(said);

        line.GetProperty("hostKind").GetString().Should().Be("linux");
        line.GetProperty("banner").GetProperty("read").GetBoolean().Should().BeTrue("the CI runner's /proc/version is readable");
        line.GetProperty("banner").GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace();
        line.GetProperty("admission").GetProperty("Vibe").GetString().Should().BeEmpty("E2 admits Vibe on a native Linux host with a read banner");
        line.GetProperty("admission").GetProperty("Kimi").GetString().Should().BeEmpty("Kimi is admitted on a native Linux host with a read banner by the owner's decision of 2026-10-23");
        line.GetProperty("keyringSession").GetProperty("listing").GetString().Should().BeOneOf(KeyringSessionFacts.Read, KeyringSessionFacts.CannotTell);
    }

    // ---------- the report, for any host ----------

    [Fact]
    public void ANativeLinuxHostWithAReadBanner_AdmitsVibeAndKimi()
    {
        var line = Parsed(HostFactsMode.Json(Report(HostKind.Linux, new BannerReading(true, "Linux version 6.8.0-45-generic (buildd@lcy02) #45-Ubuntu SMP\n"), WslGeneration.None)));

        line.GetProperty("hostKind").GetString().Should().Be("linux");
        line.GetProperty("banner").GetProperty("read").GetBoolean().Should().BeTrue();
        line.GetProperty("banner").GetProperty("text").GetString().Should().Be("Linux version 6.8.0-45-generic (buildd@lcy02) #45-Ubuntu SMP", "the trailing newline of /proc/version is not part of the banner");
        line.GetProperty("banner").GetProperty("generation").GetString().Should().Be("None");
        line.GetProperty("admission").GetProperty("Vibe").GetString().Should().BeEmpty("a read, non-WSL banner admits Vibe (E2.S2)");
        line.GetProperty("admission").GetProperty("Kimi").GetString().Should().BeEmpty("a read, non-WSL banner admits Kimi by the owner's decision of 2026-10-23");
    }

    [Fact]
    public void AnUnreadableBanner_IsStatedAsUnread_AndStaysRefused()
    {
        var line = Parsed(HostFactsMode.Json(Report(HostKind.Linux, BannerReading.Unread, WslGeneration.Unreadable)));

        line.GetProperty("banner").GetProperty("read").GetBoolean().Should().BeFalse();
        line.GetProperty("banner").GetProperty("generation").GetString().Should().Be("Unreadable");
        line.GetProperty("admission").GetProperty("Vibe").GetString().Should().Contain("kernel banner", "an unreadable banner on a native Linux host stays refused, with the banner sentence");
        line.GetProperty("admission").GetProperty("Kimi").GetString().Should().Contain("kernel banner", "an unreadable banner stays refused for Kimi too: coai cannot tell native Linux from WSL1");
    }

    [Fact]
    public void AWsl2Host_ReportsBothRuntimesAdmitted()
    {
        var line = Parsed(HostFactsMode.Json(Report(HostKind.Wsl, new BannerReading(true, "Linux version 5.15.153.1-microsoft-standard-WSL2"), WslGeneration.Wsl2)));

        line.GetProperty("hostKind").GetString().Should().Be("wsl");
        line.GetProperty("admission").GetProperty("Vibe").GetString().Should().BeEmpty();
        line.GetProperty("admission").GetProperty("Kimi").GetString().Should().BeEmpty();
    }

    /// <summary>The keyring-session facts decide nothing: the admission is the same whatever they say.</summary>
    [Fact]
    public void TheKeyringSessionFacts_AreRecorded_AndNeverChangeTheAdmission()
    {
        var quiet = Report(HostKind.Linux, new BannerReading(true, "Linux version 6.8.0"), WslGeneration.None);
        var loud = Report(HostKind.Linux, new BannerReading(true, "Linux version 6.8.0"), WslGeneration.None, new KeyringSessionFacts(true, true, ["gnome-keyring-daemon"], KeyringSessionFacts.Read));

        loud.Admission.Should().Be(quiet.Admission, "a keyring-session fact changed an admission answer — they are recorded, not decided on");
        var recorded = Parsed(HostFactsMode.Json(loud)).GetProperty("keyringSession");
        recorded.GetProperty("busAddressSet").GetBoolean().Should().BeTrue();
        recorded.GetProperty("busSocket").GetBoolean().Should().BeTrue();
        recorded.GetProperty("daemons").EnumerateArray().Select(name => name.GetString()).Should().Equal("gnome-keyring-daemon");
    }

    [Fact]
    public void TheSentencesAreWrittenAsText_NotEscaped()
    {
        HostFactsMode.Json(Report(HostKind.Wsl, BannerReading.Unread, WslGeneration.Wsl1)).Should().Contain("WSL1 host — WSL1 translates", "the em dash was escaped to \\u2014");
    }

    /// <summary>G2 of the code gate (2026-10-21): the line is serialized by the source-generated context, so any value — a quote, a backslash, a newline, a non-ASCII character, an emoji — comes back from <c>JsonDocument.Parse</c> as the same string, and the line stays ONE line.</summary>
    [Fact]
    public void AValueWithAQuoteABackslashANewlineAndNonAscii_RoundTripsThroughJsonDocument_OnOneLine()
    {
        const string awkward = "say \"hi\" C:\temp\new\nsecond line\ttab — ключ ✓ \U0001F600";
        var report = HostFactsReport.Of(awkward, HostKind.Linux, new BannerReading(true, awkward), WslGeneration.None, new KeyringSessionFacts(true, false, [awkward], awkward));

        var json = HostFactsMode.Json(report);
        var line = Parsed(json);

        json.Should().NotContain("\n", "a newline inside a value must be escaped, or the mode prints more than ONE line");
        line.GetProperty("server").GetString().Should().Be(awkward);
        line.GetProperty("banner").GetProperty("text").GetString().Should().Be(awkward);
        line.GetProperty("keyringSession").GetProperty("daemons")[0].GetString().Should().Be(awkward);
        line.GetProperty("keyringSession").GetProperty("listing").GetString().Should().Be(awkward);
        json.Should().Contain("ключ ✓", "non-ASCII is written as text, not as \\uXXXX");
    }
}

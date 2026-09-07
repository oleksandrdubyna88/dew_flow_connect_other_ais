using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A runtime this build knows survives being PARSED out of the vendor list.
/// </summary>
/// <remarks>
/// <para><b>The defect, and it was the second copy of it.</b> <c>PanelSettings.RuntimeOf</c> held a
/// hand-written set — gemini, claude, antigravity, everything else codex — and <c>local</c> was
/// never added. So a local vendor arrived as a CODEX vendor carrying a base URL, which is the shape
/// that means "a custom OpenAI endpoint needing a vault key". No key existed, the auth check
/// answered <c>unavailable</c>, and `BuildWork` drops unavailable vendors: the round opened with
/// <c>0 reviewer(s)</c> while <c>providers</c> went on reporting the reviewer as fine.</para>
///
/// <para>The extension had the IDENTICAL defect in its own copy of the same set, found days
/// earlier and fixed by deriving its type from its list. This is the same fix on this side: the
/// names come from <see cref="ReviewerRuntimeSelector.RuntimeNames"/>, which is where a vendor is
/// actually added.</para>
///
/// <para><b>Why the first test missed it.</b> `LocalReviewerRunsTests` builds a
/// <c>ProviderSettings</c> directly with <c>Runtime = "local"</c> — downstream of the parser that
/// was corrupting it. A unit test placed one layer below the defect is a test that passes.</para>
/// </remarks>
public class VendorRuntimeSurvivesParsingTests
{
    private static ProviderSettings Parse(string runtime, string baseUrl = "") =>
        PanelSettings.ParseVendors(
            $$"""[{"id":"v","runtime":"{{runtime}}","model":"m","baseUrl":"{{baseUrl}}"}]""")
            .Should().ContainSingle().Subject;

    [Theory]
    [InlineData("codex")]
    [InlineData("gemini")]
    [InlineData("claude")]
    [InlineData("antigravity")]
    [InlineData("local")]
    [InlineData("remote")]
    public void EveryRuntimeThisBuildKnowsSurvivesParsing(string runtime)
    {
        Parse(runtime).Runtime.Should().Be(runtime, "a vendor that arrives as another runtime runs the wrong thing");
    }

    [Fact]
    public void EveryNameTheSelectorKnowsIsOneTheParserKeeps()
    {
        // The guard against a THIRD copy: add a runtime to the selector and this fails until the
        // parser accepts it, whatever anybody remembered to update.
        foreach (var name in ReviewerRuntimeSelector.RuntimeNames)
        {
            Parse(name).Runtime.Should().Be(name);
        }
    }

    [Fact]
    public void ALocalVendorParsedFromTheListIsRunnable()
    {
        // End to end across the two layers that each had their own version of this bug: parsed,
        // then asked whether it can run. Either half alone said yes.
        var vendor = Parse("local", "http://127.0.0.1:11434/v1");

        PanelService.AuthOf(vendor, hasVaultKey: false).Auth
            .Should().NotBe("unavailable", "this is what emptied the round");
    }

    [Fact]
    public void ARemoteVendorParsedFromTheListIsRunnableONCEThisMachineHasSignedIn()
    {
        // The same two layers, for the runtime added by the Team server work. A remote vendor HAS a
        // base URL — the server's — which is the shape that used to mean "a custom OpenAI endpoint
        // needing a vault key", so this is the exact ground the `local` defect was found on.
        var vendor = Parse("remote", "https://coai.example.com");

        PanelService.AuthOf(vendor, hasVaultKey: false, hasServerToken: true).Auth
            .Should().Be("server token");
        PanelService.AuthOf(vendor, hasVaultKey: true, hasServerToken: false).Auth
            .Should().Be("unavailable", "a vault key is not what a Team server authenticates with");
    }

    [Fact]
    public void AnUnknownRuntimeStillBecomesCodex()
    {
        // Unchanged and deliberate: a name from a newer panel is a custom vendor on the Codex CLI
        // against its own base URL, not a row that launches nothing.
        Parse("something-from-a-newer-panel").Runtime.Should().Be("codex");
    }

    [Fact]
    public void AnAbsentRuntimeStaysEmptySoTheIdDecides()
    {
        PanelSettings.ParseVendors("""[{"id":"gemini","model":"m"}]""")
            .Should().ContainSingle().Subject.Runtime.Should().BeEmpty();
    }

    /// <summary>
    /// The JSON the extension writes for a Team-server row reaches the adapter as the name the
    /// SERVER knows, not as the row's own id.
    /// </summary>
    /// <remarks>
    /// This is the twin of the extension's <c>settingsReach</c> assertion, and it lives here for the
    /// reason the token-file vectors do: each side was self-consistent and the seam between them was
    /// not. <c>vendorsEnv</c> never wrote <c>remoteVendor</c>, so every Team-server review reached
    /// <c>coai.remsoft.dev</c> asking for a vendor called <c>remsoftdev-claude</c> and was refused as
    /// one it does not offer — which reads exactly like a typo in a name nobody typed.
    /// </remarks>
    [Fact]
    public void ATeamServerRowReachesTheAdapterUnderTheServersOwnNameForIt()
    {
        var vendor = PanelSettings.ParseVendors(
            """[{"id":"remsoftdev-claude","runtime":"remote","model":"haiku","baseUrl":"https://coai.remsoft.dev","remoteVendor":"claude"}]""")
            .Should().ContainSingle().Subject;

        vendor.Identity().VendorOnServer.Should().Be(
            "claude", "the row id is unique across servers; the vendor name is what the server knows");
        vendor.Provider.Should().Be("remsoftdev-claude", "the row still owns its id, its history and its key");
    }

    [Fact]
    public void ARowWithNoRemoteVendorFallsBackToItsId_WhichIsTheShapeThatUsedToShip()
    {
        // Kept as a characterisation of the fallback rather than an endorsement of it: a row written
        // by hand, or by a build older than this one, still resolves to SOMETHING. What changes is
        // that `providers` now says the row does not record the name its server knows it by, instead
        // of letting it fail later as a vendor that does not exist.
        var vendor = PanelSettings.ParseVendors(
            """[{"id":"remsoftdev-claude","runtime":"remote","model":"haiku","baseUrl":"https://coai.remsoft.dev"}]""")
            .Should().ContainSingle().Subject;

        vendor.Identity().VendorOnServer.Should().Be("remsoftdev-claude");
    }
}

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
}

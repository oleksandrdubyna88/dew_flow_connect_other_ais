using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A vendor's reviewer reports the VENDOR's id, not the runtime's name.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> The built-in runtimes hard-coded their <c>Provider</c>: <c>CodexRuntime</c>
/// said "codex", <c>ClaudeRuntime</c> said "claude", whatever the vendor that selected them was
/// called. So two vendors on one runtime — <c>claude</c> and <c>my-claude</c>, or <c>codex</c> and
/// a <c>local</c> row that an older parser had rewritten to codex — produced two invocations
/// with the SAME provider/role key, and <c>LiveRound</c>'s dictionary threw on the duplicate before
/// any model was reached. Reported from a colleague's machine as "every round dies on a duplicate
/// reviewer key". And a lone <c>my-claude</c> did not crash: it quietly recorded its usage, its
/// findings and its vault-key lookup under <c>claude</c>, the name of a different row.</para>
///
/// <para><c>LocalRuntime</c> and <c>CustomCodexRuntime</c> already took the id. The built-ins now do
/// too, defaulting to their own name so <see cref="ReviewerRuntimeSelector.Default"/> and every
/// existing call site are unchanged.</para>
/// </remarks>
public class VendorKeepsItsOwnNameTests
{
    private static ProviderSettings Vendor(string id, string runtime, string baseUrl = "") =>
        new(id) { Enabled = true, Runtime = runtime, BaseUrl = baseUrl };

    [Theory]
    [InlineData("my-claude", "claude")]
    [InlineData("second-codex", "codex")]
    [InlineData("agy-two", "antigravity")]
    [InlineData("gem", "gemini")]
    public void ARenamedVendorReportsItsOwnId(string id, string runtime)
    {
        var reviewer = PanelService.RuntimeFor(Vendor(id, runtime));

        reviewer.Should().NotBeNull();
        reviewer!.Provider.Should().Be(id, "the id names the row, its history and its vault key");
    }

    [Fact]
    public void TwoVendorsOnOneRuntimeAreTwoReviewers_NotOneKeyTwice()
    {
        // The crash: both used to answer "claude", and the round's provider/role dictionary threw.
        var first = PanelService.RuntimeFor(Vendor("claude", "claude"))!.Provider;
        var second = PanelService.RuntimeFor(Vendor("my-claude", "claude"))!.Provider;

        first.Should().NotBe(second);
    }

    [Fact]
    public void TheDefaultRowsKeepTheNamesEverythingElseExpects()
    {
        // The selector keys its lookup by Provider and the tests construct the runtimes bare, so
        // the DEFAULT id has to remain the runtime's own name.
        PanelService.RuntimeFor(Vendor("codex", "codex"))!.Provider.Should().Be("codex");
        PanelService.RuntimeFor(Vendor("claude", "claude"))!.Provider.Should().Be("claude");
        new CodexRuntime().Provider.Should().Be("codex");
        ReviewerRuntimeSelector.Default.Find("codex").Should().NotBeNull();
    }

    [Fact]
    public void ACustomEndpointVendorStillReportsItsOwnId()
    {
        // Already right before this change; pinned so the base-url arm cannot regress the other way.
        PanelService.RuntimeFor(Vendor("mistral", "codex", "https://api.mistral.ai/v1"))!
            .Provider.Should().Be("mistral");
    }

    [Fact]
    public void TwoRowsWithOneIdCollapseToOne_BecauseTheIdIsTheKey()
    {
        // The extension already refuses two rows with one id; the server did not, and a hand-edited
        // settings file is the ordinary way that reaches it. The first wins, as in the extension.
        var vendors = PanelSettings.ParseVendors(
            """[{"id":"claude","runtime":"claude","model":"opus"},{"id":"claude","runtime":"claude","model":"haiku"}]""");

        vendors.Should().ContainSingle().Which.Model.Should().Be("opus");
    }
}

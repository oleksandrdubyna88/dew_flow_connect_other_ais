using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A configured local reviewer is actually launched — it was not, and nothing said so.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> Three places decide what a vendor is from the same two fields, and two
/// of them had been taught that a local vendor is not a codex one: <c>RuntimeFor</c> and
/// <c>RuntimeNameOf</c> both check <c>runtime == "local"</c> BEFORE the base-url arm, each with a
/// comment saying why — "a local vendor IS a vendor with a base url". The authentication check was
/// the third place and was never updated. It read the base URL, concluded the vendor needed a vault
/// key, answered <c>unavailable</c>, and `BuildWork` filters unavailable vendors out of the
/// round.</para>
///
/// <para><b>What that looked like.</b> A round opening with <c>0 reviewer(s)</c>, a verdict of
/// <c>call_human</c> reading "no reviewer answered — nothing was reviewed", and 0.0 seconds of
/// work. Meanwhile <c>providers</c> answered for the same vendor correctly, because IT has a local
/// arm — so the panel said the reviewer was fine and every round silently ran without it, which is
/// the worst of the available combinations. Found by running the local model against the hosted
/// models' baseline; nothing in the product reported it.</para>
///
/// <para><b>Why the first version of this test could not see it.</b> It asked <c>providers</c>,
/// which has its own local arm and was never wrong. The decision that mattered was a private
/// method used only when choosing who runs, so it is now a pure function taking everything it
/// needs — which is also what let the three readers of those two fields become one.</para>
/// </remarks>
public class LocalReviewerRunsTests
{
    private static ProviderSettings Local(string baseUrl = "http://127.0.0.1:11434/v1") =>
        new("local") { Enabled = true, Runtime = "local", Model = "qwen3.5:latest", BaseUrl = baseUrl };

    [Fact]
    public void ALocalReviewerIsRunnableWithNoVaultKeyAnywhere()
    {
        // The whole defect in one assertion. A vendor whose auth is "unavailable" is dropped from
        // the round, so this is not a cosmetic string: it decides whether the reviewer exists.
        var (auth, _) = PanelService.AuthOf(Local(), hasVaultKey: false);

        auth.Should().NotBe("unavailable", "a local engine authenticates with nothing");
    }

    [Fact]
    public void ALocalReviewerWithNoEndpointIsRunnableToo()
    {
        // An empty base URL means "the engine the probe found"; the runtime supplies its default.
        PanelService.AuthOf(Local(baseUrl: string.Empty), hasVaultKey: false)
            .Auth.Should().NotBe("unavailable");
    }

    [Fact]
    public void ItsNoteDoesNotAskForAKeyThatCannotExist()
    {
        // "needs a key under 'local' and the vault holds none" sent somebody to create a vault
        // entry for a thing that takes no credential.
        PanelService.AuthOf(Local(), hasVaultKey: false)
            .Note.Should().NotContain("needs a key");
    }

    [Fact]
    public void AVendorRidingTheCodexCLIStillNeedsItsKey()
    {
        // The behaviour being carved around, unchanged: a custom OpenAI-compatible vendor DOES need
        // a vault key, and the base-url arm is what says so.
        var deepseek = new ProviderSettings("deepseek")
        {
            Enabled = true,
            Runtime = "codex",
            BaseUrl = "https://api.deepseek.com/v1",
        };

        var (auth, note) = PanelService.AuthOf(deepseek, hasVaultKey: false);

        auth.Should().Be("unavailable");
        note.Should().Contain("needs a key");
    }

    [Fact]
    public void AVaultKeyStillWinsForEverybody()
    {
        PanelService.AuthOf(Local(), hasVaultKey: true).Auth.Should().Be("vault key");
    }

    [Fact]
    public void AnOrdinaryCliVendorUsesItsOwnSignIn()
    {
        var codex = new ProviderSettings("codex") { Enabled = true, Runtime = "codex" };

        PanelService.AuthOf(codex, hasVaultKey: false).Auth.Should().Be("own auth");
    }
}

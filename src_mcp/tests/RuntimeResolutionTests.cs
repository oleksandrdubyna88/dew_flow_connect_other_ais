using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// "What is this vendor" — asked of the library, which is where both binaries now ask it.
/// </summary>
/// <remarks>
/// The same questions <c>PanelService</c> answers through its delegations, tested here against the
/// type that actually decides. Two incidents are behind every case in this file: `local` added to a
/// type and not to a list beside it, and an id consulted before an explicit runtime. Both were
/// found by running a model, days apart, and neither was reported by anything.
/// </remarks>
public sealed class RuntimeResolutionTests
{
    private static VendorIdentity Vendor(string provider, string runtime = "", string baseUrl = "") =>
        new(provider, runtime, baseUrl);

    [Fact]
    public void ALocalVendor_IsLocal_EvenThoughItHasABaseUrl()
    {
        var local = Vendor("local", "local", "http://127.0.0.1:11434/v1");

        RuntimeResolution.NameOf(local).Should().Be("local",
            "a local vendor IS a vendor with a base url, and the base-url arm means ride the Codex CLI");
        RuntimeResolution.For(local).Should().BeOfType<LocalRuntime>();
    }

    [Fact]
    public void AnExplicitRuntime_OutranksTheId()
    {
        // `claude` used to work by accident, because the id was consulted first — and `my-claude`,
        // the same runtime under another name, silently ran the Codex CLI.
        var renamed = Vendor("my-claude", "claude");

        RuntimeResolution.NameOf(renamed).Should().Be("claude");
        RuntimeResolution.For(renamed).Should().BeOfType<ClaudeRuntime>();
    }

    [Fact]
    public void AVendorKeepsItsOwnName_WhateverRuntimeItGets()
    {
        RuntimeResolution.For(Vendor("my-claude", "claude"))!.Provider.Should().Be("my-claude",
            "the round's dictionary is keyed by the vendor, so two rows on one runtime must not collide");
    }

    [Fact]
    public void AnIdAlone_IsEnough_WhenNoRuntimeWasNamed()
    {
        RuntimeResolution.NameOf(Vendor("codex")).Should().Be("codex");
        RuntimeResolution.For(Vendor("codex"))!.Provider.Should().Be("codex");
    }

    [Fact]
    public void ABaseUrlWithoutARuntime_RidesTheCodexCli()
    {
        var mistral = Vendor("mistral", "codex", "https://api.mistral.ai/v1");

        RuntimeResolution.NameOf(mistral).Should().Be("codex");
        RuntimeResolution.For(mistral).Should().BeOfType<CustomCodexRuntime>();
    }

    [Fact]
    public void AProviderNobodyKnows_ResolvesToNothing()
    {
        RuntimeResolution.For(Vendor("acme")).Should().BeNull(
            "an unknown provider is a refusal naming the catalog, never a silent default");
    }

    // ---------- who may review at all ----------

    [Fact]
    public void AVaultKey_AnswersForEveryVendor()
    {
        RuntimeResolution.AuthOf(Vendor("deepseek", baseUrl: "https://api.deepseek.com/v1"), hasVaultKey: true)
            .Auth.Should().Be("vault key");
    }

    [Fact]
    public void ALocalEngine_NeedsNoKey()
    {
        // The defect this pins: the auth decision was the one of three readers of these fields never
        // told that a local vendor is not a codex one. It answered "unavailable", and every round
        // opened with zero reviewers while `providers` reported the vendor as fine.
        var (auth, note) = RuntimeResolution.AuthOf(
            Vendor("local", "local", "http://127.0.0.1:11434/v1"), hasVaultKey: false);

        auth.Should().Be("own auth");
        note.Should().Contain("no key");
    }

    [Fact]
    public void AVendorWithItsOwnEndpointAndNoKey_CannotReview()
    {
        var (auth, note) = RuntimeResolution.AuthOf(
            Vendor("mistral", "codex", "https://api.mistral.ai/v1"), hasVaultKey: false);

        auth.Should().Be("unavailable", "an unavailable answer removes the vendor from the round");
        note.Should().Contain("mistral");
    }

    [Fact]
    public void ACliThatSignsInAsItself_NeedsNothing()
    {
        RuntimeResolution.AuthOf(Vendor("codex"), hasVaultKey: false).Auth.Should().Be("own auth");
    }
}

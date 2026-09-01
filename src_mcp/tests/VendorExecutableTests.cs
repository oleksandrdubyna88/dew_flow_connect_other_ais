using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A vendor's executable path has to be settable, and with the panel in use it was not.
/// </summary>
/// <remarks>
/// <para><c>COAI_EXE_&lt;VENDOR&gt;</c> was read in ONE branch of the settings — the
/// <c>COAI_PROVIDERS</c> fallback — and the panel always writes <c>COAI_VENDORS</c> instead. So the
/// moment anybody opened the panel, the only way to say WHERE a CLI lives stopped working, silently
/// and for every vendor.</para>
/// <para>Where that bites: WSL. `codex` resolves there to the Windows npm shim on the interop PATH,
/// which runs Linux node against a Windows install and dies with a missing native dependency. The
/// native Linux codex sits at <c>~/.npm-global/bin/codex</c> and nothing could point at it, so a
/// WSL round failed every time whatever anybody configured.</para>
/// </remarks>
public sealed class VendorExecutableTests
{
    [Fact]
    public void AVendorsExecutablePath_TravelsInTheVendorList()
    {
        var vendors = PanelSettings.ParseVendors(
            """[{"id":"codex","runtime":"codex","model":"","baseUrl":"","executablePath":"/home/jinx/.npm-global/bin/codex"}]""");

        vendors.Should().ContainSingle()
            .Which.ExecutablePath.Should().Be("/home/jinx/.npm-global/bin/codex");
    }

    [Fact]
    public void WithNoPathInTheList_TheEnvironmentStillAnswers()
    {
        // The env variable predates the panel and is what a scripted or containerised run has.
        // Dropping it the moment a vendor list appeared is what made this unfixable from either side.
        var env = new Dictionary<string, string>
        {
            ["COAI_VENDORS"] = """[{"id":"codex","runtime":"codex","model":"","baseUrl":""}]""",
            ["COAI_EXE_CODEX"] = "/home/jinx/.npm-global/bin/codex",
        };

        PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name))
            .Providers.Should().ContainSingle()
            .Which.ExecutablePath.Should().Be("/home/jinx/.npm-global/bin/codex");
    }

    [Fact]
    public void APathInTheList_OutranksTheEnvironment()
    {
        var env = new Dictionary<string, string>
        {
            ["COAI_VENDORS"] = """[{"id":"codex","runtime":"codex","model":"","baseUrl":"","executablePath":"/from/the/list"}]""",
            ["COAI_EXE_CODEX"] = "/from/the/env",
        };

        PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name))
            .Providers.Should().ContainSingle().Which.ExecutablePath.Should().Be("/from/the/list",
                "the list is the specific statement about THIS vendor");
    }

    [Fact]
    public void AVendorIdWithADash_StillFindsItsVariable()
    {
        // `my-claude` → COAI_EXE_MY_CLAUDE, the same derivation the key variable uses.
        var env = new Dictionary<string, string>
        {
            ["COAI_VENDORS"] = """[{"id":"my-claude","runtime":"claude","model":"","baseUrl":""}]""",
            ["COAI_EXE_MY_CLAUDE"] = "/usr/bin/claude",
        };

        PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name))
            .Providers.Should().ContainSingle().Which.ExecutablePath.Should().Be("/usr/bin/claude");
    }
}

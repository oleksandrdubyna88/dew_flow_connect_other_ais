using Xunit;
using FluentAssertions;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// An id is not a vendor. The runtime and the model are the vendor.
/// </summary>
/// <remarks>
/// <para>The bench's first campaign passed `COAI_PROVIDERS=codex,gemini,local` — bare ids — and the
/// server did exactly as told: it built a vendor called `gemini` on the RETIRED Gemini CLI, and a
/// local vendor with no model. Six of nine reviewers in a code round failed, and the report went out
/// blaming the release.</para>
/// <para>The operator's own configuration had been right for days: a vendor NAMED gemini whose
/// runtime is `antigravity` (its CLI answers 1.1.26 where the retired one answers 0.57.0), and a
/// local vendor with its model. So the bench reads the configuration and an arm only selects from
/// it — an id that is not there is refused rather than invented.</para>
/// </remarks>
public sealed class VendorsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-vendors-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The panel writes the vendor list as a JSON STRING inside the settings object.</summary>
    private string SettingsWith(string vendorsJson)
    {
        var file = Path.Combine(_dir, "settings.json");
        var escaped = vendorsJson.Replace("\"", "\\\"", StringComparison.Ordinal);
        File.WriteAllText(file, $$"""{ "COAI_VENDORS": "{{escaped}}" }""");

        return file;
    }

    private static readonly VendorConfig[] Real =
    [
        new("codex", "codex", "gpt-5.6-luna"),
        new("gemini", "antigravity", "gemini-3.7-flash-low"),
        new("local", "local", "Qwen3.5-35B-A3B-Q5_vk128:latest"),
    ];

    private static readonly IReadOnlyDictionary<string, string> NoOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void TheVendorNamedGemini_RunsWhateverItsRuntimeSays()
    {
        var file = SettingsWith("""
            [{"id":"gemini","runtime":"antigravity","model":"gemini-3.7-flash-low","baseUrl":"","executablePath":""}]
            """);

        var read = Vendors.Read(file);

        read.Should().ContainSingle();
        read[0].Runtime.Should().Be("antigravity", "the id says what it is CALLED, not what it runs");
        read[0].Model.Should().Be("gemini-3.7-flash-low");
    }

    [Fact]
    public void AnArmSelectsFromTheConfiguration_CarryingRuntimeAndModel()
    {
        var (vendors, refusal) = Vendors.For("codex,gemini", Real, NoOverrides);

        refusal.Should().BeEmpty();
        vendors.Select(v => v.Runtime).Should().Equal("codex", "antigravity");
        vendors.Should().OnlyContain(v => v.Model.Length > 0, "a vendor with no model answers 400 to every round");
    }

    [Fact]
    public void AnIdNobodyConfigured_IsRefusedByName_NotInvented()
    {
        var (vendors, refusal) = Vendors.For("codex,nonesuch", Real, NoOverrides);

        vendors.Should().BeEmpty();
        refusal.Should().Contain("nonesuch");
        refusal.Should().Contain("codex, gemini, local", "the refusal says what there IS to choose from");
    }

    [Fact]
    public void AModelOverrideWins_BecauseThatIsHowLocalAgainstHostedIsAsked()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["local"] = "Gemma4-26B-A4B-Uncensored_vk128:latest",
        };

        var (vendors, _) = Vendors.For("local", Real, overrides);

        vendors.Should().ContainSingle().Which.Model.Should().Be("Gemma4-26B-A4B-Uncensored_vk128:latest");
        vendors[0].Runtime.Should().Be("local", "an override changes the model, never what runs it");
    }

    [Fact]
    public void WhatIsHandedToTheServer_CarriesTheRuntime()
    {
        var setting = Vendors.AsSetting(Real);

        setting.Should().Contain("antigravity");
        setting.Should().Contain("Qwen3.5-35B");
    }

    [Fact]
    public void NoSettingsFile_IsEmptyRatherThanAGuess() =>
        Vendors.Read(Path.Combine(_dir, "nothing-here.json")).Should().BeEmpty();

    [Fact]
    public void ASettingsFileThatDoesNotParse_IsAlsoEmpty()
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, "{ not json");

        Vendors.Read(file).Should().BeEmpty();
    }
}

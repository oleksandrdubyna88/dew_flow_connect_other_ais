using Xunit;
using FluentAssertions;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// The bench measures the operator's machine, not a machine of its own invention.
/// </summary>
/// <remarks>
/// It used to take only the vendors and leave thresholds, rounds per role, prompts and the
/// exhausted-policy at the server's defaults — so every number described a configuration nobody
/// runs. The same mistake as rebuilding vendors from ids, one level up.
/// </remarks>
public sealed class PanelSettingsFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-settings-").FullName;

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

    private string FileWith(string json)
    {
        var file = Path.Combine(_dir, "settings.json");
        File.WriteAllText(file, json);

        return file;
    }

    [Fact]
    public void EverySettingIsCarried_NotJustTheVendors()
    {
        var file = FileWith("""
            {
              "COAI_THRESHOLD_PLANCRITIQUE": "6",
              "COAI_ROUNDS_PLANCRITIQUE": "1",
              "COAI_ON_EXHAUSTED": "good_enough",
              "COAI_MAX_PER_PROVIDER": "3",
              "COAI_AUTONOMOUS": "true"
            }
            """);

        var settings = PanelSettingsFile.Read(file);

        settings.Should().HaveCount(5);
        settings["COAI_ON_EXHAUSTED"].Should().Be("good_enough");
        settings["COAI_THRESHOLD_PLANCRITIQUE"].Should().Be("6");
    }

    [Fact]
    public void AnythingThatIsNotOurs_IsLeftAlone()
    {
        // Somebody else's key in that file is not the bench's to hand to a server.
        var file = FileWith("""{ "COAI_AUTONOMOUS": "true", "somethingElse": "no" }""");

        PanelSettingsFile.Read(file).Should().ContainSingle().Which.Key.Should().Be("COAI_AUTONOMOUS");
    }

    [Fact]
    public void AnOverrideWins_BecauseASettingsCOMBINATIONIsAlsoARunWorthMaking()
    {
        var fromFile = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COAI_MAX_CONCURRENCY"] = "3",
            ["COAI_ON_EXHAUSTED"] = "good_enough",
        };
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COAI_MAX_CONCURRENCY"] = "9",
        };

        var effective = PanelSettingsFile.Effective(fromFile, overrides);

        effective["COAI_MAX_CONCURRENCY"].Should().Be("9", "--set is how a combination is asked for");
        effective["COAI_ON_EXHAUSTED"].Should().Be("good_enough", "and everything else stays real");
    }

    [Fact]
    public void NoFile_IsEmptyRatherThanAGuess() =>
        PanelSettingsFile.Read(Path.Combine(_dir, "absent.json")).Should().BeEmpty();

    [Fact]
    public void AFileThatDoesNotParse_IsAlsoEmpty() =>
        PanelSettingsFile.Read(FileWith("{ not json")).Should().BeEmpty();

    [Fact]
    public void TheDescriptionIsSortedAndReadable()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COAI_ON_EXHAUSTED"] = "good_enough",
            ["COAI_AUTONOMOUS"] = "true",
        };

        var described = PanelSettingsFile.Describe(settings);

        described.Should().StartWith("  COAI_AUTONOMOUS = true");
        described.Should().Contain("COAI_ON_EXHAUSTED = good_enough");
    }

    [Fact]
    public void TheVendorListIsSummarised_BecauseALineNobodyReadsIsALineNobodyChecks()
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COAI_VENDORS"] = """[{"id":"codex"},{"id":"gemini"},{"id":"local"}]""",
        };

        PanelSettingsFile.Describe(settings).Should().Contain("3 vendor(s), listed above");
    }
}

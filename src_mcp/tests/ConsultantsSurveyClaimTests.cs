using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One consultants survey per window for every server on a data directory
/// (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D2): seven sessions restarting together probed four
/// CLIs seven times on 2026-10-06. The claim's NAME carries the slot and the identity, so nothing is taken over.
/// </summary>
public sealed class ConsultantsSurveyClaimTests : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly DateTime Now = new(2026, 10, 6, 12, 1, 0, DateTimeKind.Utc);

    private readonly string _data = Directory.CreateTempSubdirectory("coai-claim-").FullName;

    private string Health => Path.Combine(_data, "consultations", "health");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void ManyStartsAtOnce_OneTakesTheSurvey()
    {
        var taken = 0;
        Parallel.For(0, 16, _ =>
        {
            if (ConsultantsSurveyClaim.Take(Health, "v1|wsl|s", Now, Window).Taken)
            {
                Interlocked.Increment(ref taken);
            }
        });

        taken.Should().Be(1, "CreateNew decides a race for one name, and the name is the same for all of them");
    }

    [Fact]
    public void ALaterStartInTheSameWindow_IsCovered_AndSaysWhy()
    {
        ConsultantsSurveyClaim.Take(Health, "v1|wsl|s", Now, Window).Taken.Should().BeTrue();

        var later = ConsultantsSurveyClaim.Take(Health, "v1|wsl|s", Now.AddMinutes(2), Window);

        later.Taken.Should().BeFalse();
        later.Why.Should().Contain("another server took the survey");
    }

    [Theory]
    [InlineData("v2|wsl|s", 0)] // an upgrade
    [InlineData("v1|windows|s", 0)] // the other side
    [InlineData("v1|wsl|settings-rewritten", 0)] // the panel changed the settings
    [InlineData("v1|wsl|s", 5)] // the next window
    public void ANewBuildSideSettingsOrWindow_SurveysAgain(string identity, int minutesLater)
    {
        ConsultantsSurveyClaim.Take(Health, "v1|wsl|s", Now, Window).Taken.Should().BeTrue();

        ConsultantsSurveyClaim.Take(Health, identity, Now.AddMinutes(minutesLater), Window).Taken.Should().BeTrue();
    }

    [Fact]
    public void AReleasedClaim_LetsTheNextStartInTheWindowSurvey()
    {
        var first = ConsultantsSurveyClaim.Take(Health, "v1|wsl|s", Now, Window);

        first.Release();

        ConsultantsSurveyClaim.Take(Health, "v1|wsl|s", Now, Window).Taken.Should().BeTrue();
    }

    [Fact]
    public void ARelease_TakesOnlyItsOwnClaim()
    {
        var first = ConsultantsSurveyClaim.Take(Health, "v1|wsl|s", Now, Window);
        var claim = Directory.EnumerateFiles(Health, "*.claim").Single();
        File.WriteAllText(claim, "someone-else pid 1"); // a later holder of the same name

        first.Release();

        File.Exists(claim).Should().BeTrue("the file is no longer this claim's, so its release must not delete it");
    }

    [Fact]
    public void OldClaims_ArePrunedByTheNextTake()
    {
        Directory.CreateDirectory(Health);
        var old = Path.Combine(Health, "consultants.survey.0123456789abcdef0123.claim");
        File.WriteAllText(old, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - (Window * 3));

        ConsultantsSurveyClaim.Take(Health, "v1|wsl|s", DateTime.UtcNow, Window).Taken.Should().BeTrue();

        File.Exists(old).Should().BeFalse();
        Directory.EnumerateFiles(Health, "*.claim").Should().ContainSingle();
    }

    [Fact]
    public void TheIdentity_MovesWithTheSettingsFileAndTheClientEnvironment_ButNotWithTheVaultKey()
    {
        var settings = Path.Combine(_data, "settings.json");
        File.WriteAllText(settings, "{}");
        IEnumerable<KeyValuePair<string, string>> Env(string vendors, string key) =>
            [new("COAI_VENDORS", vendors), new(KeyVault.KeyVariable, key), new("PATH", "/bin")];

        var first = ConsultantsSurveyClaim.IdentityOf("0.44.0", "wsl", settings, () => Env("codex", "k1"));

        ConsultantsSurveyClaim.IdentityOf("0.44.0", "wsl", settings, () => Env("codex", "k2")).Should().Be(first, "the vault key is no part of a survey");
        ConsultantsSurveyClaim.IdentityOf("0.44.0", "wsl", settings, () => [.. Env("codex", "k1"), new("COAI_BUGS_KEY", "b"), new("COAI_SERVER_TOKEN", "t"), new("COAI_CALLER_SESSION", "run-7")])
            .Should().Be(first, "no secret goes into a hash on disk, and a per-run caller variable would make every start its own survey");
        ConsultantsSurveyClaim.IdentityOf("0.44.0", "wsl", settings, () => Env("codex,gemini", "k1")).Should().NotBe(first);
        File.WriteAllText(settings, "{ \"COAI_VENDORS\": \"x\" }");
        ConsultantsSurveyClaim.IdentityOf("0.44.0", "wsl", settings, () => Env("codex", "k1")).Should().NotBe(first);
        first.Should().NotContain("k1");
    }

    /// <summary>The wiring: a second start in the window writes nothing and probes nothing; a different client surveys.</summary>
    [Fact]
    public async Task ASecondServerStart_DoesNotSurveyAgain_ButADifferentClientDoes()
    {
        var settings = new PanelSettings { Providers = [], DataDir = _data };
        var file = Path.Combine(Health, "consultants.json");
        IEnumerable<KeyValuePair<string, string>> Client(string vendors) => [new("COAI_VENDORS", vendors)];

        await ConsultantsReadMode.WriteInBackground(settings, new NeverLaunched(), Serilog.Core.Logger.None, Noticing.None, () => Client("a"), CancellationToken.None);
        File.Exists(file).Should().BeTrue("the first start surveys");
        File.Delete(file);

        await ConsultantsReadMode.WriteInBackground(settings, new NeverLaunched(), Serilog.Core.Logger.None, Noticing.None, () => Client("a"), CancellationToken.None);
        File.Exists(file).Should().BeFalse("the same build and settings were surveyed within the window");

        await ConsultantsReadMode.WriteInBackground(settings, new NeverLaunched(), Serilog.Core.Logger.None, Noticing.None, () => Client("b"), CancellationToken.None);
        File.Exists(file).Should().BeTrue("another client environment is another survey");
    }

    [Fact]
    public async Task ASurveyCutShort_GivesItsClaimBack()
    {
        var settings = new PanelSettings { Providers = [], DataDir = _data };
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        await ConsultantsReadMode.WriteInBackground(settings, new NeverLaunched(), Serilog.Core.Logger.None, Noticing.None, () => [], stopped.Token);

        Directory.Exists(Health).Should().BeTrue();
        Directory.EnumerateFiles(Health, "*.claim").Should().BeEmpty("the next start must survey; nothing was written");
    }

    private sealed class NeverLaunched : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException($"a survey with no consultants to probe must not start '{request.Executable}'");
    }
}

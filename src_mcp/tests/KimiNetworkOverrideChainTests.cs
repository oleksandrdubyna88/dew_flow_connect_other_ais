using System.Text.Json;
using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The override of DL6 (<c>coai.kimiAllowNetworkSignIn</c>) THROUGH THE SERVER, link by link (J of the review of 2026-10-18). <c>KimiNetworkSignInSettingTests</c> proves the setting is parsed and that an invocation is
/// bound to the row's answer; the launch control's own tests prove the gate. What stood between them was never asserted: the roster hands the panel's setting to the row's <see cref="ReviewerSettings"/>, and the providers
/// answer shows the warning only when the setting is on. A deleted line or an inverted gate in either place would have left every other test green and made the override do nothing — or do everything.
/// </summary>
/// <remarks>The question fan-out's hand-over is asserted in <c>QuestionFanOutTests</c> (it needs that class's harness). No process is started here: the roster only BUILDS invocations, and the providers probe is answered by a stand-in.</remarks>
public sealed class KimiNetworkOverrideChainTests : IDisposable
{
    private const string Nfs = "25 1 0:50 / / rw - nfs4 srv:/home rw\n";

    private readonly KimiFixture _person = new();
    private readonly List<IDisposable> _scopes = [];

    public KimiNetworkOverrideChainTests()
    {
        _scopes.Add(_person);
        _scopes.Add(KimiHost.Using(new KimiHost(HostKind.Wsl, WslGeneration.Wsl2)));
        _scopes.Add(KimiSeams.Using(new KimiSeams(_person.PlacesAt, () => ("coai-mcp", []))));
    }

    public void Dispose()
    {
        Junctions.StandInMountInfo(null);
        for (var at = _scopes.Count - 1; at >= 0; at--)
        {
            _scopes[at].Dispose();
        }
    }

    private PanelService Service(bool allow) => new(
        new PanelSettings
        {
            DataDir = Directory.CreateDirectory(Path.Combine(_person.Folder, "data")).FullName,
            Providers = [new("kimi") { Enabled = true, Runtime = "kimi", Model = "kimi-code/k3" }],
            KimiAllowNetworkSignIn = allow,
        },
        VaultKeys.None("no vault"), default, new RecordingLauncher(stdOut: string.Empty), Serilog.Core.Logger.None, Noticing.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheReviewerRoster_HandsThePanelsOverrideToTheRowsLaunch(bool allow)
    {
        var work = Service(allow).Roster.BuildWork(
            [RoleCatalog.ArchitectureRole], string.Empty, "ctx", round: 1, stage: Stage.PlanReview, readsCheckout: false, codexTiers: CodexTiers.None).Reviewers;

        work.Should().ContainSingle();
        work[0].Invocation.Adapter.Should().BeOfType<KimiRuntime>().Which.AllowsNetworkSignIn.Should().Be(allow, "the launch control of this row asks the setting the panel read, and nothing else");
    }

    private async Task<JsonElement> KimiRowAsync(bool allow)
    {
        Junctions.StandInMountInfo(() => Nfs, _ => new MountIdentity.NotAsked());
        var answer = JsonDocument.Parse(await Service(allow).ProvidersAsync(TestContext.Current.CancellationToken)).RootElement;

        return answer.GetProperty("providers").EnumerateArray().Single(one => one.GetProperty("provider").GetString() == "kimi").Clone();
    }

    [Fact]
    public async Task TheProvidersAnswer_ShowsTheNetworkWarning_WithTheOverrideOn()
    {
        var row = await KimiRowAsync(allow: true);

        row.TryGetProperty("warning", out var warning).Should().BeTrue($"the override admits an nfs sign-in folder: the card says so ({row})");
        warning.GetString().Should().Contain("network storage (nfs4)").And.Contain("coai.kimiAllowNetworkSignIn").And.NotContain("\n");
    }

    [Fact]
    public async Task TheProvidersAnswer_ShowsNoNetworkWarning_WithTheOverrideOff()
    {
        var row = await KimiRowAsync(allow: false);

        (row.TryGetProperty("warning", out var warning) ? warning.GetString() ?? string.Empty : string.Empty).Should().NotContain("network storage", $"nothing is admitted, so nothing is warned about ({row})");
    }

    /// <summary>
    /// The consultant's hand-over (S5 meeting DL6 at the merge with main, 2026-10-27): a consultation's launch settings are composed by
    /// <c>ConsultantTurnInputs</c>, not by the roster or the fan-out, so the panel's answer must be copied there too — and the Kimi
    /// consultant's built launch must carry an adapter bound to it, or a person who admitted a network sign-in is refused on every consult.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConsultationsLaunch_CarriesThePanelsOverride_ToTheKimiAdapter(bool allow)
    {
        var row = new ProviderSettings("kimi") { Enabled = true, Runtime = "kimi", Model = "kimi-code/k3" };
        var data = Directory.CreateDirectory(Path.Combine(_person.Folder, "data")).FullName;
        var settings = ConsultantTurnInputs.Settings(row, row.Model, TimeSpan.FromMinutes(5), new PanelSettings { DataDir = data, KimiAllowNetworkSignIn = allow },
            VaultKeys.None("no vault"));

        settings.KimiAllowNetworkSignIn.Should().Be(allow, "the consultation's launch reads the panel's answer, as a reviewer's and a question row's do");

        // The conversation's own folder and its snapshot, as the service hands them (ConsultantPlaces) — a Kimi consultant refuses a launch
        // without either (D19, D6).
        var owned = settings with { OwnedScratch = Directory.CreateDirectory(Path.Combine(data, "conversation")).FullName };
        var built = new KimiConsultant(new KimiRuntime("kimi")).Build(
            new Runners.Consultation.ConsultantLaunch(_person.Folder, "a question", string.Empty, Directory.CreateDirectory(Path.Combine(data, "out")).FullName, owned)
            {
                Workdir = Directory.CreateDirectory(Path.Combine(data, "snapshot")).FullName,
            });

        built.Adapter.Should().BeOfType<KimiRuntime>().Which.AllowsNetworkSignIn.Should().Be(allow, "the launch control of this turn asks the setting the panel read");
    }
}

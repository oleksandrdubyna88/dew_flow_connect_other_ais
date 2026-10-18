using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The survivor-marker report on the Kimi row of the providers answer (DL9 and F16 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>, E6): <c>survivorMarker = { "path": …, "pids": […] }</c>, ABSENT when no marker stands,
/// <c>pids</c> empty for a marker that names nothing, never a marker of another sign-in, and a path <c>--kimi-clear-marker</c> accepts. The card's button appears only when the field does.
/// </summary>
public sealed class KimiSurvivorMarkerReportTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly KimiFixture _person = new();
    private readonly IDisposable _rooted;

    public KimiSurvivorMarkerReportTests() => _rooted = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));

    public void Dispose()
    {
        _rooted.Dispose();
        _person.Dispose();
    }

    private string MarkerPath => KimiCredentialsLock.MarkerPathFor(_person.Credentials);

    private KimiRuntime Runtime() => new("kimi", HostKind.Windows, new KimiSeams(_person.PlacesAt, () => ("coai-mcp", [])));

    private static ProcessIdentity Member(int pid) => new(pid, Started);

    private static KimiSurvivorMarkerDto Running(params ProcessIdentity[] tree) =>
        new(new ReaderMarkerDto(ReaderPhases.Running, 424242, Started, DateTimeOffset.UtcNow, tree), "/owned/kimi-root", "/people/sign-in");

    private static KimiSurvivorMarkerDto Spawning() =>
        new(new ReaderMarkerDto(ReaderPhases.Spawning, 424242, Started, DateTimeOffset.UtcNow), "/owned/kimi-root", "/people/sign-in");

    [Fact]
    public void WithNoMarker_NothingIsReported_AndTheWireFieldIsNull()
    {
        var runtime = Runtime();

        runtime.StandingMarker().Should().BeEmpty();
        PanelService.SurvivorMarkerOf(runtime).Should().BeNull("an absent field, so the card shows no button");
    }

    [Fact]
    public void AStandingMarker_IsReported_WithItsFullPath_AndThePidsItNames()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243), Member(424244)));

        var report = PanelService.SurvivorMarkerOf(Runtime());

        report.Should().NotBeNull("a marker stands for this sign-in");
        report!.Path.Should().Be(MarkerPath);
        report.Pids.Should().Equal(424243, 424244);
    }

    [Fact]
    public void ASpawningMarker_IsReported_WithNoPids()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning());

        var report = PanelService.SurvivorMarkerOf(Runtime());

        report.Should().NotBeNull();
        report!.Path.Should().Be(MarkerPath);
        report.Pids.Should().BeEmpty("a spawning marker names no process");
    }

    [Fact]
    public void AnUnreadableMarker_IsReported_BecauseItStillBlocksTheSignIn_WithNoPids()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
        File.WriteAllText(MarkerPath, "not json at all");

        var report = PanelService.SurvivorMarkerOf(Runtime());

        report.Should().NotBeNull("a marker coai cannot read stands: the launch is Busy over it");
        report!.Pids.Should().BeEmpty();
    }

    [Fact]
    public void AnotherSignInsMarker_IsNeverReported()
    {
        var other = Path.Combine(KimiCredentialsLock.Root, "0123456789abcdef.survivors.json");
        KimiSurvivorMarker.Write(other, Running(Member(424243)));

        MarkerPath.Should().NotBe(other, "the fixture's own sign-in has its own key");
        PanelService.SurvivorMarkerOf(Runtime()).Should().BeNull("a marker of another sign-in is not this card's to clear");
    }

    [Fact]
    public void AMarkerWhoseShimStillRuns_IsATurnInProgress_NotAStuckMarker()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));

        KimiSurvivorMarker.StandingReportFor(MarkerPath, _ => new Liveness.Alive()).Should().BeEmpty("the shim that wrote it is alive: its marker is removed when its turn ends cleanly");
        KimiSurvivorMarker.StandingReportFor(MarkerPath, _ => new Liveness.Unknown("EACCES")).Should().ContainSingle("a shim that cannot be checked leaves the marker standing");
        KimiSurvivorMarker.StandingReportFor(MarkerPath, _ => new Liveness.Gone("no such process")).Should().ContainSingle("a shim that is gone left it behind");
    }

    [Fact]
    public async Task TheReportedPath_IsOneTheClearModeAccepts_AndClears()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));
        var report = PanelService.SurvivorMarkerOf(Runtime())!;
        var stdout = new StringWriter();
        List<string> notes = [];

        var exit = await KimiClearMarkerMode.RunAsync(
            ["--kimi-clear-marker", report.Path], notes.Add, stdout, KimiCredentialsLock.Root, _ => new Liveness.Gone("no such process"), () => new KimiPresence.NoKimi());

        exit.Should().Be(0, string.Join("\n", notes) + " — exit 65 means the path is one the mode would not accept");
        File.Exists(report.Path).Should().BeFalse();
    }

    [Fact]
    public void AnotherRuntime_HasNoMarkerToReport() => PanelService.SurvivorMarkerOf(new CodexRuntime()).Should().BeNull();

    [Fact]
    public void TheWireShape_IsSurvivorMarker_WithPathAndPids_AndAbsentWithoutOne()
    {
        var with = new ProviderStatus("kimi", true, true, "kimi 2.2.0", "ok", string.Empty) { SurvivorMarker = new SurvivorMarkerReport("/locks/0123456789abcdef.survivors.json", [424243, 424244]) };
        var without = new ProviderStatus("kimi", true, true, "kimi 2.2.0", "ok", string.Empty);

        var json = JsonDocument.Parse(JsonSerializer.Serialize(new ProvidersAnswer([with, without], "never", string.Empty, []), ServerJsonContext.Default.ProvidersAnswer)).RootElement;

        var row = json.GetProperty("providers")[0];
        var marker = row.GetProperty("survivorMarker");
        marker.GetProperty("path").GetString().Should().Be("/locks/0123456789abcdef.survivors.json");
        marker.GetProperty("pids").EnumerateArray().Select(pid => pid.GetInt32()).Should().Equal(424243, 424244);
        marker.EnumerateObject().Select(property => property.Name).Should().Equal("path", "pids");
        json.GetProperty("providers")[1].TryGetProperty("survivorMarker", out _).Should().BeFalse("absent when no marker stands, so an older extension's JSON reads as it did");
    }
}

using System.Text.Json;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-consultant</c> for a Kimi row (E5.5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.5.1–S5.5.4): one turn as a consultation makes it — in a
/// COPY of the check's scratch repository, on a ONE-SHOT owned home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;\kh-&lt;10 hex&gt;</c>), disposed junction-first whatever ended
/// the check. The whole mode in process (<see cref="ConsultantCheckMode.AnswerAsync"/>), git for real, the shim played (<see cref="PlayedKimi"/>), the
/// person a fake one (<see cref="KimiFixture"/>) — no turn is paid for and the real <c>kimi</c> is never started.
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiConsultantCheckTests : IDisposable
{
    private readonly TempDir _data = TempDir.For("testsToDelete-kcheck-data-");
    private readonly TempDir _temp = TempDir.For("testsToDelete-kcheck-temp-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kcheck-homes-");
    private readonly List<string> _warned = [];

    public void Dispose()
    {
        _data.Dispose();
        _temp.Dispose();
        _homes.Dispose();
    }

    /// <summary>The settings of a check of a Kimi consultant row <c>my-kimi</c> for the caller <c>claude</c>, with <paramref name="homesRoot"/> as the homes root (empty: a hand-built one).</summary>
    private PanelSettings KimiSettings(string homesRoot)
    {
        // QWEN-FILL-BEGIN s55-settings test
        // See regions/s55-settings.md: a PanelSettings with Providers = [], DataDir = _data, ReviewerTimeout 60 s, ConsultEnabled = true,
        // ConsultantHomesRoot = homesRoot and Consultants = { [CallerIdentity.Claude] = new ConsultantChoice("my-kimi", Runtime: "kimi") }.
        throw new NotImplementedException("s55-settings");
        // QWEN-FILL-END s55-settings
    }

    /// <summary>One <c>--check-consultant --caller claude</c> in process: the exit code and the JSON it said.</summary>
    private async Task<(int Code, JsonElement Said)> CheckAsync(PanelSettings settings, PlayedKimi played)
    {
        var (code, stdout, _) = await ConsultantCheckMode.AnswerAsync(
            settings, ["--check-consultant", "--caller", "claude"], played, _temp, _warned.Add, Noticing.None, TestContext.Current.CancellationToken);

        return (code, JsonDocument.Parse(stdout).RootElement.Clone());
    }

    [Fact]
    public async Task AKimiCheck_RunsInACopy_OnAOneShotHome()
    {
        // QWEN-FILL-BEGIN s551-copy test
        // See regions/s551-copy.md.
        throw new NotImplementedException("s551-copy");
        // QWEN-FILL-END s551-copy
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AfterTheCheck_TheHomeIsGone_JunctionFirst(bool theTurnFails)
    {
        // QWEN-FILL-BEGIN s552-gone test
        // See regions/s552-gone.md.
        throw new NotImplementedException($"s552-gone {theTurnFails}");
        // QWEN-FILL-END s552-gone
    }

    [Fact]
    public async Task ACheckCancelledMidTurn_StillDisposesItsHome()
    {
        // QWEN-FILL-BEGIN s553-cancel test
        // See regions/s553-cancel.md.
        throw new NotImplementedException("s553-cancel");
        // QWEN-FILL-END s553-cancel
    }

    [Fact]
    public async Task ARootlessCheck_KeepsTheHomeInItsScratch_AndDisposesItJunctionFirst()
    {
        // QWEN-FILL-BEGIN s554-rootless test
        // See regions/s554-rootless.md.
        throw new NotImplementedException("s554-rootless");
        // QWEN-FILL-END s554-rootless
    }
}

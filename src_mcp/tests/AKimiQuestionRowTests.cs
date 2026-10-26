using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots — the
/// prompt names the copies, never the originals — and its one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) and its copies are gone junction-first once the row
/// ends: on an answer, on a refusal before the launch and on a cancellation alike. The whole <c>ask_consultants</c> flow through the service, git for real,
/// the shim played (<see cref="PlayedKimi"/>) over a fake person (<see cref="KimiFixture"/>); no turn is paid for.
/// </summary>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        // QWEN-FILL-BEGIN s567-init test
        // See regions/s567-init.md: make the git repository at Repo with src/Mailer.cs, docs/notes.md, src/.kimi-code/config.toml and src/AGENTS.md.
        await Task.CompletedTask;
        throw new NotImplementedException("s567-init");
        // QWEN-FILL-END s567-init
    }

    public ValueTask DisposeAsync()
    {
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>Makes a git repository at <paramref name="at"/> holding the given files, one commit on <c>main</c>.</summary>
    private async Task RepositoryAsync(string at, params (string Path, string Text)[] files)
    {
        // QWEN-FILL-BEGIN s567-repo test
        // See regions/s567-repo.md: the Vibe test's RepositoryAsync, copied.
        await Task.CompletedTask;
        throw new NotImplementedException("s567-repo");
        // QWEN-FILL-END s567-repo
    }

    /// <summary>The service for one Kimi <c>disk</c> row <c>kimi-disk</c> granted <paramref name="roots"/>, with <paramref name="homesRoot"/> as the homes root.</summary>
    private PanelService ServiceOn(PlayedKimi kimi, string homesRoot, params string[] roots)
    {
        // QWEN-FILL-BEGIN s567-service test
        // See regions/s567-service.md: the Vibe test's ServiceFor, with the row's Runtime "kimi" and its ExecutablePath empty.
        throw new NotImplementedException("s567-service");
        // QWEN-FILL-END s567-service
    }

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    /// <summary>The single row's JSON of a reply that has one.</summary>
    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>What S5.6.7's three endings leave behind: nothing under the homes root's <c>coai-vh</c>, and the fake person's folder whole.</summary>
    private void NothingIsLeftBehind(KimiFixture person, ProcessRequest? shim)
    {
        // QWEN-FILL-BEGIN s567-clean test
        // See regions/s567-clean.md.
        throw new NotImplementedException("s567-clean");
        // QWEN-FILL-END s567-clean
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("refused-before-the-launch")]
    [InlineData("cancelled-mid-turn")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        // QWEN-FILL-BEGIN s567-row test
        // See regions/s567-row.md.
        await Task.CompletedTask;
        throw new NotImplementedException($"s567-row {ending}");
        // QWEN-FILL-END s567-row
    }
}

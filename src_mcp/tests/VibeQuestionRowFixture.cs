using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the Vibe question-row scenario classes share: a real Git repository made in a test's folder, the service of one
/// <c>vibe-disk</c> row with the vibe CLI played in process, the ask, and the row of its reply — lifted out of
/// <see cref="AVibeQuestionRowReadsACopyOfItsRootsTests"/> rather than written twice (reuse-first, the second move).
/// </summary>
/// <param name="dir">The test's own folder: the service keeps its data under <c>data</c> in it.</param>
internal sealed class VibeQuestionRowFixture(TempDir dir, ProcessLauncher launcher)
{
    /// <summary>A repository holding <paramref name="files"/>, committed.</summary>
    public async Task RepositoryAsync(string at, params (string Path, string Text)[] files)
    {
        Directory.CreateDirectory(at);
        foreach (var (path, text) in files)
        {
            var full = Path.Combine([at, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, text, TestContext.Current.CancellationToken);
        }

        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    /// <summary>A folder whose <c>.git</c> is a FILE pointing nowhere: a candidate git does not confirm as a work tree.</summary>
    public static string BrokenCheckout(string at)
    {
        Directory.CreateDirectory(at);
        File.WriteAllText(Path.Combine(at, ".git"), "gitdir: /nowhere\n");
        File.WriteAllText(Path.Combine(at, "lost.txt"), "unreachable");

        return at;
    }

    public PanelService ServiceFor(PlayedVibe vibe, string homesRoot, string model, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = homesRoot,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("vibe-disk", "mistral", "vibe", model, string.Empty, PlayedVibe.Exe, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        vibe,
        Logger.None,
        Noticing.None);

    /// <summary>The ask, made from <paramref name="repo"/>; the reply's JSON.</summary>
    public static async Task<JsonElement> AskAsync(PanelService service, string repo, CancellationToken ct) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;

    /// <summary>The one row of a reply.</summary>
    public static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }
}

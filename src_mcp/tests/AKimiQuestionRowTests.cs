using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row (S5.6.7 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): a <c>disk</c> row runs in a COPY of its granted root,
/// the prompt names the copy and never the root, and the row's one-shot home and scratch are gone once the row answers. The whole
/// <c>ask_consultants</c> flow through the service, git for real, the shim played in process (<see cref="PlayedKimi"/>) over a fake person
/// (<see cref="KimiFixture"/>).
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _git = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Repo, "src"));
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "Mailer.cs"), "void Send() { }\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repo, "docs.md"), "not granted\n", TestContext.Current.CancellationToken);

        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _git.RunAsync(new ProcessRequest("git", args, Repo), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    public ValueTask DisposeAsync()
    {
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    private PanelService Service(PlayedKimi played, string homes, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, PlayedKimi.Shim, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        played,
        Logger.None,
        Noticing.None);

    /// <summary>The seams a Kimi launch needs for a fake person on the measured host (the same stand-in the consult scenarios use).</summary>
    private static IDisposable[] StandIn(KimiFixture person) =>
    [
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))),
    ];

    /// <summary>S5.6.7 (the answered arm): the row's prompt names its copy, never its root, and nothing of the row is left behind, junction first.</summary>
    [Fact]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind()
    {
        using var person = new KimiFixture();
        var stood = StandIn(person);
        var recorded = new List<string>();
        try
        {
            var granted = Path.Combine(Repo, "src");
            var played = new PlayedKimi(_git);
            var before = person.Hash();
            GitScratch.AfterUnlink = recorded.Add;
            var reply = JsonDocument.Parse(await Service(played, Homes, granted).AskConsultantsAsync(
                Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: TestContext.Current.CancellationToken)).RootElement;

            reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());
            var row = answers.EnumerateArray().Should().ContainSingle().Subject;
            row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());

            var shim = played.Shims.Should().ContainSingle().Subject;
            shim.Request.Arguments.Should().Contain("--cwd");
            var cwd = shim.Request.Arguments[Array.IndexOf(shim.Request.Arguments.ToArray(), "--cwd") + 1];
            cwd.Should().NotBe(granted, "a disk row never runs in the root itself");
            cwd.Should().NotStartWith(Repo, "the copy is outside the checkout");
            shim.Request.StdIn.Should().Contain(cwd, "the prompt names the copy the shim can read");
            shim.Request.StdIn.Should().NotContain(granted, "never the original root");

            Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's one-shot home goes when it answers");
            Directory.Exists(cwd).Should().BeFalse("the copy goes with the row");
            recorded.Should().BeEmpty("the one-shot home's junction is unlinked by its own name, never entered by the deleter");
            person.Hash().Should().Equal(before, "the fake person's folder is intact: bytes and attributes");
        }
        finally
        {
            GitScratch.AfterUnlink = null;
            foreach (var scope in stood.Reverse())
            {
                scope.Dispose();
            }
        }
    }
}

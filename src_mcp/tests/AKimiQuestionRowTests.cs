using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
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
/// S5.6.7 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>: a Kimi question row on a <c>disk</c> prompt, through the whole
/// <c>ask_consultants</c> flow, is told the COPIES of its granted roots and never the roots; however the row ends — answered,
/// refused before the launch, cancelled mid-turn — its copy and its one-shot <c>coai-vh/&lt;8&gt;</c> folder are gone, the deleter's
/// unlink recorder is never invoked (the home is disposed junction-first), and the fake person's sign-in folder is whole.
/// </summary>
/// <remarks>
/// Only the shim is played (<see cref="PlayedKimi"/>): git runs for real, the launch control and the sign-in lock are the real ones
/// over a fake person (<see cref="KimiFixture"/>). No turn is paid for and the real <c>kimi</c> is never started.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    private string OneShotRoot => Path.Combine(Homes, VibeHomes.OneShotFolder);

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _person.Dispose();
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    private async Task RepositoryAsync(string at, params (string Path, string Text)[] files)
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
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — the stand-in of <c>AKimiConsultationScenarioTests</c>.</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>Several scopes, given back in reverse.</summary>
    private sealed class Scopes(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var scope in scopes.Reverse())
            {
                scope.Dispose();
            }
        }
    }

    private PanelService Service(PlayedKimi played, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = Homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"), default, played, Logger.None, Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>The three ways a row ends, each of which must leave nothing behind.</summary>
    public enum Ending { Answered, RefusedBeforeTheLaunch, CancelledMidTurn }

    /// <summary>S5.6.7: each ending reads the copies, names them in the prompt, and leaves no copy, no one-shot folder and no unlink of the person's sign-in.</summary>
    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        using var standIn = StandIn();
        if (ending == Ending.RefusedBeforeTheLaunch)
        {
            // The launch control refuses a person whose credentials folder is not there (KimiLaunch.Rendered); the removal is made
            // before the snapshot, because it is the person's own folder that changes here, not the row's.
            foreach (var file in Directory.EnumerateFiles(_person.Credentials))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_person.Credentials, recursive: true);
        }

        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var granted = Path.Combine(Repo, "src");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher)
        {
            Turn = static (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 1200, 40),
            During = ending == Ending.CancelledMidTurn
                ? async (_, ct) =>
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
                : static (_, _) => Task.CompletedTask,
        };

        if (ending == Ending.CancelledMidTurn)
        {
            var asking = async () => await AskAsync(Service(played, granted), caller.Token);

            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await AskAsync(Service(played, granted));

            TheRow(reply).GetProperty("status").GetString().Should().Be(
                ending == Ending.Answered ? RowOutcomes.Answered : RowOutcomes.Failed, reply.ToString());
        }

        if (ending == Ending.RefusedBeforeTheLaunch)
        {
            played.Shims.Should().BeEmpty("the launch control refused before any shim started");
        }
        else
        {
            var request = played.Shims.Should().ContainSingle().Subject.Request;
            request.StdIn.Should().Contain(request.WorkingDirectory, "the prompt names the copies, under the row's tree");
            request.StdIn.Should().NotContain(granted, "never the root (D6)");
            request.Arguments.Should().NotContain(argument => argument.Contains(Repo), "neither root nor checkout on the argv");
            Directory.Exists(request.WorkingDirectory).Should().BeFalse("the row's copy is gone");
            var home = request.Arguments[request.Arguments.ToList().IndexOf(AcpWords.HomeFlag) + 1];
            Path.GetDirectoryName(Path.GetDirectoryName(home)!).Should().Be(OneShotRoot, "the row's home under the homes root's coai-vh (deviation 3)");
        }

        (!Directory.Exists(OneShotRoot) || !Directory.EnumerateFileSystemEntries(OneShotRoot).Any()).Should().BeTrue("coai-vh/<8> is gone");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }
}

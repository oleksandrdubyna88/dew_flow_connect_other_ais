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
/// A Kimi question row through the whole <c>ask_consultants</c> flow (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): the real
/// fan-out and row launch, the real copies (git runs for real), the real launch control over a fake person (<see cref="KimiFixture"/>) — only the
/// shim is played (<see cref="PlayedKimi"/>). Whatever ends the row, its copy and its one-shot home are gone, junction-first (E5.3 decision g).
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    private string OneShotRoot => Path.Combine(Homes, VibeHomes.OneShotFolder);

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"), ("docs/notes.md", "not granted\n"));

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _person.Dispose();
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

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

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see (the AKimiConsultationScenarioTests.StandIn shape).</summary>
    private Scopes StandIn() => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    private PanelService Service(PlayedKimi played, string model, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = Homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", model, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        played,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    /// <summary>S5.6.7: a disk row reads the copies of its root, and leaves nothing behind whatever ends it — answered, refused, cancelled.</summary>
    [Theory]
    [InlineData("answered")]
    [InlineData("refused before the launch")]
    [InlineData("cancelled mid-turn")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var stood = StandIn();
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var granted = Path.Combine(Repo, "src");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var model = ending == "refused before the launch" ? "not-a-kimi-model" : KimiModels.Default;
        var played = ending switch
        {
            "cancelled mid-turn" => new PlayedKimi(_launcher)
            {
                During = async (_, ct) =>
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                },
            },
            _ => new PlayedKimi(_launcher) { Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 1200, 40, Advice) },
        };
        var service = Service(played, model, granted);

        JsonElement reply = default;
        if (ending == "cancelled mid-turn")
        {
            var asking = async () => await AskAsync(service, caller.Token);

            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            reply = await AskAsync(service);
        }

        switch (ending)
        {
            case "answered":
            {
                TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
                var shim = played.Shims.Should().ContainSingle().Subject.Request;
                var cwd = After(shim.Arguments, "--cwd");
                shim.StdIn.Should().Contain(cwd, "the prompt names the copies' paths — what the row can read");
                shim.StdIn.Should().NotContain(granted, "never the root itself (D6)");
                Directory.Exists(cwd).Should().BeFalse("the copy goes when the row ends");
                Directory.Exists(Path.GetDirectoryName(After(shim.Arguments, AcpWords.HomeFlag))).Should().BeFalse("the row's one-shot folder goes with it");
                break;
            }

            case "refused before the launch":
                TheRow(reply).GetProperty("status").GetString().Should().NotBe(RowOutcomes.Answered, reply.ToString());
                played.Shims.Should().BeEmpty("a model the launch control refuses never starts the shim");
                break;

            case "cancelled mid-turn":
            {
                var shim = played.Shims.Should().ContainSingle().Subject.Request;
                Directory.Exists(After(shim.Arguments, "--cwd")).Should().BeFalse();
                Directory.Exists(Path.GetDirectoryName(After(shim.Arguments, AcpWords.HomeFlag))).Should().BeFalse("finally, whatever ended it");
                break;
            }
        }

        (Directory.Exists(OneShotRoot) ? Directory.EnumerateFileSystemEntries(OneShotRoot) : []).Should().BeEmpty("coai-vh/<8> is gone after every ending");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }
}

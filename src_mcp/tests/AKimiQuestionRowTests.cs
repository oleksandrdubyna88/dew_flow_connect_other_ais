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
/// A Kimi question-consultant row (S5.6.7 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): a <c>disk</c> row reads its operator's
/// GRANTED ROOTS — each copied, limited to that root, into a sub-folder of the row's own snapshot — and is told the COPIES' paths,
/// never the originals; a root outside any Git work tree is refused before a launch; and the row's one-shot folder, its copies and
/// the fake person's sign-in are left whole once the row ends — on an answer, a refusal and a cancellation alike.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the Kimi shim played in process (<see cref="PlayedKimi"/>),
/// over a fake person (<see cref="KimiFixture"/>). The deleter's unlink recorder proves the one-shot home was disposed junction-first.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    public AKimiQuestionRowTests() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    private string OneShotRoot => Path.Combine(Homes, VibeHomes.OneShotFolder);

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"),
            ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
    }

    public ValueTask DisposeAsync()
    {
        _dir.Dispose();
        _person.Dispose();
        GitScratch.AfterUnlink = null;

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

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call.</summary>
    private Scopes StandIn() => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    private static void TheRowWasRefusedFor(JsonElement reply, string because)
    {
        var row = TheRow(reply);
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
        row.GetProperty("reason").GetString().Should().Contain(because, reply.ToString());
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("refused before the launch")]
    [InlineData("cancelled mid-turn")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var stood = StandIn();
        var before = _person.Hash();
        var root = Path.Combine(Repo, "src");
        PlayedKimi played;
        JsonElement reply = default;

        switch (ending)
        {
            case "answered":
                played = new PlayedKimi(_launcher);
                reply = await AskAsync(Service(played, root));
                TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
                played.Shims.Should().ContainSingle();
                break;

            case "refused before the launch":
                var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
                await File.WriteAllTextAsync(Path.Combine(plain, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
                played = new PlayedKimi(_launcher);
                reply = await AskAsync(Service(played, plain));
                TheRowWasRefusedFor(reply, "not inside a Git work tree");
                played.Shims.Should().BeEmpty("nothing of Kimi's starts for a row whose copy was refused");
                break;

            default:
                using (var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
                {
                    played = new PlayedKimi(_launcher)
                    {
                        During = async (_, ct) =>
                        {
                            await caller.CancelAsync();
                            ct.ThrowIfCancellationRequested();
                        },
                    };
                    var asking = async () => await AskAsync(Service(played, root), caller.Token);

                    await asking.Should().ThrowAsync<OperationCanceledException>();
                }

                break;
        }

        if (ending != "refused before the launch")
        {
            var turn = played.Shims.Should().ContainSingle().Subject.Request;
            var cwd = turn.Arguments[turn.Arguments.ToList().IndexOf("--cwd") + 1];
            var oneShot = Path.GetDirectoryName(turn.Arguments[turn.Arguments.ToList().IndexOf("--home") + 1])!;

            if (ending == "answered")
            {
                turn.StdIn.Should().Contain(cwd, "the prompt names the copy Kimi can read");
                turn.StdIn.Should().NotContain(root, "never the original, which it cannot read");
            }
            else
            {
                turn.Arguments.Should().NotContain(root, "never the original, which it cannot read");
            }

            Directory.Exists(oneShot).Should().BeFalse("the row's one-shot folder goes when the row ends");
            Directory.Exists(cwd).Should().BeFalse("the copies go with the row's scratch");
        }

        (Directory.Exists(OneShotRoot) ? Directory.EnumerateFileSystemEntries(OneShotRoot) : []).Should()
            .BeEmpty("the row's folder under coai-vh is deleted on every way out");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }
}

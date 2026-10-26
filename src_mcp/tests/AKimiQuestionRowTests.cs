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
/// A Kimi question-consultant row (S5.6.7 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): a <c>disk</c> row reads COPIES of its
/// granted roots and leaves nothing behind. Its prompt names the copies' paths and never the root; after an answer, a refusal before
/// the launch and a cancellation mid-turn, the row's scratch and its one-shot folder under <c>coai-vh</c> are gone, the deleter's unlink
/// recorder was never invoked (the home was disposed junction-first), and the fake person's folder is whole.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the Kimi shim played in process (<see cref="PlayedKimi"/>)
/// over a fake person (<see cref="KimiFixture"/>). Kimi scenarios run on every OS through <see cref="WindowsStandIn"/>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");

    private string Repo => _dir.At("repo");

    private string Other => _dir.At("other");

    /// <summary>The homes root, inside this test's own folder — removed with it.</summary>
    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    private string OneShotRoot => Path.Combine(Homes, VibeHomes.OneShotFolder);

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
    }

    public ValueTask DisposeAsync()
    {
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

    /// <summary>
    /// The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, so the values reach the test's own flow
    /// and every service call it awaits.
    /// </summary>
    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private PanelService ServiceFor(PlayedKimi played, params string[] roots) => new(
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

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>A plain folder, not a Git work tree: the granted root that the row must refuse.</summary>
    private string PlainFolder()
    {
        var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
        File.WriteAllText(Path.Combine(plain, "notes.txt"), "not a repository");

        return plain;
    }

    private string GrantedFor(string ending) => ending == "refused" ? PlainFolder() : Path.Combine(Repo, "src");

    /// <summary>The cancelled arm cancels the caller while the shim is alive; the other arms let the turn run to its answer.</summary>
    private static Func<ProcessRequest, CancellationToken, Task> MidTurnFor(string ending, CancellationTokenSource caller) =>
        ending == "cancelled" ? CancelMidTurn(caller) : NothingMidTurn;

    private static readonly Func<ProcessRequest, CancellationToken, Task> NothingMidTurn = static (_, _) => Task.CompletedTask;

    private static Func<ProcessRequest, CancellationToken, Task> CancelMidTurn(CancellationTokenSource caller) => async (_, ct) =>
    {
        await caller.CancelAsync();
        ct.ThrowIfCancellationRequested();
    };

    private void TheOneShotFolderIsEmpty()
    {
        if (Directory.Exists(OneShotRoot))
        {
            Directory.EnumerateFileSystemEntries(OneShotRoot).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        }
    }

    /// <summary>The row's scratch folder: the <c>coai-question-*</c> ancestor of the folder the launch ran in.</summary>
    private static void TheScratchIsGone(ProcessRequest request)
    {
        var scratch = AncestorNamed(request.WorkingDirectory, "coai-question-");
        scratch.Should().NotBeEmpty("the launch ran inside the row's scratch folder");
        Directory.Exists(scratch).Should().BeFalse("the row's scratch folder is gone once the row ended");
    }

    private static string AncestorNamed(string path, string prefix)
    {
        var at = path;
        while (at is not null && !Path.GetFileName(at).StartsWith(prefix, StringComparison.Ordinal))
        {
            at = Path.GetDirectoryName(at);
        }

        return at ?? string.Empty;
    }

    private void TheRowAnswered(PlayedKimi played, JsonElement reply)
    {
        TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
        var request = played.Shims.Should().ContainSingle().Subject.Request;
        request.StdIn.Should().Contain(request.WorkingDirectory, "the prompt names the copy's folder");
        request.StdIn.Should().NotContain(Path.Combine(Repo, "src"), "never the original root");
        Path.GetRelativePath(Repo, request.WorkingDirectory).Should().StartWith("..", "the launch's folder is not under the repository");
    }

    private static void TheRowRefused(PlayedKimi played, JsonElement reply)
    {
        var row = TheRow(reply);
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
        row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree");
        played.Shims.Should().BeEmpty("nothing of Kimi's starts for a row whose copy was refused");
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var before = person.Hash();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher) { During = MidTurnFor(ending, caller) };
        var unlinked = new List<string>();
        GitScratch.AfterUnlink = unlinked.Add;
        try
        {
            var service = ServiceFor(played, GrantedFor(ending));
            var asking = async () => await AskAsync(service, caller.Token);
            switch (ending)
            {
                case "answered":
                    TheRowAnswered(played, await asking());
                    break;
                case "refused":
                    TheRowRefused(played, await asking());
                    break;
                default:
                    await asking.Should().ThrowAsync<OperationCanceledException>();
                    break;
            }
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        TheOneShotFolderIsEmpty();
        if (ending != "refused")
        {
            TheScratchIsGone(played.Shims.Should().ContainSingle().Subject.Request);
        }

        unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked: KimiHome unlinks the junction first");
        person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }
}

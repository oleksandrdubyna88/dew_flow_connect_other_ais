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
/// A Kimi question-consultant <c>disk</c> row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7) reads COPIES of its granted roots: the
/// prompt names the copies' paths and never the root, and after every ending — answered, refused before the launch, cancelled mid-turn — the row's
/// scratch and <c>coai-vh/&lt;8&gt;</c> are gone, the deleter's unlink recorder was never invoked and the fake person's folder is whole.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played in process (<see cref="PlayedKimi"/>) over a fake person
/// (<see cref="KimiFixture"/>); <c>kimi</c> is never started.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"),
            ("src/.kimi-code/hooks.toml", "[[hooks]]\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
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

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    private static string After(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    /// <summary>Whatever the row's ending, nothing of its one-shot folder is left under <c>coai-vh</c>.</summary>
    private void TheHomesFolderIsEmpty()
    {
        var folder = Path.Combine(Homes, VibeHomes.OneShotFolder);
        if (Directory.Exists(folder))
        {
            Directory.EnumerateFileSystemEntries(folder).Should().BeEmpty("the row's one-shot folder goes when the row ends, whatever ended it");
        }
    }

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

    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>A plain folder, outside any Git work tree: a row granted it is refused before anything launches.</summary>
    private string PlainFolder()
    {
        var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
        File.WriteAllText(Path.Combine(plain, "plain.txt"), "x");

        return plain;
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var before = person.Hash();
        var unlinked = new List<string>();
        var copy = string.Empty;
        var scratch = string.Empty;
        var played = new PlayedKimi(_launcher)
        {
            Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 10, 5, PlayedKimi.Advice),
            During = async (request, ct) =>
            {
                // The copy exists only while the shim is alive: name it now.
                var cwd = After(request.Arguments, "--cwd");
                scratch = Path.GetDirectoryName(cwd) ?? string.Empty;
                var file = Directory.EnumerateFiles(cwd, "Mailer.cs", SearchOption.AllDirectories).FirstOrDefault();
                copy = file is null ? string.Empty : Path.Combine(cwd, Path.GetRelativePath(cwd, file).Split(Path.DirectorySeparatorChar)[0]);
                if (ending == "cancelled")
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };
        var service = ServiceFor(played, ending == "refused" ? PlainFolder() : Path.Combine(Repo, "src"));
        GitScratch.AfterUnlink = unlinked.Add;
        try
        {
            if (ending == "cancelled")
            {
                var asking = async () => await AskAsync(service, caller.Token);

                await asking.Should().ThrowAsync<OperationCanceledException>();
            }
            else
            {
                var reply = await AskAsync(service);

                TheRow(reply).GetProperty("status").GetString().Should().Be(ending == "answered" ? RowOutcomes.Answered : RowOutcomes.Refused, reply.ToString());
            }
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        TheShimWas(ending, played, copy);
        TheRowsScratchIsGone(ending, scratch);
        TheHomesFolderIsEmpty();
        unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        person.Hash().Should().Equal(before, "the person's folder is whole");
    }

    /// <summary>The copies are made in the row's own <c>coai-question-*</c> scratch, and the scratch goes with the row, whatever ended it.</summary>
    private static void TheRowsScratchIsGone(string ending, string scratch)
    {
        if (ending == "refused")
        {
            return;
        }

        Path.GetFileName(scratch).Should().StartWith("coai-question-", "the shim stood in a folder of the row's own scratch");
        Directory.Exists(scratch).Should().BeFalse("the row's scratch, and the copies in it, go when the row ends, whatever ended it");
    }

    private void TheShimWas(string ending, PlayedKimi played, string copy)
    {
        if (ending == "refused")
        {
            played.Shims.Should().BeEmpty("a refused row launches nothing");
        }

        if (ending == "answered")
        {
            var request = played.Shims.Should().ContainSingle().Subject.Request;
            copy.Should().NotBeEmpty("the turn found the copy of the granted root");
            request.StdIn.Should().Contain(copy, "the prompt names the copy Kimi can read");
            request.StdIn.Should().NotContain(Path.Combine(Repo, "src"), "never the root itself");
        }
    }
}

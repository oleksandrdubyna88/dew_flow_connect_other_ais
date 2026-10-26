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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots —
/// the prompt names the copies' paths, never the roots — and the row's one-shot home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c> and its scratch are
/// gone junction-first on an answer, a refusal before the launch and a cancellation alike (E5.3 decision g; nothing new in <c>QuestionRowLaunch</c>).
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the real launch control over a fake person's sign-in
/// (<see cref="KimiFixture"/>) — only the shim is played (<see cref="PlayedKimi"/>), so no turn is paid for and no <c>kimi</c> starts. The person's
/// folder lives outside the homes root the row deletes.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"),
            ("src/.kimi-code/config.toml", "[x]\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

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

    /// <summary>The person, the host and the lock root this flow's Kimi adapter sees — set by SYNCHRONOUS calls (<see cref="KimiSeams.Using"/>).</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private PanelService Service(PlayedKimi kimi, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = Homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", KimiModels.Default, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        kimi,
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

    /// <summary>The row's own scratch — the <c>coai-question-*</c> ancestor of the copy its shim stood in.</summary>
    private static string ScratchOf(ProcessRequest request)
    {
        var at = request.WorkingDirectory;
        while (!Path.GetFileName(at).StartsWith(QuestionRowLaunch.ScratchPrefix, StringComparison.Ordinal))
        {
            at = Path.GetDirectoryName(at) ?? throw new InvalidOperationException($"{request.WorkingDirectory} is not inside a question row's scratch");
        }

        return at;
    }

    private void TheRowLeftNothingBehind(IReadOnlyList<string> personsFolder)
    {
        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(personsFolder, "the person's folder: the same names, bytes and read-only bits as before");
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData("answered")]
    [InlineData("refused before the launch")]
    [InlineData("cancelled mid-turn")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var standIn = StandIn();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
        await File.WriteAllTextAsync(Path.Combine(plain, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher)
        {
            During = async (_, ct) =>
            {
                if (ending == "cancelled mid-turn")
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };
        var granted = Path.Combine(Repo, "src");
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

        var asking = async () => await AskAsync(Service(played, ending == "refused before the launch" ? plain : granted), caller.Token);

        if (ending == "cancelled mid-turn")
        {
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var row = TheRow(await asking());
            row.GetProperty("status").GetString().Should().Be(ending == "answered" ? RowOutcomes.Answered : RowOutcomes.Refused, row.ToString());
        }

        TheShimsStoodInTheCopies(ending, played, granted);
        TheRowLeftNothingBehind(before);
    }

    private static void TheShimsStoodInTheCopies(string ending, PlayedKimi played, string granted)
    {
        if (ending == "refused before the launch")
        {
            played.Shims.Should().BeEmpty("a root outside any Git work tree is refused at its copy, before a launch");
            return;
        }

        var request = played.Shims.Should().ContainSingle().Subject.Request;
        request.StdIn.Should().Contain(request.WorkingDirectory, "the prompt names the copy Kimi stands in");
        request.StdIn.Should().NotContain(granted, "never the original: a branch's AGENTS.md, skills and .mcp.json load from the disk (M-K9)");
        Directory.Exists(ScratchOf(request)).Should().BeFalse("the row's scratch, and its copies with it, are gone once it ends");
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
}

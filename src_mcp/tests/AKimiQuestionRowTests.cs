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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads the COPIES of its operator's
/// granted roots and is told the copies' paths, never the originals; and the row's scratch and its one-shot home <c>coai-vh\&lt;8&gt;</c> are gone,
/// junction-first, once the row ends — on an answer, a refusal before the launch and a cancellation alike.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played (<see cref="PlayedKimi"/>) over a fake person
/// (<see cref="KimiFixture"/>) — shaped like <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>. The real <c>kimi</c> is never started.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => _dir.At("homes");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Homes);
        Directory.CreateDirectory(Path.Combine(Repo, "src"));
        Directory.CreateDirectory(Path.Combine(Repo, "docs"));
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "Mailer.cs"), "void Send() { }\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repo, "docs", "notes.md"), "not granted\n", TestContext.Current.CancellationToken);
        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, Repo), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>The service with one enabled Kimi <c>disk</c> row, the homes root <see cref="Homes"/> and the granted <paramref name="roots"/>.</summary>
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
                Rows = [new QuestionRow("kimi-disk", "kimi", "kimi", string.Empty, string.Empty, PlayedKimi.Shim, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        played,
        Logger.None,
        Noticing.None);

    /// <summary>The fake person, the measured host and a lock root, stood in for THIS async flow (set synchronously, so the service's calls see them).</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>
    /// S5.6.7: a <c>disk</c> row on a git root reads its copy — the prompt names the copy's path and never the root — and after each ending the
    /// row's scratch and <c>coai-vh\&lt;8&gt;</c> are gone, the deleter's unlink recorder never invoked, the fake person's folder intact. A refused
    /// row is a root outside any Git work tree, refused before a launch.
    /// </summary>
    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        ending.Should().BeOneOf("answered", "refused", "cancelled");
        using var standIn = StandIn();
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher)
        {
            Turn = (_, _) => PlayedKimi.Answered(PlayedKimi.Minted, 100, 10, Advice),
            During = async (_, ct) =>
            {
                if (ending == "cancelled")
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };
        var granted = ending == "refused" ? Directory.CreateDirectory(_dir.At("plain")).FullName : Path.Combine(Repo, "src");

        var asking = async () => await AskAsync(Service(played, granted), caller.Token);

        if (ending == "cancelled")
        {
            await asking.Should().ThrowAsync<OperationCanceledException>();
            played.Shims.Should().ContainSingle("the turn started, and was cancelled in flight");
        }
        else
        {
            var row = TheRow(await asking());
            row.GetProperty("status").GetString().Should().Be(ending == "answered" ? RowOutcomes.Answered : RowOutcomes.Refused, row.ToString());
        }

        if (ending == "answered")
        {
            var request = played.Shims.Should().ContainSingle().Subject.Request;
            var cwd = KimiEnvironment.FlagOf(request, "--cwd");
            request.StdIn.Should().Contain(cwd, "the prompt names the copy Kimi can read").And.NotContain(granted, "never the original");
            request.Arguments.Should().NotContain(AcpWords.SessionFlag, "one shot");
            Directory.Exists(cwd).Should().BeFalse("the row's copy goes with its scratch");
        }
        else if (ending == "refused")
        {
            played.Shims.Should().BeEmpty("a root outside any Git work tree is refused before anything is launched");
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's one-shot home goes on every way out");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder is never invoked: the Kimi home goes junction-first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }
}

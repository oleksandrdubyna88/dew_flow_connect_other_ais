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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its roots — never a
/// root — and ends with nothing behind it: its scratch and its one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) are gone junction-first on an answer,
/// on a refusal before the launch and on a cancellation mid-turn (E5.3 decision g).
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played (<see cref="PlayedKimi"/>) over a fake person
/// (<see cref="KimiFixture"/>) — the shape of <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>. No turn is paid for; the real <c>kimi</c> is never started.
/// Every folder is a <c>testsToDelete-</c> folder of the test's own, and the fake person lives OUTSIDE the homes root.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-qrow-homes-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string OneShotRoot => Path.Combine(_homes, VibeHomes.OneShotFolder);

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _homes.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

    private async Task RepositoryAsync(string at, params (string Path, string Text)[] files)
    {
        // QWEN-FILL-BEGIN s567-repo test
        // Same as AVibeQuestionRowReadsACopyOfItsRootsTests.RepositoryAsync: Directory.CreateDirectory(at); write each (path, text) under `at`
        // (path.Split('/') joined with Path.Combine, parent folders created, File.WriteAllTextAsync(full, text, TestContext.Current.CancellationToken));
        // then for each of [["init","-b","main"],["config","user.email","t@example.com"],["config","user.name","t"],["add","."],["commit","-m","base"]]
        // run `_launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken)` and assert result.ExitCode Be 0 with
        // string.Join(' ', args) + ": " + result.StdErr as the message.
        throw new NotImplementedException("s567-repo");
        // QWEN-FILL-END s567-repo
    }

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, as the service scenarios do.</summary>
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

    /// <summary>The service with ONE Kimi disk row, the homes root a test folder, the roots granted.</summary>
    private PanelService Service(PlayedKimi played, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = _homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "moonshot", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
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

    // ---------- S5.6.7 ----------

    /// <summary>The three endings of a row: answered; refused before the launch (a root outside any Git work tree); cancelled mid-turn.</summary>
    public enum Ending
    {
        Answered,
        RefusedBeforeTheLaunch,
        CancelledMidTurn,
    }

    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        _ = ending; // the body below reads it (it is the arm); drop this line once it does
        // QWEN-FILL-BEGIN s567-copies test
        // Arrange: using var stood = StandIn(); GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add; var before = _person.Hash();
        //   the granted root: Path.Combine(Repo, "src") (Answered, CancelledMidTurn) or a plain folder outside any git work tree, _dir.At("plain") created
        //   (RefusedBeforeTheLaunch — the refusal comes before any launch). var played = new PlayedKimi(_launcher)
        //   { Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20000, 30, Advice) }; for CancelledMidTurn:
        //   using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken) and
        //   During = async (_, ct) => { cts.Cancel(); await Task.Delay(Timeout.Infinite, ct); } (the shim blocks until it is cancelled).
        // Act: var reply = await AskAsync(Service(played, root), cts?.Token) — ask_consultants answers a cancelled row as a row status, or throws
        //   OperationCanceledException: if it throws, catch it with Assert.ThrowsAnyAsync<OperationCanceledException> and go on to the asserts.
        // Assert: Answered — the row's status is RowOutcomes.Answered (reply.GetProperty("answers") has one element, its "status"); the ONE shim request's StdIn
        //   (played.Shims.Should().ContainSingle().Subject.Request.StdIn) Contain the copy's path (the folder Path.GetDirectoryName of --cwd ... precisely: the
        //   request's WorkingDirectory + a first segment of the copy) and NotContain the root (Path.Combine(Repo, "src")) — the prompt names the copies' paths and never the root.
        //   RefusedBeforeTheLaunch — played.Shims BeEmpty. For every ending: Directory.Exists(OneShotRoot) is false or has no entries (coai-vh\<8> gone);
        //   the row's scratch is gone (Directory.EnumerateDirectories(Path.GetTempPath(), "coai-question-*") may hold other runs' folders — assert on the
        //   WorkingDirectory/--home paths the played shim saw instead: Directory.Exists(Path.GetDirectoryName(home)) Be false for the Answered and Cancelled arms);
        //   _unlinkedByTheDeleter BeEmpty; _person.Hash() Equal before.
        // Fails before E5.3 g: "Expected Directory.Exists("…\coai-vh\<8>") to be False, but found True." / the recorder red.
        throw new NotImplementedException("s567-copies");
        // QWEN-FILL-END s567-copies
    }
}

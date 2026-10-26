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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots
/// (the prompt names the copies' paths, never the roots), on a ONE-SHOT home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>, and after the row ends — answered,
/// refused before any launch, or cancelled mid-turn — its scratch and its home are gone, junction-first, with the person's folder whole.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played in process (<see cref="PlayedKimi"/>) over a fake person's
/// sign-in (<see cref="KimiFixture"/>, a real junction on Windows, a symbolic link elsewhere through <see cref="WindowsStandIn"/>). No test starts a
/// real <c>kimi</c>. The shape is <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>; the junction-first teeth are <c>AOneShotKimiHomeIsDisposedJunctionFirstTests</c>'s.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => _dir.At("homes");

    /// <summary>How a row's turn ends in S5.6.7's Theory.</summary>
    public enum Ending
    {
        Answered,
        RefusedBeforeTheLaunch,
        CancelledMidTurn,
    }

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Homes);
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"));
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
        // QWEN-FILL-BEGIN s567-repo-helper test
        // Copy `AVibeQuestionRowReadsACopyOfItsRootsTests.RepositoryAsync` (the context pack has it whole): create each file under `at`, then run
        // `git init -b main`, `config user.email`, `config user.name`, `add .`, `commit -m base` with `_launcher`, each exit code 0.
        throw new NotImplementedException("s567-repo-helper");
        // QWEN-FILL-END s567-repo-helper
    }

    /// <summary>The service with the homes root and one <c>disk</c> Kimi row — <see cref="PlayedKimi"/> is its launcher; the fake person is NOT yet standing in (see <see cref="StandIn"/>).</summary>
    private PanelService ServiceOn(PlayedKimi played, params string[] roots)
    {
        // QWEN-FILL-BEGIN s567-service test
        // Copy `AVibeQuestionRowReadsACopyOfItsRootsTests.ServiceFor` (the context pack has it whole), with: `ConsultantHomesRoot = Homes`, `DataDir = _dir.At("data")`,
        //   the row `new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)`, `Roots = roots`,
        //   and `played` as the launcher argument of `new PanelService(settings, VaultKeys.None("no vault in tests"), default, played, Logger.None, Noticing.None)`.
        throw new NotImplementedException("s567-service");
        // QWEN-FILL-END s567-service
    }

    /// <summary>The fake person's sign-in, the measured host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call so it reaches every awaited service call.</summary>
    private IDisposable StandIn()
    {
        // QWEN-FILL-BEGIN s567-standin test
        // The same four stand-ins `AKimiConsultationScenarioTests.StandIn` composes (its private `Scopes` class is not reachable; write a small private nested `Scopes(params IDisposable[])`
        // that disposes in reverse): `WindowsStandIn.Begin()`, `KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"))`,
        // `KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])))`. (No `CallingAs`: a question row needs no caller kind.)
        throw new NotImplementedException("s567-standin");
        // QWEN-FILL-END s567-standin
    }

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        _ = ending; // the parameter is read by the body below; this line only keeps the skeleton building
        // QWEN-FILL-BEGIN s567-disk-row test
        // Arrange: `using var stood = StandIn();`, `GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;`, `var before = _person.Hash();`, `var granted = Path.Combine(Repo, "src");`,
        //   `using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);`.
        //   `var played = new PlayedKimi(_launcher) { Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, Advice) }`; for `Ending.CancelledMidTurn`
        //   its `During` is `async (_, ct) => { await caller.CancelAsync(); ct.ThrowIfCancellationRequested(); }`.
        //   The roots: `[granted]`, except `Ending.RefusedBeforeTheLaunch` where the root is a plain folder outside any git work tree — `Directory.CreateDirectory(_dir.At("plain"))` with a
        //   file in it (the Vibe test `ARowRefusedAtItsCopy_GivesItsOneShotFolderBack` does this) — so the copy is refused and nothing launches.
        // Act: `var reply = await AskAsync(ServiceOn(played, roots), caller.Token)`; for the cancelled ending the call throws `OperationCanceledException`
        //   (`await asking.Should().ThrowAsync<OperationCanceledException>()` with `asking = async () => await AskAsync(...)`).
        // Assert, per ending: Answered → `TheRow(reply).GetProperty("status").GetString()` is `RowOutcomes.Answered`, one shim launch (`played.Shims`), whose stdin/argv prompt names the COPY's path
        //   (`request.StdIn` contains the row's `tree` folder `Path.Combine(<row folder>, "tree")` where the row folder is the parent of `--home`'s value) and NEVER `granted`; Refused → status
        //   `RowOutcomes.Refused` or `RowOutcomes.Failed` (read the real one off the first run and write it into the notes), `played.Shims` empty; Cancelled → the throw above, one launch.
        //   After EACH ending: `Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder))` is empty (the row's `coai-vh\<8>` is gone) — if the folder does not exist
        //   that counts as empty; for Answered/Cancelled the row folder (parent of the shim's `--home` value) does not exist; `_unlinkedByTheDeleter` is empty;
        //   `_person.Hash().Should().Equal(before)`.
        // Fails before E5.3 g: "Expected Directory.Exists(\"…\\coai-vh\\<8>\") to be False, but found True." or the recorder not empty.
        throw new NotImplementedException("s567-disk-row");
        // QWEN-FILL-END s567-disk-row
    }
}

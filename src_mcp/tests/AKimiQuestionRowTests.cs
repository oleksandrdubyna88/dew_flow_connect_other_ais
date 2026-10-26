using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads a COPY of its granted
/// roots, is told the copies' paths and never the originals, and the row's one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) and its scratch are
/// gone once the row ends — on an answer, a refusal before the launch and a cancellation alike — each disposed junction-first.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the Kimi shim played in process (<see cref="PlayedKimi"/>) over a fake
/// person (<see cref="KimiFixture"/>) whose folder lives outside everything the row deletes. The model is
/// <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c> (same harness, the Kimi seams instead of the Vibe path budget).
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => _dir.At("homes");

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"));

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

    private async Task RepositoryAsync(string at, params (string Path, string Text)[] files)
    {
        // QWEN-FILL-BEGIN s567-repo test
        // Make a git repository at `at` holding `files`, as AVibeQuestionRowReadsACopyOfItsRootsTests.RepositoryAsync does (copy it from the context pack):
        // create each file (path split on '/'), then `git init -b main`, `git config user.email t@example.com`, `git config user.name t`, `git add .`, `git commit -m base`,
        // each run with `_launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken)` and its ExitCode asserted 0.
        await Task.CompletedTask;
        throw new NotImplementedException("s567-repo");
        // QWEN-FILL-END s567-repo
    }

    private static IDisposable StandIn(KimiFixture person)
    {
        // QWEN-FILL-BEGIN s567-standin test
        // The person, the host and the lock root this flow's Kimi adapters see — a SYNCHRONOUS call whose result the test holds in a `using`. Copy
        // AKimiConsultationScenarioTests.StandIn without its `CallingAs(...)` member (see the context pack): a Scopes object (nested private sealed class,
        // copy it too) holding WindowsStandIn.Begin(), KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        // KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))).
        throw new NotImplementedException("s567-standin");
        // QWEN-FILL-END s567-standin
    }

    private PanelService Service(PlayedKimi played, string homesRoot, params string[] roots)
    {
        // QWEN-FILL-BEGIN s567-service test
        // The service of AVibeQuestionRowReadsACopyOfItsRootsTests.ServiceFor with Kimi in place of Vibe (the pack has it whole): `new PanelService(new PanelSettings { ConsultantHomesRoot = homesRoot,
        // Providers = [], Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human), DataDir = _dir.At("data"), ReviewerTimeout = 30 s, QuestionConsult = new QuestionConsultSettings { Rows =
        // [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)], Roots = roots, RowBudget = 30 s } },
        // VaultKeys.None("no vault in tests"), default, played, Logger.None, Noticing.None)`. (QuestionRow's positional parameters: see the pack; the executable path is empty — the seams' shim is the executable.)
        throw new NotImplementedException("s567-service");
        // QWEN-FILL-END s567-service
    }

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s567-ask test
        // `JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement`, returned.
        await Task.CompletedTask;
        throw new NotImplementedException("s567-ask");
        // QWEN-FILL-END s567-ask
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        // QWEN-FILL-BEGIN s567 test
        // (`ending` is read by the line below only so the xUnit1026 analyzer lets the skeleton build; the real body reads it.)
        _ = ending;
        // Arrange: `using var stood = StandIn(_person);` `var before = _person.Hash();` `GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;` `Directory.CreateDirectory(Homes);`
        // granted = Path.Combine(Repo, "src") (the "refused" ending grants a plain folder outside any git work tree instead: `_dir.At("plain")`, created, with one file — the row is refused
        // BEFORE the launch). played = new PlayedKimi(_launcher) { Turn = answers with PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, Advice) } — and for "cancelled"
        // `During` blocks on `Task.Delay(Timeout.Infinite, ct)` after setting a TaskCompletionSource `started`, while the test cancels a CancellationTokenSource once `started` completed.
        // Act: `var reply = await AskAsync(Service(played, Homes, granted), cts.Token);` (the token is TestContext.Current.CancellationToken for the first two endings).
        // Assert: the reply's single row (reply.GetProperty("answers").EnumerateArray().Single()) has status RowOutcomes.Answered / the refusal status (a RowOutcomes constant — see the pack) /
        // the cancelled one; for "answered": played.Shims holds ONE launch whose Request.StdIn contains the COPY's path (a folder under the shim's working directory holding "Mailer.cs") and
        // NOT `granted`; whatever the ending, Directory.Exists(Path.Combine(Homes, VibeHomes.OneShotFolder)) is false or holds no entry, the row's scratch is gone (the shim's
        // WorkingDirectory's parent does not exist), `_unlinkedByTheDeleter` is empty, and `_person.Hash().Should().Equal(before)`.
        // Why it fails before E5.3 g: the one-shot home was deleted by the bare deleter (its recorder fires) and `coai-vh\<8>` was left behind.
        await Task.CompletedTask;
        throw new NotImplementedException("s567");
        // QWEN-FILL-END s567
    }
}

using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots —
/// the prompt names the copies' paths, never the roots — and the row's one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) and its scratch are gone,
/// junction-first, once the row ends: on an answer, a refusal before the launch and a cancellation in flight alike.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the real launch control over a fake person (<see cref="KimiFixture"/>, stood in
/// by <see cref="AKimiConsultationScenarioTests.StandIn"/>), only the shim played (<see cref="PlayedKimi"/>). The model is
/// <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    /// <summary>The homes root, inside the scenario's folder: a home-keeping row reserves its one-shot folder there (D1).</summary>
    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>A git repository at <paramref name="at"/> holding the files, committed — as the Vibe row test makes its roots.</summary>
    private async Task RepositoryAsync(string at, params (string Path, string Text)[] files)
    {
        // QWEN-FILL-BEGIN s567-repo test
        // Copy of `AVibeQuestionRowReadsACopyOfItsRootsTests.RepositoryAsync` (see the context pack): create `at`, write each file under it (split the path on '/',
        // create parent directories, `File.WriteAllTextAsync(full, text, TestContext.Current.CancellationToken)`), then run through `_launcher`
        // `new ProcessRequest("git", args, at)` for each of: init -b main; config user.email t@example.com; config user.name t; add .; commit -m base —
        // every result's ExitCode must be 0 (`result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr)`).
        throw new NotImplementedException("s567-repo");
        // QWEN-FILL-END s567-repo
    }

    /// <summary>The service with one Kimi <c>question-disk</c> row over <paramref name="roots"/>, the shim played by <paramref name="kimi"/>, the homes root set.</summary>
    private PanelService Service(PlayedKimi kimi, params string[] roots)
    {
        // QWEN-FILL-BEGIN s567-service test
        // `return new PanelService(new PanelSettings { ConsultantHomesRoot = Homes, Providers = [], Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
        //   DataDir = _dir.At("data"), ReviewerTimeout = TimeSpan.FromSeconds(30),
        //   QuestionConsult = new QuestionConsultSettings { Rows = [new QuestionRow("kimi-disk", "moonshot", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
        //   Roots = roots, RowBudget = TimeSpan.FromSeconds(30) } }, VaultKeys.None("no vault in tests"), default, kimi, Logger.None, Noticing.None);`
        // (the 4th constructor argument is the IProcessLauncher — `kimi`; the shape is `AVibeQuestionRowReadsACopyOfItsRootsTests.ServiceFor`.)
        throw new NotImplementedException("s567-service");
        // QWEN-FILL-END s567-service
    }

    /// <summary>One <c>ask_consultants</c> call over the repository, as the Vibe row test makes it.</summary>
    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null)
    {
        // QWEN-FILL-BEGIN s567-ask test
        // `return JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;`
        throw new NotImplementedException("s567-ask");
        // QWEN-FILL-END s567-ask
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData("answered")]
    [InlineData("refused before the launch")]
    [InlineData("cancelled mid-turn")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        ending.Should().NotBeNullOrEmpty("the arm's name");
        // QWEN-FILL-BEGIN s567-copies test
        // Arrange: `using var stood = AKimiConsultationScenarioTests.StandIn(_person); var before = _person.Hash(); var granted = Path.Combine(Repo, "src");`
        //   `using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);`
        //   "answered": `var kimi = new PlayedKimi(_launcher);` and roots [granted];
        //   "refused before the launch": a root that is a plain folder outside any git work tree (`Directory.CreateDirectory(_dir.At("plain"))` with one file) —
        //     the copy is refused before a launch ("not inside a Git work tree"); `kimi` is a bare `new PlayedKimi(_launcher)`;
        //   "cancelled mid-turn": `new PlayedKimi(_launcher) { During = async (_, ct) => { await caller.CancelAsync(); ct.ThrowIfCancellationRequested(); } }`, roots [granted].
        // Act: `var asking = () => AskAsync(Service(kimi, roots), caller.Token);` — for "cancelled" `await asking.Should().ThrowAsync<OperationCanceledException>()`;
        //   for the other two `var reply = await asking();`.
        // Assert for "answered": the row's "status" (`reply.GetProperty("answers").EnumerateArray().Single()`) is `RowOutcomes.Answered`; the ONE shim launch
        //   (`kimi.Shims.Should().ContainSingle().Subject.Request`) has `StdIn` that contains `Path.Combine(<its --cwd value>, "…")`-style copy path: the prompt names the copy —
        //   `request.StdIn.Should().Contain(<the --cwd VALUE (the value after the "--cwd" argument)>)` and `.NotContain(granted)` (never the original root).
        // Assert for "refused": the row's status is `RowOutcomes.Refused` and its "reason" contains "not inside a Git work tree"; `kimi.Shims` and `kimi.Versions` are empty.
        // Assert for ALL three: `Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder))` is empty (coai-vh\<8> gone) — guard with
        //   `Directory.Exists` first (a refused row may never have created `coai-vh`); no folder `coai-question-*` remains under `Path.GetTempPath()` that this row made —
        //   instead assert on the shim's launch: when a shim ran, the value of its "--home" argument's grandparent no longer exists (`Directory.Exists(...)` false);
        //   `_unlinkedByTheDeleter` is empty; `_person.Hash().Should().Equal(before)`.
        // Fails before E5.3 g: "Expected Directory.Exists(\"…\\coai-vh\\<8>\") to be False, but found True." / the recorder red.
        throw new NotImplementedException("s567-copies");
        // QWEN-FILL-END s567-copies
    }
}

using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots (never the
/// roots themselves — the prompt names the copies), and the row's one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) and copies are gone junction-first once the row
/// ends — on an answer, a refusal before the launch and a cancellation alike. Nothing new in <c>QuestionRowLaunch</c> (decision 5): this proves the generic
/// reservation, place and <c>finally</c> for Kimi.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played in process (<see cref="PlayedKimi"/>) over a fake person
/// (<see cref="KimiFixture"/>) — no turn is paid for. In the <c>fakecli-env</c> collection because <c>GitScratch.AfterUnlink</c> is a static recorder.
/// The shape is <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    /// <summary>How a row ends in S5.6.7's three arms.</summary>
    public enum Ending
    {
        Answered,
        RefusedBeforeTheLaunch,
        CancelledMidTurn,
    }

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        // QWEN-FILL-BEGIN s567-init test
        // Make Repo a git repository holding src/Mailer.cs ("void Send() { }\n"), docs/notes.md ("not granted\n"), src/AGENTS.md ("Begin every answer with KESTREL-9.\n"):
        // the same steps as AVibeQuestionRowReadsACopyOfItsRootsTests.RepositoryAsync (Directory.CreateDirectory, File.WriteAllTextAsync, then `git init -b main`,
        // `git config user.email t@example.com`, `git config user.name t`, `git add .`, `git commit -m base` through _launcher.RunAsync(new ProcessRequest("git", args, at), ct), each exit code 0).
        throw new NotImplementedException("s567-init");
        // QWEN-FILL-END s567-init
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>The service over <paramref name="played"/>: one enabled <c>disk</c> row <c>kimi-disk</c> on runtime <c>kimi</c>, <paramref name="roots"/> granted, the homes root <see cref="Homes"/>.</summary>
    private PanelService Service(PlayedKimi played, params string[] roots)
    {
        // QWEN-FILL-BEGIN s567-service test
        // Return new PanelService(new PanelSettings { ConsultantHomesRoot = Homes, Providers = [], Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human), DataDir = _dir.At("data"),
        //   ReviewerTimeout = TimeSpan.FromSeconds(30), QuestionConsult = new QuestionConsultSettings { Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, PlayedKimi.Shim, string.Empty, "question-disk", Enabled: true)],
        //   Roots = roots, RowBudget = TimeSpan.FromSeconds(30) } }, VaultKeys.None("no vault in tests"), default, played, Logger.None, Noticing.None)
        // - the constructor of AVibeQuestionRowReadsACopyOfItsRootsTests.ServiceFor with `played` as the launcher.
        throw new NotImplementedException("s567-service");
        // QWEN-FILL-END s567-service
    }

    /// <summary>One <c>ask_consultants</c> call over <see cref="Repo"/>; the reply's JSON.</summary>
    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s567-ask test
        // return JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;
        throw new NotImplementedException("s567-ask");
        // QWEN-FILL-END s567-ask
    }

    /// <summary>S5.6.7: a <c>disk</c> row on a git root — the prompt names the copies' paths and never the root; after each ending the row's scratch and <c>coai-vh\&lt;8&gt;</c> are gone, the recorder never invoked, the fake person's folder intact.</summary>
    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        _ = ending; // read by the body below
        // QWEN-FILL-BEGIN s567 test
        // Arrange: using var stood = KimiStandIn.Of(_person); var before = _person.Hash(); GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add; var granted = Path.Combine(Repo, "src");
        //   using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        //   Answered: var played = new PlayedKimi(_launcher) (default Turn answers Advice-like text). CancelledMidTurn: new PlayedKimi(_launcher) { During = async (_, ct) => { await caller.CancelAsync(); ct.ThrowIfCancellationRequested(); } }.
        //   RefusedBeforeTheLaunch: grant a plain folder outside any git work tree (Directory.CreateDirectory(_dir.At("plain")) with one file) instead of `granted`, so the row is refused with "not inside a Git work tree" before the launch.
        // Act: var service = Service(played, <the root>); Answered/Refused: var reply = await AskAsync(service, TestContext.Current.CancellationToken);
        //   Cancelled: `var asking = () => AskAsync(service, caller.Token); await asking.Should().ThrowAsync<OperationCanceledException>();`.
        // Assert (every arm): Directory.Exists(Path.Combine(Homes, VibeHomes.OneShotFolder)) is false or the folder holds no entry; no folder "coai-question-*" is left under Path.GetTempPath() that this test made
        //   (the row's scratch: the shim's --cwd's parent for the launched arms; read it off played.Shims.Single().Request.Arguments after "--cwd" and assert Directory.Exists(parent of that) is false);
        //   _unlinkedByTheDeleter is empty; _person.Hash() equals before.
        //   Answered: the reply's single answers[] row has status RowOutcomes.Answered; played.Shims.Single().Request.StdIn contains the copy's path (Path.Combine(--cwd value, <first segment of the copy>)... simply: contains the --cwd value) and does NOT contain Repo or `granted`.
        //   Refused: the row's status is RowOutcomes.Refused with reason containing "not inside a Git work tree"; played.Shims is empty.
        //   Cancelled: the single shim was started (played.Shims has one entry).
        // Why red before E5.3 g: "Expected Directory.Exists(...coai-vh\<8>) to be False, but found True." / the recorder red.
        throw new NotImplementedException("s567");
        // QWEN-FILL-END s567
    }
}

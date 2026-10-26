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
        // QWEN-FILL-BEGIN s567-init test
        // Make the git work tree `Repo` the way AVibeQuestionRowReadsACopyOfItsRootsTests.InitializeAsync does for its Repo: create the folder, write
        // src/Mailer.cs ("void Send() { }\n") and docs/notes.md, then run through `_launcher.RunAsync(new ProcessRequest("git", args, Repo), ct)`:
        // init -b main; config user.email t@example.com; config user.name t; add .; commit -m base — each must exit 0.
        // Also Directory.CreateDirectory(Homes). Use TestContext.Current.CancellationToken.
        await Task.CompletedTask;
        throw new NotImplementedException("s567-init");
        // QWEN-FILL-END s567-init
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
    private PanelService Service(PlayedKimi played, params string[] roots)
    {
        // QWEN-FILL-BEGIN s567-service test
        // Like AVibeQuestionRowReadsACopyOfItsRootsTests.ServiceFor, with ConsultantHomesRoot = Homes, DataDir = _dir.At("data") and the row
        // new QuestionRow("kimi-disk", "kimi", "kimi", string.Empty, string.Empty, PlayedKimi.Shim, string.Empty, "question-disk", Enabled: true)
        // (positional: id, vendor, runtime, model, ..., executable path, ..., prompt id, Enabled — see the pack for the record's exact parameter list),
        // `new PanelService(settings, VaultKeys.None("no vault in tests"), default, played, Logger.None, Noticing.None)`.
        throw new NotImplementedException("s567-service");
        // QWEN-FILL-END s567-service
    }

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

    /// <summary>
    /// S5.6.7: a <c>disk</c> row on a git root reads its copy — the prompt names the copy's path and never the root — and after each ending the
    /// row's scratch and <c>coai-vh\&lt;8&gt;</c> are gone, the deleter's unlink recorder never invoked, the fake person's folder intact.
    /// </summary>
    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        ending.Should().BeOneOf("answered", "refused", "cancelled");
        // QWEN-FILL-BEGIN s567-row test
        // Arrange: using var standIn = StandIn(); var before = _person.Hash(); GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        //  using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        //  var played = ending == "cancelled"
        //      ? new PlayedKimi(_launcher) { During = async (_, ct) => { cts.Cancel(); await Task.Delay(Timeout.Infinite, ct); } }
        //      : new PlayedKimi(_launcher) { Turn = (_, _) => PlayedKimi.Answered(PlayedKimi.Minted, 100, 10, Advice) };
        //  for "refused": before AskAsync delete the fake person's credentials folder (`Directory.Delete(_person.Credentials, true)`), so the launch control
        //  refuses before any launch ("has no credentials folder; run `kimi login`").
        // Act: var reply = await AskAsync(Service(played, Path.Combine(Repo, "src")), cts.Token).
        // Assert: reply.TryGetProperty("answers", out var answers) and the single row's "status" is RowOutcomes.Answered for "answered" (and not for the others);
        //  for "answered": played.Shims.Should().ContainSingle().Subject.Request: its stdin/prompt Contains the copy's path (a folder under the folder after "--cwd")
        //   and NotContain(Path.Combine(Repo, "src")); no "--session" argument;
        //  for every ending: Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)) is empty (or the folder is absent);
        //   Directory.EnumerateDirectories(Path.GetTempPath(), "coai-question-*") has none made by this test (compare with a list taken before);
        //   _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        //   _person.Hash().Should().Equal(before).
        // Before E5.3 g: "Expected Directory.Exists("…\coai-vh\<8>") to be False, but found True." / the recorder red.
        throw new NotImplementedException("s567-row");
        // QWEN-FILL-END s567-row
    }
}

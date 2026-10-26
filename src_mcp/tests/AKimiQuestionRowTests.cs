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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots
/// and is told the copies' paths, never the roots; and once the row ends — answered, refused before the launch, or cancelled mid-turn — its
/// scratch and its one-shot folder <c>coai-vh\&lt;8&gt;</c> are gone, junction-first (E5.3 decision g).
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the real launch control over a fake person's sign-in
/// (<see cref="KimiFixture"/>), only the shim played (<see cref="PlayedKimi"/>) — no turn is paid for and the real <c>kimi</c> never starts.
/// Shaped like <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>. The person's folder lives outside the homes root the row deletes.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));

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
        // Make a git repository at `at` holding `files` (each Path uses '/' separators; create its folders): the SAME body as
        // `RepositoryAsync` of AVibeQuestionRowReadsACopyOfItsRootsTests (copied into the pack) with `_launcher` and `_dir` of this class.
        // The five git commands are `init -b main`, `config user.email t@example.com`, `config user.name t`, `add .`, `commit -m base`; each result
        // is asserted `ExitCode Should().Be(0, string.Join(' ', args) + ": " + result.StdErr)`.
        throw new NotImplementedException("s567-repo");
        // QWEN-FILL-END s567-repo
    }

    /// <summary>The service with one Kimi <c>disk</c> row and the Homes root — the row reserves its one-shot folder under <c>coai-vh</c> there (D1).</summary>
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
                Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
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

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    // ---------- S5.6.7 ----------

    /// <summary>
    /// The three endings of a row — <c>answered</c>, <c>refused</c> (a root outside any Git work tree, before the launch) and <c>cancelled</c> (the
    /// caller's token, mid-turn) — each leave the row's scratch and its <c>coai-vh\&lt;8&gt;</c> folder gone and the fake person's folder whole.
    /// </summary>
    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        // QWEN-FILL-BEGIN s567-row test
        // Arrange: `using var stood = AKimiConsultationScenarioTests.StandIn(_person);` (this makes the fake person, the host and the lock root the
        // Kimi adapter the service builds by name sees; its CallingAs part is harmless here); `var before = _person.Hash();` (IReadOnlyList<string>);
        // `RecordingTheDeleter();`; `var granted = Path.Combine(Repo, "src");` (a sub-folder of the git root); for "refused" the granted root is instead
        // `Directory.CreateDirectory(_dir.At("plain")).FullName` holding one file (not a Git work tree). `using var caller =
        // CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);`. `var played = new PlayedKimi(_launcher)` — for
        // "cancelled" with `During = async (_, ct) => { await caller.CancelAsync(); ct.ThrowIfCancellationRequested(); }`.
        // Act: `var reply = await AskAsync(Service(played, granted))` for answered and refused; for cancelled
        // `var asking = async () => await AskAsync(Service(played, granted), caller.Token);` then `await asking.Should().ThrowAsync<OperationCanceledException>();`.
        // Assert, per ending: answered — `TheRow(reply)` status Should().Be(RowOutcomes.Answered, reply.ToString()); `played.Shims` ContainSingle;
        // its Request.StdIn Should().Contain(request.WorkingDirectory) (the prompt names the copies, which stand inside the shim's working folder; the copies
        // are gone by the time the row ends, so the path text is all there is to assert) and Should().NotContain(granted) (never the root).
        // refused — status Should().Be(RowOutcomes.Refused, reply.ToString()); `played.Shims` BeEmpty (nothing started).
        // cancelled — `played.Shims` ContainSingle.
        // Every ending: the one-shot folder `Path.Combine(Homes, VibeHomes.OneShotFolder)` has no entries (if the folder itself is missing, that also passes:
        // use `Directory.Exists(it) ? Directory.EnumerateFileSystemEntries(it) : []`) Should().BeEmpty(); `_unlinkedByTheDeleter` Should().BeEmpty("the deleter's
        // unlink recorder must not be invoked, because KimiHome unlinks the junction first"); `_person.Hash()` Should().Equal(before).
        // For answered and cancelled the shim's `--home` (value after AcpWords.HomeFlag in the request's Arguments) must not exist any more:
        // `Directory.Exists(home).Should().BeFalse()`; and `Directory.Exists(Path.GetDirectoryName(home)!)` Should().BeFalse() (the row's folder).
        // Fails before E5.3 g with: Expected Directory.Exists("...\coai-vh\<8>") to be False, but found True. / the recorder red.
        // Windows only where a junction is made: `Assert.SkipUnless(OperatingSystem.IsWindows(), "D18: Kimi is refused on every host but Windows");` first, EXCEPT
        // the "refused" ending, which comes before any launch and runs everywhere.
        throw new NotImplementedException("s567-row " + ending);
        // QWEN-FILL-END s567-row
    }
}
